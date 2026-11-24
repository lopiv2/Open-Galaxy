using System.Collections;
using UnityEngine;

// Herramienta de prueba: A-Wing directo en Tatooine (planeta, con desert.png)
// sin pasar por inicio -> Galaxy Mode -> mapa -> volar hasta el planeta.
public static class GalaxyPlanetTestTool
{
    public const string TestShip = "xwing";
    public const string TestPlanet = "Tatooine";

    // Llamado desde el menu principal (Tools) y desde el menu Tools del editor.
    public static void TestAwingTatooine()
    {
        GalaxyModeFunctions.SelectedShipType = TestShip;
        GalaxyModeFunctions.SelectedStartPlanet = TestPlanet;
        GalaxyModeFunctions.SelectedFaction = GalaxyModeFunctions.GetAllegianceForShip(TestShip);
        GalaxyModeFunctions.StartGalaxyMode(TestPlanet);

        EnsureAutoEnter();
    }

    private static void EnsureAutoEnter()
    {
        GameObject go = GameObject.Find("PlanetTestAutoEnter");

        if (go == null)
        {
            go = new GameObject("PlanetTestAutoEnter");
            Object.DontDestroyOnLoad(go);
        }

        AutoEnter enter = go.GetComponent<AutoEnter>();

        if (enter == null)
        {
            go.AddComponent<AutoEnter>();
        }
    }

    private class AutoEnter : MonoBehaviour
    {
        private float timeout = 60f;

        private IEnumerator Start()
        {
            float elapsed = 0f;

            while (elapsed < timeout)
            {
                // RunGalaxyMode is a coroutine and may be interrupted (domain
                // reload, menu double-click, lost focus). Repair the minimum
                // state needed for a planet entry instead of just timing out.
                MissionManager missionManager = FindFirstObjectByType<MissionManager>();

                if (missionManager == null)
                {
                    GameObject managerObject = new GameObject("MissionManager");
                    missionManager = managerObject.AddComponent<MissionManager>();
                    managerObject.AddComponent<GalaxyMapController>();
                    managerObject.AddComponent<GalaxyPlanetEntry>();
                    missionManager.missionName = "Planet Test - " + TestPlanet;
                    missionManager.missionAddress = OGGetAddress.missions_internal;
                    missionManager.missionData = "{\"missionEventData\":[]}";
                    missionManager.audioLoaded = true;
                    missionManager.running = false;
                }

                if (UnityEngine.Object.FindAnyObjectByType<Audio>() == null)
                    AudioFunctions.CreateAudioManager();

                GalaxyPlanetEntry entry = GalaxyPlanetEntry.Instance;
                if (entry == null)
                {
                    entry = missionManager.gameObject.GetComponent<GalaxyPlanetEntry>();
                    if (entry == null)
                        entry = missionManager.gameObject.AddComponent<GalaxyPlanetEntry>();
                }

                Scene scene = SceneFunctions.GetScene();
                if (scene == null)
                    scene = SceneFunctions.CreateScene();

                if (scene.shipsPrefabPool == null || scene.shipsPrefabPool.Length == 0)
                    SceneFunctions.LoadScenePrefabs();

                if (string.IsNullOrEmpty(scene.currentLocation))
                {
                    scene.currentLocation = TestPlanet;
                    scene.availibleLocations = new System.Collections.Generic.List<string> { TestPlanet };
                }

                if (scene.mainShip == null)
                {
                    SceneFunctions.LoadSingleShip(
                        new Vector3(0, 0, -200), Quaternion.identity,
                        TestShip, "player01",
                        GalaxyModeFunctions.GetAllegianceForShip(TestShip),
                        "no cargo", false, false, false, "red", true);
                }

                // ForceEnterForTest needs an active planet to descend into.
                GameObject activePlanet;
                Vector3 planetCenter;
                float planetRadius;
                if (!GalaxyPlanetEntry.GetActivePlanet(scene, out activePlanet,
                        out planetCenter, out planetRadius))
                {
                    string planetPrefab =
                        GalaxyModeFunctions.GetPlanetPrefabForSystemPublic(TestPlanet);
                    if (!string.IsNullOrEmpty(planetPrefab))
                    {
                        SceneFunctions.GeneratePlanet(
                            planetPrefab, "none", true,
                            GalaxyModeFunctions.GetPlanetDistanceForSystem(TestPlanet),
                            0, 0, 0, 0, 0, 0);
                    }
                }

                missionManager.scene = scene;
                missionManager.running = true;

                if (missionManager.running == true
                    && entry != null && scene != null && scene.mainShip != null
                    && GalaxyPlanetEntry.IsTransitioning == false
                    && GalaxyPlanetEntry.IsOnPlanet == false)
                {
                    entry.ForceEnterForTest();

                    if (GalaxyPlanetEntry.IsTransitioning)
                        yield break;
                }

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            Debug.LogWarning("[PlanetTest] AutoEnter timeout: no se pudo entrar en " + TestPlanet);
        }
    }
}
