using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Ambient desert fauna herds (banthas on Tatooine).
// Each entry in the "creatures" block of PlanetPOIs.json is one herd: a
// random size between herdMin and herdMax, spawned inside herdRadius around
// (x, z). Members roam slowly around the herd centre and follow the terrain.
// The walk cycle is wired in the prefab (BanthaController), so the runtime
// only moves and rotates the herd members.
public class PlanetCreatures : MonoBehaviour
{
    private class HerdMember
    {
        public Transform transform;
        public System.Random rng;
        public Vector3 home;
        public Vector3 target;
        public float wanderRadius;
        public float speed;
        public float groundY;
        public float groundOffset;
        public float groundTimer;
    }

    private readonly List<HerdMember> members = new List<HerdMember>();
    private readonly List<GameObject> spawned = new List<GameObject>();

    // Called by PlanetNavigation once the planet scene is ready. The manager
    // is parented to the navigation object so it is destroyed on planet exit.
    public static void Begin(Transform parent, Scene scene, PlanetPOILocation location)
    {
        if (scene == null || location == null || location.creatures == null || location.creatures.Length == 0)
        {
            return;
        }

        GameObject go = new GameObject("PlanetCreatures");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;

        PlanetCreatures creatures = go.AddComponent<PlanetCreatures>();
        creatures.StartCoroutine(creatures.SpawnHerds(location));
    }

    private IEnumerator SpawnHerds(PlanetPOILocation location)
    {
        foreach (PlanetPOICreature herd in location.creatures)
        {
            if (herd == null || string.IsNullOrEmpty(herd.prefab))
            {
                continue;
            }

            yield return StartCoroutine(SpawnHerd(herd));
        }
    }

    private IEnumerator SpawnHerd(PlanetPOICreature herd)
    {
        GameObject prefab = Resources.Load<GameObject>("objects/creatures/" + herd.prefab);

        if (prefab == null)
        {
            Debug.LogWarning("[PlanetCreatures] Creature prefab not found: " + herd.prefab);
            yield break;
        }

        Scene scene = SceneFunctions.GetScene();

        if (scene == null)
        {
            yield break;
        }

        System.Random rng = new System.Random(herd.seed);
        int min = Mathf.Max(1, herd.herdMin);
        int max = Mathf.Max(min, herd.herdMax);
        // Inclusive random herd size (3-6 by default).
        int count = min + rng.Next(max - min + 1);
        float herdRadius = Mathf.Max(herd.herdRadius, 1f);
        float wanderRadius = Mathf.Max(herd.wanderRadius, herdRadius);
        float speed = herd.speed <= 0f ? 2.2f : herd.speed;
        float groundOffset = GetGroundOffset(prefab);
        Vector3 home = new Vector3(scene.transform.position.x + herd.x, 0f, scene.transform.position.z + herd.z);

        // Wait for the tiles under the herd before placing the first member.
        float groundY = float.NaN;
        float waited = 0f;

        while (float.IsNaN(groundY) && waited < 30f)
        {
            groundY = SampleGroundHeight(home);
            if (float.IsNaN(groundY))
            {
                waited += 0.5f;
                yield return new WaitForSeconds(0.5f);
                scene = SceneFunctions.GetScene();

                if (scene == null)
                {
                    yield break;
                }
            }
        }

        if (float.IsNaN(groundY))
        {
            Debug.LogWarning("[PlanetCreatures] No ground for herd " + herd.prefab + ", skipped.");
            yield break;
        }

        for (int i = 0; i < count; i++)
        {
            Vector3 spawn = PickSpawnPoint(home, herdRadius, rng);
            float spawnGround = SampleGroundHeight(spawn);

            if (float.IsNaN(spawnGround))
            {
                spawnGround = groundY;
            }

            GameObject go = Instantiate(prefab);
            go.name = "Creature_" + herd.prefab + "_" + i;
            go.transform.SetParent(scene.transform, true);
            go.transform.position = new Vector3(spawn.x, spawnGround + groundOffset, spawn.z);
            go.transform.rotation = Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f);
            spawned.Add(go);

            Animator animator = go.GetComponentInChildren<Animator>(true);

            if (animator != null)
            {
                // Desync the herd and vary the gait a little.
                animator.Play("Unreal Take", 0, (float)rng.NextDouble());
                animator.speed = 0.9f + (float)rng.NextDouble() * 0.2f;
            }

            HerdMember member = new HerdMember();
            member.transform = go.transform;
            member.rng = new System.Random(herd.seed * 397 + i + 1);
            member.home = home;
            member.wanderRadius = wanderRadius;
            member.speed = speed * (0.85f + (float)rng.NextDouble() * 0.3f);
            member.groundY = spawnGround;
            member.groundOffset = groundOffset;
            member.groundTimer = (float)rng.NextDouble() * 0.3f;
            PickTarget(member);
            members.Add(member);
        }
    }

    private void Update()
    {
        if (members.Count == 0)
        {
            return;
        }

        if (GalaxyPlanetEntry.IsOnPlanet == false || GalaxyPlanetEntry.IsTransitioning)
        {
            return;
        }

        if (GalaxyMapController.IsMapOpen || GalaxyMapController.IsTraveling)
        {
            return;
        }

        float dt = Time.deltaTime;

        for (int i = 0; i < members.Count; i++)
        {
            HerdMember m = members[i];

            if (m.transform == null)
            {
                continue;
            }

            Vector3 position = m.transform.position;
            Vector3 toTarget = m.target - position;
            toTarget.y = 0f;

            if (toTarget.sqrMagnitude < 4f)
            {
                PickTarget(m);
                toTarget = m.target - position;
                toTarget.y = 0f;
            }

            Vector3 desired = toTarget.normalized;

            // Soft separation so herd members do not stack on each other.
            Vector3 separation = Vector3.zero;

            for (int j = 0; j < members.Count; j++)
            {
                if (j == i || members[j].transform == null)
                {
                    continue;
                }

                Vector3 away = position - members[j].transform.position;
                away.y = 0f;
                float sq = away.sqrMagnitude;

                if (sq > 0.0001f && sq < 9f)
                {
                    separation += away.normalized * (3f - Mathf.Sqrt(sq));
                }
            }

            if (separation.sqrMagnitude > 0.0001f)
            {
                desired = (desired + separation * 0.6f).normalized;
            }

            if (desired.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(desired, Vector3.up);
                m.transform.rotation = Quaternion.RotateTowards(m.transform.rotation, targetRotation, 45f * dt);

                Vector3 forward = m.transform.forward;
                forward.y = 0f;

                if (forward.sqrMagnitude > 0.001f)
                {
                    position += forward.normalized * m.speed * dt;
                }
            }

            // Terrain follow at ~4 Hz, smoothed between samples.
            m.groundTimer -= dt;

            if (m.groundTimer <= 0f)
            {
                m.groundTimer = 0.25f;
                float ground = SampleGroundHeight(position);

                if (float.IsNaN(ground) == false)
                {
                    m.groundY = ground;
                }
            }

            position.y = Mathf.Lerp(position.y, m.groundY + m.groundOffset, 1f - Mathf.Exp(-10f * dt));
            m.transform.position = position;
        }
    }

    private void OnDestroy()
    {
        // Planet exit destroys the navigation object (our parent), so take the
        // herds with it instead of leaving them behind in the scene.
        foreach (GameObject go in spawned)
        {
            if (go != null)
            {
                Destroy(go);
            }
        }

        spawned.Clear();
        members.Clear();
    }

    private static Vector3 PickSpawnPoint(Vector3 home, float radius, System.Random rng)
    {
        double angle = rng.NextDouble() * Mathf.PI * 2.0;
        double distance = radius * Mathf.Sqrt((float)rng.NextDouble());

        return new Vector3(
            home.x + (float)(System.Math.Cos(angle) * distance),
            0f,
            home.z + (float)(System.Math.Sin(angle) * distance));
    }

    private static void PickTarget(HerdMember member)
    {
        double angle = member.rng.NextDouble() * Mathf.PI * 2.0;
        double distance = member.wanderRadius * Mathf.Sqrt((float)member.rng.NextDouble());

        member.target = new Vector3(
            member.home.x + (float)(System.Math.Cos(angle) * distance),
            member.home.y,
            member.home.z + (float)(System.Math.Sin(angle) * distance));
    }

    // Lifts the prefab so its feet sit exactly on the sampled ground.
    // The model pivot is above the soles, so the renderer min Y is used.
    private static float GetGroundOffset(GameObject prefab)
    {
        SkinnedMeshRenderer[] renderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        bool found = false;
        float lowest = 0f;

        foreach (SkinnedMeshRenderer renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            Vector3 min = renderer.transform.TransformPoint(renderer.localBounds.min);

            if (found == false)
            {
                lowest = min.y;
                found = true;
            }
            else
            {
                lowest = Mathf.Min(lowest, min.y);
            }
        }

        return found == true ? -lowest : 0.97f;
    }

    // Same rule as the rest of the planet systems: only terrain tiles count.
    private static float SampleGroundHeight(Vector3 worldPosition)
    {
        // Start high enough for the tallest terrain (planet max ~1000 m) even
        // when the probe carries a y of 0 (spawn points use XZ only).
        float probeY = Mathf.Max(worldPosition.y + 400f, 2000f);
        Vector3 origin = new Vector3(worldPosition.x, probeY, worldPosition.z);
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, probeY + 2000f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;

        foreach (RaycastHit hit in hits)
        {
            if (hit.transform == null || hit.transform.name.StartsWith("Tile_") == false)
            {
                continue;
            }

            if (hit.point.y > best)
            {
                best = hit.point.y;
            }
        }

        return float.IsNegativeInfinity(best) ? float.NaN : best;
    }
}
