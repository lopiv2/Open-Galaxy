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
    public static string SelectedFaction = "rebel";
    public static string SelectedLaserColor = "red";

    public static readonly string[] LaserColors = new string[] { "red", "green", "yellow" };

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
        "Mon Calamari",
        "Wrea",
        "Smuggler's Run"
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
            SelectedFaction = GetAllegianceForShip(SelectedShipType);
        }

        string label = SelectedShipType;

        foreach (ShipType s in GetFlyableShips())
        {
            if (s != null && s.type == SelectedShipType)
            {
                label = GetShipDisplayName(s);
                break;
            }
        }

        MainMenuFunctions.OutputMenuMessage("Ship selected: " + label);
        MainMenuFunctions.ActivateSubMenu("Galaxy Faction");
    }

    // Called from the Galaxy Faction submenu.
    public static void SelectGalaxyFaction(string faction)
    {
        if (!string.IsNullOrEmpty(faction))
        {
            SelectedFaction = faction;
        }

        MainMenuFunctions.OutputMenuMessage("Faction selected: " + GetFactionDisplayName(SelectedFaction));
        MainMenuFunctions.ActivateSubMenu("Galaxy Laser");
    }

    // Called from the Galaxy Laser submenu.
    public static void SelectGalaxyLaser(string laserColor)
    {
        if (!string.IsNullOrEmpty(laserColor))
        {
            SelectedLaserColor = laserColor.ToLower();
        }

        MainMenuFunctions.OutputMenuMessage("Laser color selected: " + SelectedLaserColor);
        MainMenuFunctions.ActivateSubMenu("Galaxy Map");
    }

    // Playable factions for the Galaxy Mode faction picker.
    public static List<Allegiance> GetPlayableFactions()
    {
        List<Allegiance> result = new List<Allegiance>();

        TextAsset allegiancesFile = Resources.Load(OGGetAddress.files + "Allegiances") as TextAsset;

        if (allegiancesFile == null)
        {
            return result;
        }

        Allegiances allegiances = JsonUtility.FromJson<Allegiances>(allegiancesFile.text);

        if (allegiances == null || allegiances.allegianceData == null)
        {
            return result;
        }

        foreach (Allegiance allegiance in allegiances.allegianceData)
        {
            if (allegiance == null || string.IsNullOrEmpty(allegiance.allegiance))
            {
                continue;
            }

            if (allegiance.allegiance == "none" || allegiance.allegiance == "hostile" || allegiance.allegiance == "independent")
            {
                continue;
            }

            result.Add(allegiance);
        }

        result.Sort((a, b) => string.Compare(GetFactionDisplayName(a.allegiance), GetFactionDisplayName(b.allegiance)));

        return result;
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
        SceneFunctions.GeneratePlanet(planetPrefab, "none", true, GetPlanetDistanceForSystem(planetName), 0, 0, 0, 0, 0, 0);
        MainMenuFunctions.AddLogToLoadingScreen("Planet " + planetName + " (" + planetPrefab + ") loaded.", startTime);

        // Suns declared in StarSystems.json (e.g. Tatooine's binary suns).
        GalaxySunFunctions.SpawnSunsForSystem(planetName);

        // Player ship (faction and laser color chosen in the Galaxy Mode flow).
        string allegiance = string.IsNullOrEmpty(SelectedFaction) ? GetAllegianceForShip(shipType) : SelectedFaction;
        string playerLaser = string.IsNullOrEmpty(SelectedLaserColor) ? "red" : SelectedLaserColor;
        Vector3 playerPos = new Vector3(0, 0, -200);
        Quaternion playerRot = Quaternion.identity;
        SceneFunctions.LoadSingleShip(playerPos, playerRot, shipType, "player01", allegiance, "no cargo", false, false, false, playerLaser, true);
        MainMenuFunctions.AddLogToLoadingScreen("Player ship " + shipType + " loaded.", startTime);

        // A couple of ambient ships so the scene is not empty.
        SceneFunctions.LoadSingleShip(new Vector3(500, 100, 500), Quaternion.Euler(0, 180, 0), shipType, "escort01", allegiance, "no cargo", false, true, false, playerLaser, true);
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

    private static Dictionary<string, string> factionDisplayNames;

    // Display helpers: prefer the explicit displayName from the JSON files,
    // falling back to a formatted identifier so older entries still read well.
    public static string GetShipDisplayName(ShipType shipType)
    {
        if (shipType == null)
        {
            return "Unknown";
        }

        if (!string.IsNullOrEmpty(shipType.displayName))
        {
            return shipType.displayName;
        }

        return FormatIdAsName(shipType.type);
    }

    public static string GetFactionDisplayName(string faction)
    {
        if (string.IsNullOrEmpty(faction))
        {
            return faction;
        }

        if (factionDisplayNames == null)
        {
            LoadFactionDisplayNames();
        }

        string displayName;

        if (factionDisplayNames.TryGetValue(faction, out displayName) && !string.IsNullOrEmpty(displayName))
        {
            return displayName;
        }

        return FormatIdAsName(faction);
    }

    private static void LoadFactionDisplayNames()
    {
        factionDisplayNames = new Dictionary<string, string>();

        TextAsset allegiancesFile = Resources.Load(OGGetAddress.files + "Allegiances") as TextAsset;

        if (allegiancesFile == null)
        {
            return;
        }

        Allegiances allegiances = JsonUtility.FromJson<Allegiances>(allegiancesFile.text);

        if (allegiances == null || allegiances.allegianceData == null)
        {
            return;
        }

        foreach (Allegiance allegiance in allegiances.allegianceData)
        {
            if (allegiance != null && !string.IsNullOrEmpty(allegiance.allegiance) && !factionDisplayNames.ContainsKey(allegiance.allegiance))
            {
                factionDisplayNames.Add(allegiance.allegiance, allegiance.displayName);
            }
        }
    }

    public static string FormatIdAsName(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return id;
        }

        string[] parts = id.Replace('-', '_').Split('_');

        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
            {
                parts[i] = char.ToUpper(parts[i][0]) + parts[i].Substring(1);
            }
        }

        return string.Join(" ", parts);
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
                    if (system != null && system.Planet == planetName)
                    {
                        if (!string.IsNullOrEmpty(system.planetPrefab))
                        {
                            return system.planetPrefab;
                        }

                        if (!string.IsNullOrEmpty(system.planetType))
                        {
                            planetType = system.planetType;
                        }

                        break;
                    }
                }
            }
        }

        return MapPlanetTypeToPrefab(planetType);
    }

    // Optional per-system planet distance (StarSystems.json "planetDistance").
    // Falls back to the historical default so other systems render unchanged.
    public static float GetPlanetDistanceForSystem(string planetName)
    {
        TextAsset starSystemFile = Resources.Load(OGGetAddress.files + "StarSystems") as TextAsset;

        if (starSystemFile != null)
        {
            StarSystems starSystems = JsonUtility.FromJson<StarSystems>(starSystemFile.text);

            if (starSystems != null && starSystems.starSystemsData != null)
            {
                foreach (StarSystem system in starSystems.starSystemsData)
                {
                    if (system != null && system.Planet == planetName && system.planetDistance > 0f)
                    {
                        return system.planetDistance;
                    }
                }
            }
        }

        return 150f;
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
