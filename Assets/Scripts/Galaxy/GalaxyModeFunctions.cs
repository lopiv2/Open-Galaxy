using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Holds the galaxy free-roam (Galaxy Mode) flow:
// 1. Player picks a ship with cockpit under Start Game -> Galaxy Mode.
// 2. Player picks a start planet on the galaxy map.
// 3. A minimal free-roam scene is launched with that ship and planet.
public static class GalaxyModeFunctions
{
    public static string SelectedShipType = "xwing";
    public static string SelectedStartPlanet = "Tatooine";

    // Curated start planets - all exist in StarSystems.json with region/faction data.
    public static readonly string[] StartPlanets = new string[]
    {
        "Tatooine",
        "Coruscant",
        "Corellia",
        "Naboo",
        "Hoth",
        "Endor",
        "Bespin",
        "Yavin",
        "Alderaan",
        "Dantooine",
        "Kashyyyk",
        "Mustafar",
        "Geonosis",
        "Kamino",
        "Jakku",
        "Scarif",
        "Sullust",
        "Dagobah",
        "Mon Calamari"
    };

    // Ships with cockpit that the player can fly.
    public static List<ShipType> GetFlyableShips()
    {
        List<ShipType> result = new List<ShipType>();

        TextAsset shipTypesFile = Resources.Load(OGGetAddress.files + "ShipTypes") as TextAsset;

        if (shipTypesFile == null)
        {
            return result;
        }

        ShipTypes shipTypes = JsonUtility.FromJson<ShipTypes>(shipTypesFile.text);

        if (shipTypes == null || shipTypes.shipTypeData == null)
        {
            return result;
        }

        foreach (ShipType shipType in shipTypes.shipTypeData)
        {
            if (shipType == null)
            {
                continue;
            }

            if (shipType.scriptType != "smallship")
            {
                continue;
            }

            if (string.IsNullOrEmpty(shipType.cockpitPrefab) || shipType.cockpitPrefab == "none")
            {
                continue;
            }

            if (string.IsNullOrEmpty(shipType.type) || string.IsNullOrEmpty(shipType.prefab))
            {
                continue;
            }

            result.Add(shipType);
        }

        result.Sort((a, b) => string.Compare(a.type, b.type));

        return result;
    }

    // Curated start systems with full StarSystem data for the galaxy map.
    public static List<StarSystem> GetStartSystems()
    {
        List<StarSystem> result = new List<StarSystem>();

        TextAsset starSystemFile = Resources.Load(OGGetAddress.files + "StarSystems") as TextAsset;

        if (starSystemFile == null)
        {
            return result;
        }

        StarSystems starSystems = JsonUtility.FromJson<StarSystems>(starSystemFile.text);

        if (starSystems == null || starSystems.starSystemsData == null)
        {
            return result;
        }

        Dictionary<string, StarSystem> byPlanet = new Dictionary<string, StarSystem>();

        foreach (StarSystem system in starSystems.starSystemsData)
        {
            if (system == null || string.IsNullOrEmpty(system.Planet))
            {
                continue;
            }

            if (!byPlanet.ContainsKey(system.Planet))
            {
                byPlanet.Add(system.Planet, system);
            }
        }

        foreach (string planetName in StartPlanets)
        {
            if (byPlanet.ContainsKey(planetName))
            {
                result.Add(byPlanet[planetName]);
            }
        }

        return result;
    }

    // Called from the Galaxy Ships submenu.
    public static void SelectGalaxyShip(string shipType)
    {
        if (!string.IsNullOrEmpty(shipType))
        {
            SelectedShipType = shipType;
        }

        MainMenuFunctions.OutputMenuMessage("Ship selected: " + SelectedShipType);
        MainMenuFunctions.ActivateSubMenu("Galaxy Map");
    }

    // Called from the Galaxy Map submenu. Launches free-roam.
    public static void StartGalaxyMode(string planetName)
    {
        if (!string.IsNullOrEmpty(planetName))
        {
            SelectedStartPlanet = planetName;
        }

        MainMenu mainMenu = GameObject.FindFirstObjectByType<MainMenu>();

        if (mainMenu != null)
        {
            if (mainMenu.missionRunning == true)
            {
                return;
            }

            mainMenu.missionRunning = true;

            Task t = new Task(RunGalaxyMode(SelectedShipType, SelectedStartPlanet));
        }
    }

    // Minimal free-roam loader reusing the normal scene pipeline.
    public static IEnumerator RunGalaxyMode(string shipType, string planetName)
    {
        float startTime = Time.unscaledTime;

        Scene scene = SceneFunctions.CreateScene();
        SceneFunctions.LoadScenePrefabs();

        GameObject missionManagerGO = new GameObject();
        missionManagerGO.name = "MissionManager";
        MissionManager missionManager = missionManagerGO.AddComponent<MissionManager>();
        missionManagerGO.AddComponent<GalaxyMapController>();
        missionManager.missionName = "Galaxy Mode - " + planetName;
        missionManager.missionAddress = OGGetAddress.missions_internal;
        missionManager.missionData = "{\"missionEventData\":[]}";
        missionManager.scene = SceneFunctions.GetScene();
        missionManager.audioLoaded = true;
        missionManager.running = false;

        Time.timeScale = 0;

        MissionFunctions.DisplayLoadingScreen(missionManager.missionName, true);
        MainMenuFunctions.AddLogToLoadingScreen("Start loading Galaxy Mode - " + planetName + ".", startTime);

        // HUD + audio/music (no mission-specific audio folder).
        HudFunctions.CreateHud();
        AudioFunctions.CreateAudioManager();
        OGVideoPlayerFunctions.CreateOGVideoPlayer();
        MusicFunctions.CreateMusicManager();

        SceneFunctions.SetSkybox("space", true);
        SceneFunctions.SetFogDistanceAndColor(30000, 40000, "#000000");
        SceneFunctions.SetLighting("#E2EAF4", false, 1, 1, 0, 0, 0, 60, 0, 0);
        SceneFunctions.SetSceneRadius(15000);

        if (scene != null)
        {
            scene.currentLocation = planetName;
            scene.availibleLocations = new List<string>();
            scene.availibleLocations.Add(planetName);
        }

        // Planet for the selected start system.
        string planetPrefab = GetPlanetPrefabForSystem(planetName);
        SceneFunctions.GeneratePlanet(planetPrefab, "none", true, 150, 0, 0, 0, 0, 0, 0);
        MainMenuFunctions.AddLogToLoadingScreen("Planet " + planetName + " (" + planetPrefab + ") loaded.", startTime);

        // Player ship.
        string allegiance = GetAllegianceForShip(shipType);
        Vector3 playerPos = new Vector3(0, 0, -200);
        Quaternion playerRot = Quaternion.identity;
        SceneFunctions.LoadSingleShip(playerPos, playerRot, shipType, "player01", allegiance, "no cargo", false, false, false, "red", true);
        MainMenuFunctions.AddLogToLoadingScreen("Player ship " + shipType + " loaded.", startTime);

        // A couple of ambient ships so the scene is not empty.
        SceneFunctions.LoadSingleShip(new Vector3(500, 100, 500), Quaternion.Euler(0, 180, 0), shipType, "escort01", allegiance, "no cargo", false, true, false, "red", true);
        SceneFunctions.LoadSingleShip(new Vector3(-600, -50, 800), Quaternion.Euler(0, 180, 0), "tiefighter", "hostile01", "hostile", "no cargo", false, true, false, "red", true);

        yield return null;

        OGCameraFunctions.ActivateOGCamera();

        MainMenuFunctions.AddLogToLoadingScreen("Galaxy Mode loaded.", startTime);

        Time.timeScale = 1;
        Cursor.lockState = CursorLockMode.Confined;
        Cursor.visible = false;

        MissionFunctions.DisplayLoadingScreen(missionManager.missionName, false);

        missionManager.running = true;
        missionManager.eventNo = new List<int>();
        missionManager.missionTasks = new List<Task>();

        HudFunctions.AddToMissionLog("Galaxy Mode: " + planetName + " - free roam");
        HudFunctions.AddToMissionLog("Press M to open the galaxy map and jump to another system.");
    }

    private static string GetAllegianceForShip(string shipType)
    {
        TextAsset shipTypesFile = Resources.Load(OGGetAddress.files + "ShipTypes") as TextAsset;

        if (shipTypesFile != null)
        {
            ShipTypes shipTypes = JsonUtility.FromJson<ShipTypes>(shipTypesFile.text);

            if (shipTypes != null && shipTypes.shipTypeData != null)
            {
                foreach (ShipType s in shipTypes.shipTypeData)
                {
                    if (s != null && s.type == shipType && !string.IsNullOrEmpty(s.allegiance))
                    {
                        return s.allegiance;
                    }
                }
            }
        }

        return "rebel";
    }

    private static string GetPlanetPrefabForSystem(string planetName)
    {
        return GetPlanetPrefabForSystemPublic(planetName);
    }

    public static string GetPlanetPrefabForSystemPublic(string planetName)
    {
        string planetType = "terran01";

        TextAsset starSystemFile = Resources.Load(OGGetAddress.files + "StarSystems") as TextAsset;

        if (starSystemFile != null)
        {
            StarSystems starSystems = JsonUtility.FromJson<StarSystems>(starSystemFile.text);

            if (starSystems != null && starSystems.starSystemsData != null)
            {
                foreach (StarSystem system in starSystems.starSystemsData)
                {
                    if (system != null && system.Planet == planetName && !string.IsNullOrEmpty(system.planetType))
                    {
                        planetType = system.planetType;
                        break;
                    }
                }
            }
        }

        return MapPlanetTypeToPrefab(planetType);
    }

    private static string MapPlanetTypeToPrefab(string planetType)
    {
        if (string.IsNullOrEmpty(planetType))
        {
            return "terran01";
        }

        string t = planetType.ToLower();

        if (t == "water") return "oceanic01";
        if (t == "forest") return "terran03";
        if (t == "desert") return "desert01";
        if (t == "ice") return "snow01";
        if (t == "city") return "coruscant";
        if (t == "volcanic") return "volcanic01";
        if (t == "barren") return "moon01";
        if (t == "grasslands") return "terran01";
        if (t == "tropical") return "terran02";
        if (t == "redplanet") return "korriban";

        return "terran01";
    }
}
