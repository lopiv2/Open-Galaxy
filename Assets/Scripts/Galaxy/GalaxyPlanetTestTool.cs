using System.Collections;
using UnityEngine;

// Herramienta de prueba: A-Wing directo en Tatooine (planeta, con desert.png)
// sin pasar por inicio -> Galaxy Mode -> mapa -> volar hasta el planeta.
public static class GalaxyPlanetTestTool
{
    public const string TestShip = "awing";
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
                MissionManager missionManager = FindFirstObjectByType<MissionManager>();
                GalaxyPlanetEntry entry = GalaxyPlanetEntry.Instance;
                Scene scene = SceneFunctions.GetScene();

                if (missionManager != null && missionManager.running == true
                    && entry != null && scene != null && scene.mainShip != null
                    && GalaxyPlanetEntry.IsTransitioning == false
                    && GalaxyPlanetEntry.IsOnPlanet == false)
                {
                    entry.ForceEnterForTest();
                    yield break;
                }

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            Debug.LogWarning("[PlanetTest] AutoEnter timeout: no se pudo entrar en " + TestPlanet);
        }
    }
}
