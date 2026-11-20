using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.InputSystem;

// Galaxy Mode atmospheric entry (Fase 1: Tatooine test).
// X near a supported planet: fade, hide the distant sphere, spawn a
// TerrainStreamer patch (seeded from StarSystems.json) and drop the ship
// to low altitude. Climb above the ceiling and press X to leave.
// Ground collision is a V1 hard clamp via downward raycast onto the tiles.
public class GalaxyPlanetEntry : MonoBehaviour
{
    public static GalaxyPlanetEntry Instance;
    public static bool IsOnPlanet = false;
    public static bool IsTransitioning = false;

    // Only these planets support entry for now (Fase 2: full biome table).
    private static readonly string[] SupportedPlanets = new string[] { "Tatooine" };

    private enum EntryState { InSpace, Entering, OnPlanet, Exiting }

    private EntryState state = EntryState.InSpace;
    private GameObject terrainGO;
    private TerrainStreamer streamer;
    // Volumetric-look cloud band (hybrid Elite + Starfield): stacked
    // translucent shells for the deck seen from above, instanced billboard
    // puffs for parallax while crossing, and a fullscreen whiteout haze
    // while inside (Starfield-style cut hiding the shell pop). Profile
    // (coverage/density/tint/altitude) comes from PlanetTypes.json per
    // planetType so each climate gets its own roof.
    private GameObject[] cloudDeckGOs;
    private Material[] cloudMats;
    private GameObject[] cloudPuffGOs;
    private Material cloudPuffMat;
    private Vector3[] cloudPuffOffsets;
    private float[] cloudPuffScales;
    private float cloudDrift;
    private float cloudUvTimer;
    private static readonly Dictionary<string, Texture2D> cloudTextureCache = new Dictionary<string, Texture2D>();
    private GameObject fadeCanvasGO;
    private Image fadeImage;
    private GameObject cloudWhiteoutGO;
    private Image cloudWhiteoutImage;
    private CloudProfile activeCloud;
    private GameObject hiddenPlanet;
    private Vector3 planetCenter;
    private float planetRadius;
    private string cachedLocation;
    private Vector3 returnPosition;
    private bool entryPromptShown;
    private bool exitPromptShown;

    private const float PromptRange = 120f;
    private const float EntryAltitudeMargin = 300f;
    private const float MaxTerrainHeightTatooine = 250f;
    private const float ExitCeilingAGL = 1500f;
    private const float ExitArmAGL = 800f;
    private const float MinClearance = 25f;
    // Fallback deck when PlanetTypes.json has no cloud block for the planet.
    // Real values come from CloudProfile (JSON per planetType).
    private const float DefaultCloudAltitude = 1400f;
    private const float DefaultCloudThickness = 400f;
    private const float EntryAltitudeAboveClouds = 800f;
    // Cloud volume shape (runtime layer count derives from coverage: 3-6).
    private const int CloudLayerMax = 6;
    private const float CloudLayerFadeDistance = 150f;
    private static readonly Color DefaultCloudTint = new Color(1f, 0.98f, 0.95f);
    private const float EntryFadeColorR = 0.96f;
    private const float EntryFadeColorG = 0.85f;
    private const float EntryFadeColorB = 0.66f;

    // Per-planet roof resolved from PlanetTypes.json.
    private struct CloudProfile
    {
        public float coverage;
        public float opacity;
        public Color tint;
        public float altitude;
        public float thickness;
        public int puffs;
        public float whiteout;
    }

    // Floating-origin freeze for planet flight (plan B): the scene anchor is
    // reset to zero on entry and restored on exit, so RecenterScene never
    // fires mid-flight. Suspended below 50 km in SceneFunctions instead.
    // Wide turn-around radius while roaming: terrain streams endlessly, so
    // the 15 km limit must not bite. Finite (not infinite) to avoid
    // float-precision/physics issues far from the origin.
    private const float PlanetSceneRadius = 60000f;
    private float savedSceneRadius = 15000f;
    private Vector3 savedAnchorPosition = Vector3.zero;
    private bool savedAnchorValid = false;
    private float planetLogTimer = 0f;
    private const float PlanetLogInterval = 1f;

    void Awake()
    {
        Instance = this;
        IsOnPlanet = false;
        IsTransitioning = false;
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        IsOnPlanet = false;
        IsTransitioning = false;
        DestroyTerrain();
        DestroyFadeCanvas();
    }

    void Update()
    {
        MissionManager missionManager = GameObject.FindFirstObjectByType<MissionManager>();

        if (missionManager == null || missionManager.running == false)
        {
            return;
        }

        if (missionManager.missionName == null || !missionManager.missionName.StartsWith("Galaxy Mode"))
        {
            return;
        }

        if (GalaxyMapController.IsMapOpen || GalaxyMapController.IsTraveling || IsTransitioning)
        {
            return;
        }

        if (WasEntryKeyPressed() == false)
        {
            if (state == EntryState.InSpace)
            {
                UpdateEntryPrompt();
            }
            else if (state == EntryState.OnPlanet)
            {
                UpdateOnPlanet();
            }

            return;
        }

        if (state == EntryState.InSpace)
        {
            TryBeginEntry();
        }
        else if (state == EntryState.OnPlanet)
        {
            TryBeginExit();
        }
    }

    private bool WasEntryKeyPressed()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard != null && keyboard.xKey.wasPressedThisFrame == true)
        {
            return true;
        }

        if (Input.GetKeyDown(KeyCode.X) == true)
        {
            return true;
        }

        Gamepad gamepad = Gamepad.current;

        if (gamepad != null && gamepad.buttonNorth.wasPressedThisFrame == true)
        {
            return true;
        }

        return false;
    }

    private static bool IsSupported(string planetName)
    {
        foreach (string supported in SupportedPlanets)
        {
            if (supported == planetName)
            {
                return true;
            }
        }

        return false;
    }

    private void UpdateEntryPrompt()
    {
        Scene scene = SceneFunctions.GetScene();

        if (scene == null || scene.mainShip == null || string.IsNullOrEmpty(scene.currentLocation))
        {
            return;
        }

        if (!IsSupported(scene.currentLocation))
        {
            entryPromptShown = false;
            return;
        }

        if (GetActivePlanet(scene, out GameObject planet, out Vector3 center, out float radius) == false)
        {
            return;
        }

        float surfaceDistance = Vector3.Distance(scene.mainShip.transform.position, center) - radius;

        if (surfaceDistance <= PromptRange)
        {
            if (entryPromptShown == false)
            {
                entryPromptShown = true;
                HudFunctions.AddToMissionLog("Press X to enter " + scene.currentLocation + " atmosphere.");
            }
        }
        else
        {
            entryPromptShown = false;
        }
    }

    private void TryBeginEntry()
    {
        Scene scene = SceneFunctions.GetScene();

        if (scene == null || scene.mainShip == null || string.IsNullOrEmpty(scene.currentLocation))
        {
            return;
        }

        if (!IsSupported(scene.currentLocation))
        {
            HudFunctions.AddToMissionLog("Atmospheric entry not yet supported at " + scene.currentLocation + ".");
            return;
        }

        if (GetActivePlanet(scene, out GameObject planet, out Vector3 center, out float radius) == false)
        {
            return;
        }

        float surfaceDistance = Vector3.Distance(scene.mainShip.transform.position, center) - radius;

        if (surfaceDistance > PromptRange)
        {
            HudFunctions.AddToMissionLog("Too far from " + scene.currentLocation + " - fly closer to enter.");
            return;
        }

        hiddenPlanet = planet;
        planetCenter = center;
        planetRadius = radius;
        cachedLocation = scene.currentLocation;
        returnPosition = center + (scene.mainShip.transform.position - center).normalized * (radius + PromptRange);
        StartCoroutine(EnterAtmosphereRoutine(scene));
    }

    // Test hook: entra directo sin comprobar distancia (para la herramienta de prueba).
    public void ForceEnterForTest()
    {
        if (state != EntryState.InSpace || IsTransitioning)
        {
            return;
        }

        Scene scene = SceneFunctions.GetScene();

        if (scene == null || scene.mainShip == null || string.IsNullOrEmpty(scene.currentLocation))
        {
            return;
        }

        if (!IsSupported(scene.currentLocation))
        {
            return;
        }

        if (GetActivePlanet(scene, out GameObject planet, out Vector3 center, out float radius) == false)
        {
            return;
        }

        hiddenPlanet = planet;
        planetCenter = center;
        planetRadius = radius;
        cachedLocation = scene.currentLocation;
        returnPosition = scene.mainShip.transform.position;
        StartCoroutine(EnterAtmosphereRoutine(scene));
    }

    private IEnumerator EnterAtmosphereRoutine(Scene scene)
    {
        state = EntryState.Entering;
        IsTransitioning = true;
        entryPromptShown = false;
        exitPromptShown = false;

        GameObject mainShip = scene.mainShip;
        Ship ship = mainShip.GetComponent<Ship>();
        FlightControlSystem_Small flight = mainShip.GetComponent<FlightControlSystem_Small>();

        bool previousControlLock = flight != null && flight.controlLock;
        bool previousInvincible = ship != null && ship.damageSystem != null && ship.damageSystem.invincible;

        if (flight != null)
        {
            flight.controlLock = true;
        }

        if (ship != null && ship.damageSystem != null)
        {
            ship.damageSystem.invincible = true;
        }

        yield return FadeTo(new Color(EntryFadeColorR, EntryFadeColorG, EntryFadeColorB, 1f), 0.8f);

        if (hiddenPlanet != null)
        {
            hiddenPlanet.SetActive(false);
        }

        // Freeze the floating origin for planet flight: reset the scene anchor
        // once (ship/terrain/clouds are rebuilt around the origin below), so
        // RecenterScene can't snap the ship mid-flight. Restored on exit.
        savedAnchorPosition = scene.transform.position;
        savedAnchorValid = true;
        scene.transform.position = Vector3.zero;
        Debug.Log("[PlanetDiag] entry anchor was " + savedAnchorPosition + ", reset to zero.");

        // Wide turn-around limit for planet roaming (restored on exit).
        savedSceneRadius = scene.sceneRadius;
        SceneFunctions.SetSceneRadius(PlanetSceneRadius);

        // Desert day sky + sandy haze. The binary suns stay where they are
        // (their flares still glow through the haze from the ground).
        TrySetSkybox("loworbit_desert_png01", "loworbit_desert01", "sky_clouds01");
        activeCloud = GetCloudProfile(cachedLocation);
        SceneFunctions.SetFogDistanceAndColor(800f, 7000f, ColorToHex(activeCloud.tint));

        SpawnTerrain(scene);
        SpawnCloudDeck(mainShip.transform.position, activeCloud);

        // Let TerrainStreamer build tiles and physics cook their colliders
        // before sampling the ground below the spawn point.
        yield return null;
        yield return new WaitForFixedUpdate();

        float entryAltitude = scene.transform.position.y + activeCloud.altitude + EntryAltitudeAboveClouds;
        Vector3 shipPos = mainShip.transform.position;
        float groundAtSpawn = SampleGroundHeight(new Vector3(shipPos.x, entryAltitude + 1000f, shipPos.z));
        float safeAltitude = float.IsNaN(groundAtSpawn)
            ? entryAltitude
            : Mathf.Max(entryAltitude, groundAtSpawn + EntryAltitudeMargin);
        mainShip.transform.position = new Vector3(shipPos.x, safeAltitude, shipPos.z);
        Debug.Log("[PlanetDiag] spawned at " + mainShip.transform.position + " (ground " + groundAtSpawn + ").");

        yield return FadeTo(new Color(EntryFadeColorR, EntryFadeColorG, EntryFadeColorB, 0f), 0.8f);

        if (flight != null)
        {
            flight.controlLock = previousControlLock;
        }

        if (ship != null && ship.damageSystem != null)
        {
            ship.damageSystem.invincible = previousInvincible;
        }

        state = EntryState.OnPlanet;
        IsOnPlanet = true;
        IsTransitioning = false;
        HudFunctions.AddToMissionLog("Entered " + cachedLocation + " atmosphere - climb above " + ExitCeilingAGL + " and press X to leave.");
    }

    private void SpawnTerrain(Scene scene)
    {
        DestroyTerrain();

        int seed = GetSeedForSystem(scene.currentLocation);
        terrainGO = new GameObject("PlanetTerrain");
        // Child of the scene anchor: recenters move terrain rigidly with the
        // ship instead of leaving it behind.
        terrainGO.transform.SetParent(scene.transform, false);
        terrainGO.transform.localPosition = Vector3.zero;
        terrainGO.transform.rotation = Quaternion.identity;
        streamer = terrainGO.AddComponent<TerrainStreamer>();
        streamer.seed = seed;
        streamer.ship = scene.mainShip.transform;
        streamer.tileParent = terrainGO.transform;
        streamer.terrainTextureType = "sandy";
        streamer.tileRadiusInKilometres = 3;
        // Distant ring to the horizon: fogged out, hides the terrain edge.
        streamer.outerTileRadiusInKilometres = 8;
        streamer.maxTerrainHeight = MaxTerrainHeightTatooine;
        streamer.plainsAmp = 25f;
        streamer.desertAmp = 90f;
        streamer.hillsAmp = 120f;
        streamer.mountAmp = 220f;
    }

    private void UpdateOnPlanet()
    {
        Scene scene = SceneFunctions.GetScene();

        if (scene == null || scene.mainShip == null)
        {
            return;
        }

        UpdateCloudDeck(scene.mainShip.transform.position);

        float groundHeight = SampleGroundHeight(scene.mainShip.transform.position);
        // No tile below (flying high): treat as infinite clearance so the
        // MinClearance clamp below can't push the ship upward.
        float agl = float.IsNaN(groundHeight) ? float.PositiveInfinity : scene.mainShip.transform.position.y - groundHeight;

        planetLogTimer += Time.deltaTime;

        if (planetLogTimer >= PlanetLogInterval)
        {
            planetLogTimer = 0f;
            Vector3 anchorPos = scene.transform.position;
            float distToCenter = Vector3.Distance(scene.mainShip.transform.position, anchorPos);
            string camY = "n/a";

            if (scene.ogCamera != null && scene.ogCamera.mainCamera != null)
            {
                camY = scene.ogCamera.mainCamera.transform.position.y.ToString("F1");
            }

            Debug.Log("[PlanetDiag] shipY=" + scene.mainShip.transform.position.y.ToString("F1")
                + " groundY=" + groundHeight.ToString("F1")
                + " agl=" + agl.ToString("F1")
                + " anchorY=" + anchorPos.y.ToString("F1")
                + " camY=" + camY
                + " distToCenter=" + distToCenter.ToString("F1"));
        }

        if (float.IsNaN(groundHeight) == false && agl < MinClearance)
        {
            Vector3 shipPos = scene.mainShip.transform.position;
            shipPos.y = groundHeight + MinClearance;
            scene.mainShip.transform.position = shipPos;
            agl = MinClearance;
        }

        if (agl >= ExitCeilingAGL && exitPromptShown == false)
        {
            exitPromptShown = true;
            HudFunctions.AddToMissionLog("Press X to leave " + cachedLocation + " atmosphere.");
        }
        else if (agl < ExitArmAGL)
        {
            exitPromptShown = false;
        }
    }

    private void TryBeginExit()
    {
        Scene scene = SceneFunctions.GetScene();

        if (scene == null || scene.mainShip == null)
        {
            return;
        }

        float groundHeight = SampleGroundHeight(scene.mainShip.transform.position);
        float agl = float.IsNaN(groundHeight) ? float.PositiveInfinity : scene.mainShip.transform.position.y - groundHeight;

        if (agl < ExitCeilingAGL)
        {
            HudFunctions.AddToMissionLog("Too low - climb above " + ExitCeilingAGL + " to leave.");
            return;
        }

        StartCoroutine(ExitAtmosphereRoutine(scene, false));
    }

    // Called by the hyperspace jump so no terrain tiles are left behind.
    // The ship is NOT repositioned: the jump routine places it afterwards.
    public IEnumerator ExitForHyperspace()
    {
        if (state != EntryState.OnPlanet)
        {
            yield break;
        }

        Scene scene = SceneFunctions.GetScene();

        if (scene == null)
        {
            ForceCleanupToSpace();
            yield break;
        }

        yield return ExitAtmosphereRoutine(scene, true);
    }

    private IEnumerator ExitAtmosphereRoutine(Scene scene, bool keepPosition)
    {
        state = EntryState.Exiting;
        IsTransitioning = true;

        GameObject mainShip = scene.mainShip;
        Ship ship = mainShip != null ? mainShip.GetComponent<Ship>() : null;
        FlightControlSystem_Small flight = mainShip != null ? mainShip.GetComponent<FlightControlSystem_Small>() : null;

        bool previousControlLock = flight != null && flight.controlLock;
        bool previousInvincible = ship != null && ship.damageSystem != null && ship.damageSystem.invincible;

        if (flight != null)
        {
            flight.controlLock = true;
        }

        if (ship != null && ship.damageSystem != null)
        {
            ship.damageSystem.invincible = true;
        }

        yield return FadeTo(new Color(0f, 0f, 0f, 1f), 0.8f);

        DestroyTerrain();

        if (savedAnchorValid)
        {
            scene.transform.position = savedAnchorPosition;
            savedAnchorValid = false;
            Debug.Log("[PlanetDiag] exit anchor restored to " + savedAnchorPosition + ".");
        }

        SceneFunctions.SetSceneRadius(savedSceneRadius);

        if (hiddenPlanet != null)
        {
            hiddenPlanet.SetActive(true);
            hiddenPlanet = null;
        }

        // Back to space visuals (also on hyperspace exits: the jump expects
        // the starfield skybox and deep fog).
        SceneFunctions.SetSkybox("space", true);
        SceneFunctions.SetFogDistanceAndColor(30000f, 40000f, "#000000");

        if (keepPosition == false && mainShip != null)
        {
            mainShip.transform.position = returnPosition;
        }

        yield return FadeTo(new Color(0f, 0f, 0f, 0f), 0.8f);

        if (flight != null)
        {
            flight.controlLock = previousControlLock;
        }

        if (ship != null && ship.damageSystem != null)
        {
            ship.damageSystem.invincible = previousInvincible;
        }

        state = EntryState.InSpace;
        IsOnPlanet = false;
        IsTransitioning = false;
        exitPromptShown = false;

        if (keepPosition == false)
        {
            HudFunctions.AddToMissionLog("Left " + cachedLocation + " atmosphere.");
        }
    }

    private void ForceCleanupToSpace()
    {
        DestroyTerrain();

        Scene scene = SceneFunctions.GetScene();

        if (scene != null && savedAnchorValid)
        {
            scene.transform.position = savedAnchorPosition;
            savedAnchorValid = false;
            Debug.Log("[PlanetDiag] cleanup anchor restored to " + savedAnchorPosition + ".");
        }

        if (scene != null)
        {
            SceneFunctions.SetSceneRadius(savedSceneRadius);
        }

        if (hiddenPlanet != null)
        {
            hiddenPlanet.SetActive(true);
            hiddenPlanet = null;
        }

        // Back to space visuals (also on hyperspace exits: the jump expects
        // the starfield skybox and deep fog).
        SceneFunctions.SetSkybox("space", true);
        SceneFunctions.SetFogDistanceAndColor(30000f, 40000f, "#000000");

        state = EntryState.InSpace;
        IsOnPlanet = false;
        IsTransitioning = false;
    }

    private void DestroyTerrain()
    {
        if (streamer != null)
        {
            streamer.enabled = false;
            streamer = null;
        }

        if (terrainGO != null)
        {
            GameObject.Destroy(terrainGO);
            terrainGO = null;
        }

        DestroyCloudDeck();
    }

    // Hybrid Elite + Starfield deck. Deck shells (seen from above) + billboard
    // puffs (parallax while crossing) + whiteout haze (hides shell pop).
    // Layer count and alpha derive from the per-planet coverage so a desert
    // roof reads broken and a tropical roof reads overcast.
    private void SpawnCloudDeck(Vector3 shipPosition, CloudProfile profile)
    {
        DestroyCloudDeck();

        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");

        if (unlit == null)
        {
            unlit = Shader.Find("Unlit/Color");
        }

        int layerCount = Mathf.Clamp(3 + Mathf.RoundToInt(profile.coverage * 3f), 3, CloudLayerMax);
        float halfThickness = profile.thickness * 0.5f;
        Texture2D tex = GetCloudTexture(profile.coverage);

        cloudDeckGOs = new GameObject[layerCount];
        cloudMats = new Material[layerCount];

        for (int i = 0; i < layerCount; i++)
        {
            float layerT = layerCount == 1 ? 0.5f : (float)i / (layerCount - 1);
            float layerY = profile.altitude - halfThickness + layerT * halfThickness * 2f;
            float tiling = 10f - layerT * 5f;

            Material mat = new Material(unlit);
            mat.mainTexture = tex;
            mat.SetTexture("_BaseMap", tex);
            mat.SetTexture("_MainTex", tex);
            mat.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
            mat.SetTextureScale("_MainTex", new Vector2(tiling, tiling));
            // Decorrelate the noise pattern between shells.
            Vector2 seedOffset = new Vector2(i * 0.37f, i * 0.61f);
            mat.SetTextureOffset("_BaseMap", seedOffset);
            mat.SetTextureOffset("_MainTex", seedOffset);
            Color tint = profile.tint;
            tint.a = profile.opacity;
            mat.color = tint;
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.SetFloat("_Cull", 0f);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            cloudMats[i] = mat;

            GameObject deck = GameObject.CreatePrimitive(PrimitiveType.Plane);
            deck.name = "PlanetCloudShell_L" + i;
            Scene cloudScene = SceneFunctions.GetScene();
            if (cloudScene != null)
            {
                deck.transform.SetParent(cloudScene.transform, true);
            }
            float shellAnchorY = cloudScene != null ? cloudScene.transform.position.y : 0f;
            deck.transform.position = new Vector3(shipPosition.x, shellAnchorY + layerY, shipPosition.z);
            deck.transform.localScale = Vector3.one * 1600f;
            Collider deckCollider = deck.GetComponent<Collider>();

            if (deckCollider != null)
            {
                GameObject.Destroy(deckCollider);
            }

            Renderer deckRenderer = deck.GetComponent<Renderer>();
            deckRenderer.sharedMaterial = mat;
            cloudDeckGOs[i] = deck;
        }

        // Puffs: camera-facing quads scattered through the band for parallax.
        // One shared material = N cheap draw calls, no per-puff allocation.
        cloudPuffMat = new Material(unlit);
        cloudPuffMat.mainTexture = tex;
        cloudPuffMat.SetTexture("_BaseMap", tex);
        cloudPuffMat.SetTexture("_MainTex", tex);
        Color puffTint = profile.tint;
        puffTint.a = Mathf.Clamp01(profile.opacity * 0.85f);
        cloudPuffMat.color = puffTint;
        cloudPuffMat.SetFloat("_Surface", 1f);
        cloudPuffMat.SetFloat("_Blend", 0f);
        cloudPuffMat.SetOverrideTag("RenderType", "Transparent");
        cloudPuffMat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        cloudPuffMat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        cloudPuffMat.SetInt("_ZWrite", 0);
        cloudPuffMat.SetFloat("_Cull", 0f);
        cloudPuffMat.DisableKeyword("_ALPHATEST_ON");
        cloudPuffMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        cloudPuffMat.renderQueue = (int)RenderQueue.Transparent;

        int puffCount = Mathf.Clamp(profile.puffs, 0, 80);
        cloudPuffGOs = new GameObject[puffCount];
        cloudPuffOffsets = new Vector3[puffCount];
        cloudPuffScales = new float[puffCount];
        System.Random rng = new System.Random(GetSeedForSystem(cachedLocation) ^ 0x9E3779B9);
        for (int i = 0; i < puffCount; i++)
        {
            float ang = (float)(rng.NextDouble() * Mathf.PI * 2f);
            float rad = 150f + (float)rng.NextDouble() * 1200f;
            float yOff = ((float)rng.NextDouble() * 2f - 1f) * halfThickness;
            cloudPuffOffsets[i] = new Vector3(Mathf.Cos(ang) * rad, yOff, Mathf.Sin(ang) * rad);
            cloudPuffScales[i] = (150f + (float)rng.NextDouble() * 200f) * (0.7f + profile.coverage * 0.6f);

            GameObject puff = GameObject.CreatePrimitive(PrimitiveType.Quad);
            puff.name = "PlanetCloudPuff_" + i;
            Scene puffScene = SceneFunctions.GetScene();
            if (puffScene != null)
            {
                puff.transform.SetParent(puffScene.transform, true);
            }
            Collider puffCollider = puff.GetComponent<Collider>();
            if (puffCollider != null)
            {
                GameObject.Destroy(puffCollider);
            }
            Renderer puffRenderer = puff.GetComponent<Renderer>();
            puffRenderer.sharedMaterial = cloudPuffMat;
            // Per-puff UV variation without extra materials: offset via texture
            // is shared, so vary rotation/scale instead to break repetition.
            puff.transform.localRotation = Quaternion.Euler(0f, 0f, (float)rng.NextDouble() * 360f);
            cloudPuffGOs[i] = puff;
        }

        EnsureCloudWhiteout(profile.tint);
        cloudDrift = 0f;
        cloudUvTimer = 0f;
    }

    private void UpdateCloudDeck(Vector3 shipPosition)
    {
        if (cloudDeckGOs == null || cloudMats == null)
        {
            return;
        }

        CloudProfile profile = activeCloud;
        float halfThickness = profile.thickness * 0.5f;

        cloudDrift += Time.deltaTime * 2.5f;
        // Scroll UVs at ~15 Hz: same look, fewer material updates.
        cloudUvTimer += Time.deltaTime;
        bool pushUv = cloudUvTimer >= 0.066f;
        if (pushUv)
        {
            cloudUvTimer = 0f;
        }

        float deckAnchorY = 0f;
        float camY = shipPosition.y;
        Vector3 camPos = shipPosition;
        Scene deckSceneInit = SceneFunctions.GetScene();
        if (deckSceneInit != null)
        {
            deckAnchorY = deckSceneInit.transform.position.y;

            if (deckSceneInit.ogCamera != null && deckSceneInit.ogCamera.mainCamera != null)
            {
                camY = deckSceneInit.ogCamera.mainCamera.transform.position.y;
                camPos = deckSceneInit.ogCamera.mainCamera.transform.position;
            }
        }

        // LOD: far above/below the band the deck is a distant lid; hide puffs
        // entirely and (below) let fog carry the look to save overdraw.
        float distToBand = Mathf.Abs(camY - (deckAnchorY + profile.altitude));
        float bandRange = halfThickness + 1200f;
        float deckGlobalFade = 1f - Mathf.Clamp01((distToBand - bandRange) / 800f);
        bool puffsVisible = distToBand < halfThickness + 900f;

        for (int i = 0; i < cloudDeckGOs.Length; i++)
        {
            if (cloudDeckGOs[i] == null || cloudMats[i] == null)
            {
                continue;
            }

            int layerCount = cloudDeckGOs.Length;
            float layerT = layerCount == 1 ? 0.5f : (float)i / (layerCount - 1);
            float layerY = profile.altitude - halfThickness + layerT * halfThickness * 2f;
            float layerDrift = cloudDrift * (1f + layerT * 0.6f);
            Scene deckScene = SceneFunctions.GetScene();
            if (deckScene != null)
            {
                deckAnchorY = deckScene.transform.position.y;
            }
            cloudDeckGOs[i].transform.position = new Vector3(
                shipPosition.x + layerDrift,
                deckAnchorY + layerY,
                shipPosition.z + layerDrift * 0.6f);
            if (pushUv)
            {
                Vector2 uvOffset = new Vector2(
                    i * 0.37f + layerDrift * 0.0004f,
                    i * 0.61f + layerDrift * 0.00025f);
                cloudMats[i].SetTextureOffset("_BaseMap", uvOffset);
                cloudMats[i].SetTextureOffset("_MainTex", uvOffset);
            }

            // Fade shells out as the camera approaches them: the pop happens
            // at the camera plane, not at the ship, so fade by camera height.
            float distToCam = Mathf.Abs(camY - (deckAnchorY + layerY));
            float fade = Mathf.Clamp01(distToCam / CloudLayerFadeDistance);
            Color tint = cloudMats[i].color;
            tint.a = profile.opacity * fade * deckGlobalFade;
            cloudMats[i].color = tint;
        }

        if (cloudPuffGOs != null)
        {
            for (int i = 0; i < cloudPuffGOs.Length; i++)
            {
                GameObject puff = cloudPuffGOs[i];
                if (puff == null)
                {
                    continue;
                }
                if (puffsVisible == false)
                {
                    puff.SetActive(false);
                    continue;
                }
                puff.SetActive(true);
                Vector3 basePos = new Vector3(
                    shipPosition.x + cloudPuffOffsets[i].x + cloudDrift * 0.8f,
                    deckAnchorY + profile.altitude + cloudPuffOffsets[i].y,
                    shipPosition.z + cloudPuffOffsets[i].z + cloudDrift * 0.5f);
                puff.transform.position = basePos;
                puff.transform.localScale = Vector3.one * cloudPuffScales[i];
                // Billboard: face the camera, keep the baked roll variation.
                Vector3 toCam = camPos - basePos;
                if (toCam.sqrMagnitude > 0.01f)
                {
                    puff.transform.rotation = Quaternion.LookRotation(toCam.normalized);
                }
            }
        }

        // Dense haze while the camera is inside the volume: sells being
        // wrapped in cloud. Driven by camera height, not ship height.
        // Starfield-style whiteout overlay hides the shell pop at crossing.
        float inside = Mathf.Clamp01(1f - distToBand / (halfThickness + 200f));
        float fogNear = Mathf.Lerp(800f, 60f, inside);
        float fogFar = Mathf.Lerp(7000f, 1800f, inside);
        SceneFunctions.SetFogDistanceAndColor(fogNear, fogFar, ColorToHex(profile.tint));
        if (cloudWhiteoutImage != null)
        {
            Color wc = cloudWhiteoutImage.color;
            wc.a = inside * profile.whiteout * 0.85f;
            cloudWhiteoutImage.color = wc;
            if (cloudWhiteoutGO != null && cloudWhiteoutGO.activeSelf == false && wc.a > 0.001f)
            {
                cloudWhiteoutGO.SetActive(true);
            }
        }
    }

    private void DestroyCloudDeck()
    {
        if (cloudDeckGOs != null)
        {
            foreach (GameObject deck in cloudDeckGOs)
            {
                if (deck != null)
                {
                    GameObject.Destroy(deck);
                }
            }

            cloudDeckGOs = null;
        }

        if (cloudMats != null)
        {
            foreach (Material mat in cloudMats)
            {
                if (mat != null)
                {
                    GameObject.Destroy(mat);
                }
            }

            cloudMats = null;
        }

        if (cloudPuffGOs != null)
        {
            foreach (GameObject puff in cloudPuffGOs)
            {
                if (puff != null)
                {
                    GameObject.Destroy(puff);
                }
            }

            cloudPuffGOs = null;
        }

        cloudPuffOffsets = null;
        cloudPuffScales = null;

        if (cloudPuffMat != null)
        {
            GameObject.Destroy(cloudPuffMat);
            cloudPuffMat = null;
        }

        if (cloudWhiteoutGO != null)
        {
            cloudWhiteoutGO.SetActive(false);
        }
    }

    private void EnsureCloudWhiteout(Color tint)
    {
        if (cloudWhiteoutGO != null && cloudWhiteoutImage != null)
        {
            cloudWhiteoutImage.color = new Color(tint.r, tint.g, tint.b, 0f);
            cloudWhiteoutGO.SetActive(false);
            return;
        }

        cloudWhiteoutGO = new GameObject("PlanetCloudWhiteout");
        Canvas canvas = cloudWhiteoutGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 110;
        cloudWhiteoutGO.AddComponent<CanvasScaler>();
        cloudWhiteoutGO.AddComponent<GraphicRaycaster>();

        GameObject imageGO = new GameObject("WhiteoutImage");
        imageGO.transform.SetParent(cloudWhiteoutGO.transform, false);
        RectTransform imageRT = imageGO.AddComponent<RectTransform>();
        imageRT.anchorMin = Vector2.zero;
        imageRT.anchorMax = Vector2.one;
        imageRT.offsetMin = Vector2.zero;
        imageRT.offsetMax = Vector2.zero;
        cloudWhiteoutImage = imageGO.AddComponent<Image>();
        cloudWhiteoutImage.color = new Color(tint.r, tint.g, tint.b, 0f);
        cloudWhiteoutImage.raycastTarget = false;
        cloudWhiteoutGO.SetActive(false);
    }

    // Roof per planetType from PlanetTypes.json. Unknown/missing fields fall
    // back to the old Tatooine look so existing saves keep working.
    private static CloudProfile GetCloudProfile(string planetName)
    {
        CloudProfile fallback = new CloudProfile
        {
            coverage = 0.32f,
            opacity = 0.34f,
            tint = DefaultCloudTint,
            altitude = DefaultCloudAltitude,
            thickness = DefaultCloudThickness,
            puffs = 24,
            whiteout = 0.5f
        };

        string planetType = GetPlanetTypeName(planetName);
        PlanetType def = GetPlanetTypeDef(planetType);

        if (def == null)
        {
            return fallback;
        }

        CloudProfile profile = fallback;
        if (def.cloudCoverage > 0f)
        {
            profile.coverage = Mathf.Clamp01(def.cloudCoverage);
        }
        if (def.cloudOpacity > 0f)
        {
            profile.opacity = Mathf.Clamp01(def.cloudOpacity);
        }
        if (string.IsNullOrEmpty(def.cloudTint) == false)
        {
            Color tint;
            if (ColorUtility.TryParseHtmlString(def.cloudTint, out tint))
            {
                profile.tint = tint;
            }
        }
        if (def.cloudAltitude > 0f)
        {
            profile.altitude = def.cloudAltitude;
        }
        if (def.cloudThickness > 0f)
        {
            profile.thickness = def.cloudThickness;
        }
        if (def.cloudPuffs > 0)
        {
            profile.puffs = Mathf.Clamp(def.cloudPuffs, 0, 80);
        }
        if (def.cloudWhiteout > 0f)
        {
            profile.whiteout = Mathf.Clamp01(def.cloudWhiteout);
        }
        return profile;
    }

    private static string GetPlanetTypeName(string planetName)
    {
        TextAsset starSystemFile = Resources.Load(OGGetAddress.files + "StarSystems") as TextAsset;

        if (starSystemFile != null)
        {
            StarSystems starSystems = JsonUtility.FromJson<StarSystems>(starSystemFile.text);

            if (starSystems != null && starSystems.starSystemsData != null)
            {
                foreach (StarSystem system in starSystems.starSystemsData)
                {
                    if (system != null && system.Planet == planetName)
                    {
                        return system.planetType;
                    }
                }
            }
        }

        return null;
    }

    private static PlanetType GetPlanetTypeDef(string planetType)
    {
        if (string.IsNullOrEmpty(planetType))
        {
            return null;
        }

        TextAsset planetTypesFile = Resources.Load(OGGetAddress.files + "PlanetTypes") as TextAsset;

        if (planetTypesFile == null)
        {
            return null;
        }

        PlanetTypes planetTypes = JsonUtility.FromJson<PlanetTypes>(planetTypesFile.text);

        if (planetTypes == null || planetTypes.planetTypesData == null)
        {
            return null;
        }

        foreach (PlanetType def in planetTypes.planetTypesData)
        {
            if (def != null && string.Equals(def.planetType, planetType, System.StringComparison.OrdinalIgnoreCase))
            {
                return def;
            }
        }

        return null;
    }

    private static string ColorToHex(Color color)
    {
        Color32 c32 = color;
        return string.Format("#{0:X2}{1:X2}{2:X2}", c32.r, c32.g, c32.b);
    }

    private static Texture2D GetCloudTexture(float coverage)
    {
        // Bucket the cache so desert/tropical/etc share a handful of textures
        // instead of one per exact slider value.
        float bucket = Mathf.Round(coverage * 20f) / 20f;
        string key = "cloud_" + bucket.ToString("F2");
        Texture2D cached;
        if (cloudTextureCache.TryGetValue(key, out cached) && cached != null)
        {
            return cached;
        }

        // Higher coverage -> lower threshold -> more texels survive as cloud.
        float threshold = 0.68f - Mathf.Clamp01(bucket) * 0.35f;
        const int size = 256;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Mirror;
        tex.filterMode = FilterMode.Bilinear;
        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size * 4f;
                float v = (float)y / size * 4f;
                float fbm = CloudNoise(u, v) * 0.65f + CloudNoise(u * 2.7f + 13f, v * 2.7f + 7f) * 0.35f;
                float alpha = Mathf.Clamp01((fbm - threshold) / 0.25f);
                float shade = 0.9f + 0.1f * CloudNoise(u * 5f, v * 5f);
                pixels[y * size + x] = new Color(shade, shade * 0.99f, shade * 0.97f, alpha * 0.88f);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false, true);
        cloudTextureCache[key] = tex;
        return tex;
    }

    private static float CloudNoise(float x, float y)
    {
        int xi = Mathf.FloorToInt(x);
        int yi = Mathf.FloorToInt(y);
        float xf = x - xi;
        float yf = y - yi;
        float a = CloudHash(xi, yi);
        float b = CloudHash(xi + 1, yi);
        float c = CloudHash(xi, yi + 1);
        float d = CloudHash(xi + 1, yi + 1);
        float u = xf * xf * (3f - 2f * xf);
        float v = yf * yf * (3f - 2f * yf);
        return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
    }

    private static float CloudHash(int x, int y)
    {
        int h = x * 374761393 + y * 668265263;
        h = (h ^ (h >> 13)) * 1274126177;
        return ((h ^ (h >> 16)) & 0x7fffffff) / (float)0x7fffffff;
    }

    // Only terrain tiles count ("Tile_x_z"): the ray starts above the ship
    // and could otherwise hit the ship itself or nearby traffic.
    // Returns NaN when no tile is below (ship too high or tiles not ready):
    // callers must NOT treat that as ground level, or the MinClearance
    // clamp feeds back into a runaway climb.
    private float SampleGroundHeight(Vector3 shipPosition)
    {
        RaycastHit[] hits;
        Vector3 origin = new Vector3(shipPosition.x, shipPosition.y + 400f, shipPosition.z);
        // Long enough to reach the terrain (~0-250) from entry altitude.
        float rayLength = origin.y + 1000f;
        hits = Physics.RaycastAll(origin, Vector3.down, rayLength, 1 << 0);

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

    private static bool GetActivePlanet(Scene scene, out GameObject planet, out Vector3 center, out float radius)
    {
        planet = null;
        center = Vector3.zero;
        radius = 0f;

        if (scene.planetsPool == null)
        {
            return false;
        }

        float bestDistance = float.MaxValue;
        GameObject bestPlanet = null;
        Vector3 bestCenter = Vector3.zero;
        float bestRadius = 0f;

        foreach (GameObject candidate in scene.planetsPool)
        {
            if (candidate == null || candidate.activeSelf == false)
            {
                continue;
            }

            if (GetPlanetBounds(candidate, out Vector3 candidateCenter, out float candidateRadius) == false)
            {
                continue;
            }

            float distance = Vector3.Distance(scene.mainShip.transform.position, candidateCenter);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestPlanet = candidate;
                bestCenter = candidateCenter;
                bestRadius = candidateRadius;
            }
        }

        if (bestPlanet == null)
        {
            return false;
        }

        planet = bestPlanet;
        center = bestCenter;
        radius = bestRadius;
        return true;
    }

    // Planet pivot holds the visual sphere; rings/atmosphere shells are
    // excluded so the radius matches the solid surface.
    private static bool GetPlanetBounds(GameObject planet, out Vector3 center, out float radius)
    {
        center = planet.transform.position;
        radius = 0f;

        Renderer[] renderers = planet.GetComponentsInChildren<Renderer>(true);
        bool found = false;
        Bounds bounds = new Bounds();

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            string objectName = renderer.gameObject.name.ToLowerInvariant();

            if (objectName.Contains("ring") || objectName.Contains("atmosphere"))
            {
                continue;
            }

            if (found == false)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (found == false)
        {
            return false;
        }

        center = bounds.center;
        radius = bounds.extents.magnitude;
        return true;
    }

    private static int GetSeedForSystem(string planetName)
    {
        TextAsset starSystemFile = Resources.Load(OGGetAddress.files + "StarSystems") as TextAsset;

        if (starSystemFile != null)
        {
            StarSystems starSystems = JsonUtility.FromJson<StarSystems>(starSystemFile.text);

            if (starSystems != null && starSystems.starSystemsData != null)
            {
                foreach (StarSystem system in starSystems.starSystemsData)
                {
                    if (system != null && system.Planet == planetName)
                    {
                        return system.seed;
                    }
                }
            }
        }

        return 1138;
    }

    private static void TrySetSkybox(params string[] names)
    {
        Scene scene = SceneFunctions.GetScene();

        if (scene == null || scene.skyboxes == null)
        {
            return;
        }

        foreach (string name in names)
        {
            foreach (Material skybox in scene.skyboxes)
            {
                if (skybox != null && skybox.name == name)
                {
                    SceneFunctions.SetSkybox(name, false);
                    return;
                }
            }
        }

        Debug.LogWarning("[PlanetEntry] None of the skyboxes found: " + string.Join(" / ", names));
    }

    private IEnumerator FadeTo(Color target, float duration)
    {
        EnsureFadeCanvas();

        if (fadeImage == null)
        {
            yield break;
        }

        fadeCanvasGO.SetActive(true);
        Color start = fadeImage.color;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            fadeImage.color = Color.Lerp(start, target, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        fadeImage.color = target;

        if (Mathf.Approximately(target.a, 0f))
        {
            fadeCanvasGO.SetActive(false);
        }
    }

    private void EnsureFadeCanvas()
    {
        if (fadeCanvasGO != null)
        {
            return;
        }

        fadeCanvasGO = new GameObject("PlanetEntryFade");
        Canvas canvas = fadeCanvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 120;
        fadeCanvasGO.AddComponent<CanvasScaler>();
        fadeCanvasGO.AddComponent<GraphicRaycaster>();

        GameObject imageGO = new GameObject("FadeImage");
        imageGO.transform.SetParent(fadeCanvasGO.transform, false);
        RectTransform imageRT = imageGO.AddComponent<RectTransform>();
        imageRT.anchorMin = Vector2.zero;
        imageRT.anchorMax = Vector2.one;
        imageRT.offsetMin = Vector2.zero;
        imageRT.offsetMax = Vector2.zero;
        fadeImage = imageGO.AddComponent<Image>();
        fadeImage.color = new Color(EntryFadeColorR, EntryFadeColorG, EntryFadeColorB, 0f);
        fadeImage.raycastTarget = false;
        fadeCanvasGO.SetActive(false);
    }

    private void DestroyFadeCanvas()
    {
        if (fadeCanvasGO != null)
        {
            GameObject.Destroy(fadeCanvasGO);
            fadeCanvasGO = null;
            fadeImage = null;
        }
    }
}
