using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Planet-global coordinates + POI navigation (V1).
// Frame: planet-local meters = scene-local XZ while on planet (the scene
// anchor is frozen at zero during planet flight, so this frame is stable).
// Origin: per-planet entry gate from PlanetPOIs.json (Tatooine: 0,0).
// POIs get ground-clamped beacon pillars + HUD waypoint routing to the
// nearest unvisited POI (H cycles). Cleanup on exit/hyperspace.
public class PlanetNavigation : MonoBehaviour
{
    private static PlanetNavigation active;
    private static PlanetPOIs cachedDefs;

    private string planetName;
    private PlanetPOILocation location;
    private readonly List<GameObject> beacons = new List<GameObject>();
    private readonly List<float> poiGroundY = new List<float>();
    private readonly HashSet<string> visited = new HashSet<string>();
    // Props registered as R-targets (removed from the pool on cleanup).
    private readonly List<GameObject> propObjects = new List<GameObject>();
    private Scene boundScene;
    private int targetIndex = -1;
    private float hudTimer;

    // Public access for the on-foot compass (GroundCompass).
    public static PlanetNavigation Active { get { return active; } }
    public bool HasPois { get { return location != null && location.pois != null && location.pois.Length > 0; } }
    public int PoiCount { get { return HasPois ? location.pois.Length : 0; } }
    public int SelectedPoiIndex { get { return targetIndex; } }

    public string GetPoiName(int index)
    {
        if (!HasPois || index < 0 || index >= location.pois.Length || location.pois[index] == null)
        {
            return "";
        }

        return location.pois[index].name;
    }

    public bool IsPoiVisited(int index)
    {
        string name = GetPoiName(index);
        return string.IsNullOrEmpty(name) == false && visited.Contains(name);
    }

    // World position of a POI. Y comes from the cached ground sample taken
    // when the beacon was spawned (scene anchor is frozen while on planet).
    public Vector3 GetPoiWorldPosition(int index)
    {
        Scene scene = SceneFunctions.GetScene();

        if (scene == null || !HasPois || index < 0 || index >= location.pois.Length)
        {
            return Vector3.zero;
        }

        PlanetPOIEntry poi = location.pois[index];

        if (poi == null)
        {
            return Vector3.zero;
        }

        float groundY = (index < poiGroundY.Count && !float.IsNaN(poiGroundY[index]))
            ? poiGroundY[index]
            : 0f;

        return new Vector3(
            scene.transform.position.x + poi.x,
            scene.transform.position.y + groundY,
            scene.transform.position.z + poi.z);
    }

    public void SelectPoi(int index)
    {
        if (!HasPois)
        {
            return;
        }

        int count = location.pois.Length;
        targetIndex = ((index % count) + count) % count;
        ApplyWaypoint();
    }

    // -1 previous, +1 next. Used by the compass Q/E (and H keeps working
    // through CycleTarget).
    public void StepPoi(int direction)
    {
        if (!HasPois)
        {
            return;
        }

        SelectPoi(targetIndex + (direction >= 0 ? 1 : -1));
    }

    // Deterministic spawn XZ for the planet. Falls back to 0,0.
    public static Vector2 GetEntryGate(string planetName)
    {
        PlanetPOILocation def = GetLocationDef(planetName);

        if (def == null)
        {
            return Vector2.zero;
        }

        return new Vector2(def.entryX, def.entryZ);
    }

    public static void BeginForPlanet(Scene scene, string planetName)
    {
        EndAll();

        if (scene == null || string.IsNullOrEmpty(planetName))
        {
            return;
        }

        GameObject go = new GameObject("PlanetNavigation");
        go.transform.SetParent(scene.transform, false);
        go.transform.localPosition = Vector3.zero;
        PlanetNavigation nav = go.AddComponent<PlanetNavigation>();
        nav.planetName = planetName;
        nav.boundScene = scene;
        active = nav;
    }

    public static void EndAll()
    {
        Hud hud = HudFunctions.GetHud();

        if (hud != null)
        {
            hud.waypointActive = false;
            hud.waypointTitle = "";
        }

        if (active != null)
        {
            // Unregister prop targets before destroying them.
            if (active.boundScene != null && active.boundScene.objectPool != null)
            {
                foreach (GameObject prop in active.propObjects)
                {
                    if (prop != null)
                    {
                        active.boundScene.objectPool.Remove(prop);
                    }
                }
            }

            active.propObjects.Clear();

            foreach (GameObject beacon in active.beacons)
            {
                if (beacon != null)
                {
                    GameObject.Destroy(beacon);
                }
            }

            active.beacons.Clear();

            if (active.gameObject != null)
            {
                GameObject.Destroy(active.gameObject);
            }

            active = null;
        }
    }

    private IEnumerator Start()
    {
        location = GetLocationDef(planetName);

        Scene scene = SceneFunctions.GetScene();

        if (scene == null)
        {
            yield break;
        }

        // Ambient fauna herds (JSON "creatures" block). This is independent of
        // the POI navigation, so it also works on planets without POIs.
        if (location != null && location.creatures != null && location.creatures.Length > 0)
        {
            PlanetCreatures.Begin(transform, scene, location);
        }

        bool hasPois = location != null && location.pois != null && location.pois.Length > 0;

        if (!hasPois)
        {
            Debug.Log("[PlanetNav] No POIs defined for " + planetName + " (flying free).");
        }
        else
        {
            // Spawn one beacon per POI once its tile has colliders (tiles build
            // over several frames). Timeout per POI so one bad spot can't stall.
            foreach (PlanetPOIEntry poi in location.pois)
            {
                if (poi == null || string.IsNullOrEmpty(poi.name))
                {
                    poiGroundY.Add(float.NaN);
                    continue;
                }

                float groundY = float.NaN;
                float waited = 0f;

                while (float.IsNaN(groundY) && waited < 20f)
                {
                    Vector3 probe = new Vector3(
                        scene.transform.position.x + poi.x,
                        2000f,
                        scene.transform.position.z + poi.z);
                    groundY = SampleGroundHeight(probe);
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
                    Debug.LogWarning("[PlanetNav] No ground under POI " + poi.name + ", beacon skipped.");
                    poiGroundY.Add(float.NaN);
                    continue;
                }

                poiGroundY.Add(groundY);
                SpawnBeacon(scene, poi, groundY);
            }
        }

        // Static surface content: isolated landmarks (props[]) and generated
        // towns/outposts (sites[]). Both use PlanetContentUtility so the
        // editor preview and the game produce the same layout.
        int relaxedPlacements;
        List<string> placementWarnings;
        List<PlanetPropPlacement> placements = PlanetContentUtility.BuildPlacements(
            location, out relaxedPlacements, out placementWarnings);

        foreach (string warning in placementWarnings)
            Debug.LogWarning("[PlanetNav] " + warning);

        if (relaxedPlacements > 0)
            Debug.Log("[PlanetNav] " + relaxedPlacements
                + " props colocados con separación reducida.");

        foreach (PlanetPropPlacement placement in placements)
        {
            yield return StartCoroutine(SpawnPropInstance(
                placement.type, placement.x, placement.z,
                placement.rotY, placement.scaleMul));
            scene = SceneFunctions.GetScene();

            if (scene == null)
            {
                yield break;
            }
        }

        if (hasPois)
            RetargetNearest(true);
    }

    // Waits for the tile under (x, z), then spawns one ground-clamped prop.
    private IEnumerator SpawnPropInstance(string propType, float x, float z, float rotY, float scaleMul)
    {
        Scene scene = SceneFunctions.GetScene();

        if (scene == null)
        {
            yield break;
        }

        float groundY = float.NaN;
        float waited = 0f;

        while (float.IsNaN(groundY) && waited < 20f)
        {
            Vector3 probe = new Vector3(
                scene.transform.position.x + x,
                2000f,
                scene.transform.position.z + z);
            groundY = SampleGroundHeight(probe);
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
            Debug.LogWarning("[PlanetNav] No ground for prop " + propType + ", skipped.");
            yield break;
        }

        GameObject prefab = null;
        PropType propDef = GetPropDef(propType);
        string prefabFolder = (propDef != null && !string.IsNullOrEmpty(propDef.folder))
            ? propDef.folder
            : "objects/vehicles/";
        string prefabName = propDef != null && !string.IsNullOrEmpty(propDef.prefab)
            ? propDef.prefab
            : propType;

        prefab = Resources.Load<GameObject>(prefabFolder + prefabName);

        if (prefab == null)
        {
            Debug.LogWarning("[PlanetNav] Prop prefab not found: " + prefabFolder + prefabName);
            yield break;
        }

        Quaternion baseRotation = prefab.transform.localRotation;
        GameObject go = GameObject.Instantiate(prefab);
        go.name = "POIProp_" + propType;

        go.transform.SetParent(scene.transform, true);
        go.transform.position = new Vector3(
            scene.transform.position.x + x,
            scene.transform.position.y + groundY,
            scene.transform.position.z + z);
        // Keep the prefab's own correction rotation (e.g. -90 on X) and add
        // the placement yaw on top. Assigning rotation directly used to
        // discard the correction and show the model at its original angle.
        go.transform.localRotation = baseRotation;
        // Canon scale from PropsTypes.json: largest dimension matches
        // targetLength (native FBX sizes vary). Measure before yaw so the
        // placement angle cannot change the resulting scale.
        if (propDef != null && propDef.targetLength > 0f)
        {
            Bounds rawBounds = GetPropBounds(go);
            float longest = Mathf.Max(rawBounds.size.x, rawBounds.size.y, rawBounds.size.z);

            if (longest > 0.01f)
            {
                go.transform.localScale *= propDef.targetLength / longest;
            }
        }

        go.transform.localScale *= scaleMul;
        // Ground by bounds, not pivot: model origins are arbitrary
        // (the Sandcrawler hangs ~16 m below its own pivot).
        // Y-rotation keeps Y extents, so one lift is exact.
        Bounds bb = GetPropBounds(go);
        float lift = (scene.transform.position.y + groundY) - bb.min.y;
        go.transform.position += Vector3.up * lift;
        go.transform.localRotation = Quaternion.Euler(0f, rotY, 0f) * baseRotation;
        beacons.Add(go);
        // Selectable with R like any ship (enemy lock-ons skip it:
        // no Ship component, no allegiance).
        if (scene.objectPool != null && !scene.objectPool.Contains(go))
        {
            scene.objectPool.Add(go);
        }

        propObjects.Add(go);
    }

    private void Update()
    {
        if (active != this || GalaxyPlanetEntry.IsOnPlanet == false || GalaxyPlanetEntry.IsTransitioning)
        {
            return;
        }

        if (GalaxyMapController.IsMapOpen || GalaxyMapController.IsTraveling)
        {
            return;
        }

        Scene scene = SceneFunctions.GetScene();

        if (scene == null || scene.mainShip == null || location == null || location.pois == null)
        {
            return;
        }

        // Cycle POI target (H). Gamepad: pending (sticks/buttons taken).
        var keyboard = UnityEngine.InputSystem.Keyboard.current;

        if (keyboard != null && keyboard.hKey.wasPressedThisFrame)
        {
            CycleTarget();
        }

        // Arrival check at ~5 Hz.
        hudTimer += Time.deltaTime;

        if (hudTimer >= 0.2f)
        {
            hudTimer = 0f;
            CheckArrival(scene);
        }
    }

    private void CheckArrival(Scene scene)
    {
        if (targetIndex < 0 || targetIndex >= location.pois.Length)
        {
            return;
        }

        PlanetPOIEntry poi = location.pois[targetIndex];

        if (poi == null || visited.Contains(poi.name))
        {
            return;
        }

        Vector3 shipPos = scene.mainShip.transform.position;
        Vector3 poiPos = new Vector3(
            scene.transform.position.x + poi.x,
            shipPos.y,
            scene.transform.position.z + poi.z);
        float flatDist = Vector3.Distance(shipPos, poiPos);

        if (flatDist <= Mathf.Max(poi.radius, 50f))
        {
            visited.Add(poi.name);
            HudFunctions.AddToMissionLog("ARRIVED AT " + poi.name);
            RetargetNearest(false);
        }
    }

    private void CycleTarget()
    {
        if (!HasPois)
        {
            return;
        }

        StepPoi(1);
    }

    private void RetargetNearest(bool first)
    {
        Scene scene = SceneFunctions.GetScene();

        if (scene == null || scene.mainShip == null)
        {
            return;
        }

        Vector3 shipPos = scene.mainShip.transform.position;
        float best = float.MaxValue;
        int bestIdx = -1;

        for (int i = 0; i < location.pois.Length; i++)
        {
            PlanetPOIEntry poi = location.pois[i];

            if (poi == null)
            {
                continue;
            }

            if (!first && visited.Contains(poi.name) && AnyUnvisited())
            {
                continue;
            }

            float d = Vector2.Distance(
                new Vector2(shipPos.x, shipPos.z),
                new Vector2(scene.transform.position.x + poi.x, scene.transform.position.z + poi.z));

            if (d < best)
            {
                best = d;
                bestIdx = i;
            }
        }

        if (bestIdx >= 0)
        {
            targetIndex = bestIdx;
            ApplyWaypoint();
        }
    }

    private bool AnyUnvisited()
    {
        foreach (PlanetPOIEntry poi in location.pois)
        {
            if (poi != null && !visited.Contains(poi.name))
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyWaypoint()
    {
        Scene scene = SceneFunctions.GetScene();
        Hud hud = HudFunctions.GetHud();

        if (scene == null || scene.mainShip == null || hud == null)
        {
            return;
        }

        if (targetIndex < 0 || targetIndex >= location.pois.Length)
        {
            return;
        }

        PlanetPOIEntry poi = location.pois[targetIndex];

        if (poi == null)
        {
            return;
        }

        Ship ship = scene.mainShip.GetComponent<Ship>();

        if (ship == null || ship.targetingSystem == null)
        {
            return;
        }

        if (ship.targetingSystem.waypoint == null)
        {
            TargetingSystemFunctions.CreateWaypoint_SmallShipPlayer(ship.targetingSystem);
        }

        float groundY = (targetIndex < poiGroundY.Count && !float.IsNaN(poiGroundY[targetIndex]))
            ? poiGroundY[targetIndex]
            : 100f;

        ship.targetingSystem.waypoint.transform.position = new Vector3(
            scene.transform.position.x + poi.x,
            scene.transform.position.y + groundY + 60f,
            scene.transform.position.z + poi.z);

        hud.waypointActive = true;
        // The on-foot compass already names the selected POI, so the old
        // "NAV: <POI>" floating title and mission-log line are redundant.
        // The waypoint marker itself (and its distance) stays.
        hud.waypointTitle = "";
    }

    private void SpawnBeacon(Scene scene, PlanetPOIEntry poi, float groundY)
    {
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");

        if (unlit == null)
        {
            unlit = Shader.Find("Unlit/Color");
        }

        Material mat = new Material(unlit);
        mat.color = new Color(1f, 0.72f, 0.25f, 0.32f);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.SetFloat("_Cull", 0f);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

        GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pillar.name = "POIBeacon_" + poi.name;
        pillar.transform.SetParent(scene.transform, true);
        pillar.transform.position = new Vector3(
            scene.transform.position.x + poi.x,
            scene.transform.position.y + groundY + 750f,
            scene.transform.position.z + poi.z);
        pillar.transform.localScale = new Vector3(50f, 750f, 50f);

        Collider col = pillar.GetComponent<Collider>();

        if (col != null)
        {
            GameObject.Destroy(col);
        }

        Renderer renderer = pillar.GetComponent<Renderer>();
        renderer.sharedMaterial = mat;
        beacons.Add(pillar);
    }

    // Union of all renderer bounds (handles multi-mesh props).
    private static Bounds GetPropBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
        Bounds bb = new Bounds(go.transform.position, Vector3.zero);
        bool init = false;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            if (!init)
            {
                bb = renderer.bounds;
                init = true;
            }
            else
            {
                bb.Encapsulate(renderer.bounds);
            }
        }

        return bb;
    }

    // Same tile-only sampling as entry: NaN while tiles aren't ready.
    private static float SampleGroundHeight(Vector3 worldPosition)
    {
        Vector3 origin = new Vector3(worldPosition.x, worldPosition.y + 400f, worldPosition.z);
        float rayLength = 4000f;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, rayLength, 1 << 0);
        float bestHeight = float.NegativeInfinity;

        foreach (RaycastHit hit in hits)
        {
            if (hit.transform == null || hit.transform.name.StartsWith("Tile_") == false)
            {
                continue;
            }

            if (hit.point.y > bestHeight)
            {
                bestHeight = hit.point.y;
            }
        }

        if (float.IsNegativeInfinity(bestHeight))
        {
            return float.NaN;
        }

        return bestHeight;
    }

    private static PlanetPOILocation GetLocationDef(string planetName)
    {
        if (string.IsNullOrEmpty(planetName))
        {
            return null;
        }

        if (cachedDefs == null)
        {
            TextAsset file = Resources.Load(OGGetAddress.files + "PlanetPOIs") as TextAsset;

            if (file == null)
            {
                return null;
            }

            cachedDefs = JsonUtility.FromJson<PlanetPOIs>(file.text);
        }

        if (cachedDefs == null || cachedDefs.planetPOIsData == null)
        {
            return null;
        }

        foreach (PlanetPOILocation loc in cachedDefs.planetPOIsData)
        {
            if (loc != null && loc.planet == planetName)
            {
                return loc;
            }
        }

        return null;
    }

    private static PropTypes cachedProps;

    private static PropType GetPropDef(string prefabName)
    {
        if (string.IsNullOrEmpty(prefabName))
        {
            return null;
        }

        if (cachedProps == null)
        {
            TextAsset file = Resources.Load(OGGetAddress.files + "PropsTypes") as TextAsset;

            if (file == null)
            {
                return null;
            }

            cachedProps = JsonUtility.FromJson<PropTypes>(file.text);
        }

        if (cachedProps == null || cachedProps.propTypesData == null)
        {
            return null;
        }

        foreach (PropType def in cachedProps.propTypesData)
        {
            if (def == null)
                continue;

            if (string.Equals(def.type, prefabName, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(def.prefab, prefabName, System.StringComparison.OrdinalIgnoreCase))
            {
                return def;
            }
        }

        return null;
    }
}
