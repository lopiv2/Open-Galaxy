using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Ambient desert fauna herds (banthas on Tatooine).
// Each entry in the "creatures" block of PlanetPOIs.json is one herd: a
// random size between herdMin and herdMax, spawned inside herdRadius around
// (x, z). Every herd is driven as a single entity: the herd picks one shared
// roaming target and all its members walk towards it in the same direction,
// keeping their formation slot and following the terrain. The walk cycle is
// wired in the prefab, so the runtime only moves and rotates the herd members.
public class PlanetCreatures : MonoBehaviour
{
    // One herd: a shared roaming target plus the members walking towards it.
    private class Herd
    {
        public Vector3 home;
        public Vector3 position;
        public Vector3 target;
        public Vector3 dir;
        public float wanderRadius;
        public float speed;
        public System.Random rng;
    }

    private class HerdMember
    {
        public Transform transform;
        public Herd herd;
        // Formation slot relative to the herd position. Constant in world
        // space, so the whole herd translates in the same direction.
        public Vector3 offset;
        public float groundY;
        public float groundOffset;
        public float groundTimer;
    }

    private readonly List<Herd> herds = new List<Herd>();
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

        // Pick the spawn layout first so the herd has one shared centre from
        // the very first frame; every member then walks the same way.
        System.Random layoutRng = new System.Random(herd.seed * 31 + 7);
        List<Vector3> spawnPoints = new List<Vector3>(count);
        Vector3 centroid = Vector3.zero;

        for (int i = 0; i < count; i++)
        {
            Vector3 point = PickSpawnPoint(home, herdRadius, layoutRng);
            spawnPoints.Add(point);
            centroid += point;
        }

        centroid /= count;
        centroid.y = home.y;

        Herd herdGroup = new Herd();
        herdGroup.home = home;
        herdGroup.position = centroid;
        herdGroup.target = centroid;
        herdGroup.dir = Vector3.forward;
        herdGroup.wanderRadius = wanderRadius;
        herdGroup.speed = speed;
        herdGroup.rng = new System.Random(herd.seed * 7919 + 13);
        PickHerdTarget(herdGroup);

        for (int i = 0; i < count; i++)
        {
            Vector3 spawn = spawnPoints[i];
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
            member.herd = herdGroup;
            member.offset = new Vector3(spawn.x - herdGroup.position.x, 0f, spawn.z - herdGroup.position.z);
            member.groundY = spawnGround;
            member.groundOffset = groundOffset;
            member.groundTimer = (float)rng.NextDouble() * 0.3f;
            members.Add(member);
        }

        herds.Add(herdGroup);
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

        // Advance every herd as a single entity. Members then steer towards
        // their formation slot, so the whole herd walks one shared direction.
        for (int h = 0; h < herds.Count; h++)
        {
            Herd herd = herds[h];
            Vector3 toTarget = herd.target - herd.position;
            toTarget.y = 0f;

            if (toTarget.sqrMagnitude < 9f)
            {
                PickHerdTarget(herd);
                toTarget = herd.target - herd.position;
                toTarget.y = 0f;
            }

            if (toTarget.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            herd.dir = toTarget.normalized;
            herd.position += herd.dir * herd.speed * dt;

            Vector3 fromHome = herd.position - herd.home;
            fromHome.y = 0f;

            if (fromHome.sqrMagnitude > herd.wanderRadius * herd.wanderRadius)
            {
                herd.position = herd.home + fromHome.normalized * herd.wanderRadius;
                PickHerdTarget(herd);
            }
        }

        for (int i = 0; i < members.Count; i++)
        {
            HerdMember m = members[i];

            if (m.transform == null || m.herd == null)
            {
                continue;
            }

            Vector3 position = m.transform.position;
            Herd herd = m.herd;
            Vector3 slot = herd.position + m.offset;
            Vector3 toSlot = slot - position;
            toSlot.y = 0f;
            float slotDistance = toSlot.magnitude;
            Vector3 desired = herd.dir;

            if (slotDistance > 0.75f)
            {
                Vector3 towardSlot = toSlot / slotDistance;
                // Bias towards the shared heading so a member that overshoots
                // its slot keeps walking with the herd instead of turning back.
                Vector3 blended = (towardSlot + herd.dir * 0.5f).normalized;

                if (Vector3.Dot(blended, herd.dir) >= 0.25f)
                {
                    desired = blended;
                }
            }

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
                    // Everyone keeps the herd pace; only members that fell
                    // behind their slot speed up a little to rejoin.
                    float speed = herd.speed;
                    float along = Vector3.Dot(toSlot, herd.dir);

                    if (along > 1f)
                    {
                        speed += Mathf.Min(herd.speed * 0.6f, along * 0.5f);
                    }

                    position += forward.normalized * speed * dt;
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
        herds.Clear();
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

    private static void PickHerdTarget(Herd herd)
    {
        Vector3 fromHome = herd.position - herd.home;
        fromHome.y = 0f;
        Vector3 heading = herd.target - herd.home;
        heading.y = 0f;

        float baseAngle;

        if (fromHome.sqrMagnitude > herd.wanderRadius * herd.wanderRadius * 0.49f)
        {
            // Close to the edge of the roaming area: head back inwards.
            Vector3 inward = -fromHome.normalized;
            baseAngle = Mathf.Atan2(inward.x, inward.z) + (float)((herd.rng.NextDouble() - 0.5) * Mathf.PI * 0.9);
        }
        else if (heading.sqrMagnitude > 1f)
        {
            // Keep the previous general direction instead of doubling back.
            baseAngle = Mathf.Atan2(heading.x, heading.z) + (float)((herd.rng.NextDouble() - 0.5) * Mathf.PI * 0.8);
        }
        else
        {
            baseAngle = (float)(herd.rng.NextDouble() * Mathf.PI * 2.0);
        }

        float distance = herd.wanderRadius * (0.55f + (float)herd.rng.NextDouble() * 0.45f);
        Vector3 direction = new Vector3(Mathf.Sin(baseAngle), 0f, Mathf.Cos(baseAngle));

        herd.target = herd.home + direction * distance;
        herd.target.y = herd.home.y;
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
