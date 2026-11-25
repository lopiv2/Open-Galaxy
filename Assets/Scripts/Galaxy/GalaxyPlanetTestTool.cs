using System.Collections;
using UnityEngine;

// Herramientas de prueba: A-Wing directo en Tatooine (planeta, con desert.png)
// sin pasar por inicio -> Galaxy Mode -> mapa -> volar hasta el planeta.
public static class GalaxyPlanetTestTool
{
    public const string TestShip = "xwing";
    public const string TestPlanet = "Tatooine";

    private static bool footTestRequested;

    // Llamado desde el menu principal (Tools) y desde el menu Tools del editor.
    public static void TestAwingTatooine()
    {
        GalaxyModeFunctions.SelectedShipType = TestShip;
        GalaxyModeFunctions.SelectedStartPlanet = TestPlanet;
        GalaxyModeFunctions.SelectedFaction = GalaxyModeFunctions.GetAllegianceForShip(TestShip);
        GalaxyModeFunctions.StartGalaxyMode(TestPlanet);

        EnsureAutoEnter(false);
    }

    // Nuevo modo: igual que el anterior pero la nave aparece ya aterrizada y
    // el personaje a pie, para probar el modo caminar sin aterrizar a mano.
    public static void TestOnFootTatooine()
    {
        GalaxyModeFunctions.SelectedShipType = TestShip;
        GalaxyModeFunctions.SelectedStartPlanet = TestPlanet;
        GalaxyModeFunctions.SelectedFaction = GalaxyModeFunctions.GetAllegianceForShip(TestShip);
        GalaxyModeFunctions.StartGalaxyMode(TestPlanet);

        EnsureAutoEnter(true);
    }

    private static void EnsureAutoEnter(bool footTest)
    {
        footTestRequested = footTestRequested || footTest;

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
            bool ready = false;

            while (elapsed < timeout)
            {
                MissionManager missionManager = FindAnyObjectByType<MissionManager>();
                GalaxyPlanetEntry entry = GalaxyPlanetEntry.Instance;
                Scene scene = SceneFunctions.GetScene();

                if (missionManager != null && missionManager.running == true
                    && entry != null && scene != null && scene.mainShip != null
                    && GalaxyPlanetEntry.IsTransitioning == false
                    && GalaxyPlanetEntry.IsOnPlanet == false)
                {
                    ready = true;
                    break;
                }

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (ready == false)
            {
                Debug.LogWarning("[PlanetTest] AutoEnter timeout: no se pudo entrar en " + TestPlanet);
                yield break;
            }

            GalaxyPlanetEntry.Instance.ForceEnterForTest();

            if (footTestRequested == false)
            {
                yield break;
            }

            yield return StartCoroutine(FootSequence());
            footTestRequested = false;
        }

        // Secuencia del test a pie: espera al planeta, deja la nave baja sobre
        // el terreno, ejecuta el aterrizaje real (como pulsar Z) y sale a pie
        // (como pulsar C).
        private IEnumerator FootSequence()
        {
            float elapsed = 0f;

            while (elapsed < 90f)
            {
                if (GalaxyPlanetEntry.IsOnPlanet && GalaxyPlanetEntry.IsTransitioning == false)
                {
                    break;
                }

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (GalaxyPlanetEntry.IsOnPlanet == false)
            {
                Debug.LogWarning("[PlanetTest] Foot test: no se entró en " + TestPlanet);
                yield break;
            }

            Scene scene = SceneFunctions.GetScene();

            if (scene == null || scene.mainShip == null)
            {
                Debug.LogWarning("[PlanetTest] Foot test: sin nave.");
                yield break;
            }

            // Espera a las tiles bajo la nave manteniéndola quieta.
            float groundY = float.NaN;
            elapsed = 0f;

            while (float.IsNaN(groundY) && elapsed < 60f)
            {
                scene = SceneFunctions.GetScene();

                if (scene == null || scene.mainShip == null)
                {
                    yield break;
                }

                HoldShipStill(scene);
                groundY = SampleGroundHeight(scene.mainShip.transform.position);

                if (float.IsNaN(groundY))
                {
                    elapsed += 0.5f;
                    yield return new WaitForSeconds(0.5f);
                }
            }

            if (float.IsNaN(groundY))
            {
                Debug.LogWarning("[PlanetTest] Foot test: sin terreno bajo la nave.");
                yield break;
            }

            Vector3 shipPos = scene.mainShip.transform.position;
            scene.mainShip.transform.position = new Vector3(shipPos.x, groundY + 30f, shipPos.z);
            HoldShipStill(scene);

            SoftLanding landing = scene.mainShip.GetComponent<SoftLanding>();

            if (landing == null)
            {
                Debug.LogWarning("[PlanetTest] Foot test: la nave no tiene SoftLanding.");
                yield break;
            }

            if (landing.TryLandForTest() == false)
            {
                Debug.LogWarning("[PlanetTest] Foot test: no se pudo iniciar el aterrizaje.");
                yield break;
            }

            elapsed = 0f;

            while (landing.LandedState == false && elapsed < 25f)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (landing.LandedState == false)
            {
                Debug.LogWarning("[PlanetTest] Foot test: el aterrizaje no terminó.");
                yield break;
            }

            GroundCharacterController walker = GroundCharacterController.Instance;

            if (walker == null)
            {
                Debug.LogWarning("[PlanetTest] Foot test: sin GroundCharacterController.");
                yield break;
            }

            bool onFoot = walker.EnterFootModeForTest();
            Debug.Log("[PlanetTest] Modo a pie: " + (onFoot ? "ACTIVO (WASD + Shift, C para subir)" : "FALLO"));
        }

        private static void HoldShipStill(Scene scene)
        {
            Rigidbody body = scene.mainShip.GetComponent<Rigidbody>();

            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            FlightControlSystem_Small flight = scene.mainShip.GetComponent<FlightControlSystem_Small>();

            if (flight != null)
            {
                flight.thrustSpeed = 0f;
                flight.thrustInput = 0f;
            }
        }

        // Mismo criterio que SoftLanding: solo las tiles cuentan como suelo.
        private static float SampleGroundHeight(Vector3 worldPosition)
        {
            Vector3 origin = new Vector3(worldPosition.x, worldPosition.y + 400f, worldPosition.z);
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, origin.y + 2000f, ~0, QueryTriggerInteraction.Ignore);
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
}
