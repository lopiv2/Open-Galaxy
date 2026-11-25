using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Herramientas de prueba de combate: X-Wing contra una nave enemiga estática
// para validar daño, escudos, torretas y efectos de impacto sin pasar por el
// menu principal ni el mapa de la galaxia.
public static class CombatTestTool
{
    public const string PlayerShipType = "xwing";
    public const string FighterTargetType = "xwing";
    public const string CapitalTargetType = "arquitens_light_cruiser_mark1";
    public const string EnemyAllegiance = "imperial";

    // Llamado desde el menu Tools del editor y desde el menu principal (Tools).
    public static void TestFighterCombat()
    {
        StartCombatTest(FighterTargetType, false);
    }

    public static void TestCapitalCombat()
    {
        StartCombatTest(CapitalTargetType, true);
    }

    private static void StartCombatTest(string targetType, bool capitalTarget)
    {
        MainMenu mainMenu = Object.FindAnyObjectByType<MainMenu>();
        Scene scene = SceneFunctions.GetScene();

        // Si el juego ya esta corriendo solo reconfigura la prueba
        bool alreadyRunning = mainMenu != null && mainMenu.missionRunning == true && scene != null && scene.mainShip != null;

        if (alreadyRunning == false)
        {
            GalaxyModeFunctions.SelectedShipType = PlayerShipType;
            GalaxyModeFunctions.SelectedStartPlanet = GalaxyPlanetTestTool.TestPlanet;
            GalaxyModeFunctions.SelectedFaction = GalaxyModeFunctions.GetAllegianceForShip(PlayerShipType);
            GalaxyModeFunctions.SelectedLaserColor = "red";
            GalaxyModeFunctions.StartGalaxyMode(GalaxyPlanetTestTool.TestPlanet);
        }

        GameObject runnerGO = GameObject.Find("CombatTestRunner");

        if (runnerGO == null)
        {
            runnerGO = new GameObject("CombatTestRunner");
            Object.DontDestroyOnLoad(runnerGO);
        }

        CombatTestRunner runner = runnerGO.GetComponent<CombatTestRunner>();

        if (runner == null)
        {
            runner = runnerGO.AddComponent<CombatTestRunner>();
        }

        runner.SetUp(targetType, capitalTarget);
    }

    // Este componente espera a que la escena este lista, coloca la nave enemiga
    // estatica y muestra un panel de datos de combate.
    private class CombatTestRunner : MonoBehaviour
    {
        private string targetType = FighterTargetType;
        private bool capitalTarget;

        private GameObject targetGO;
        private bool ready;
        private bool showOverlay = true;
        private GUIStyle overlayStyle;

        private bool freezeTarget = true;
        private bool enemyAI = true;

        private float timeout = 120;

        public void SetUp(string type, bool capital)
        {
            targetType = type;
            capitalTarget = capital;

            if (ready == true)
            {
                ResetTest();
            }
        }

        private IEnumerator Start()
        {
            // Espera a que el modo galaxia termine de cargar la escena y la nave del jugador
            float elapsed = 0;

            while (elapsed < timeout)
            {
                MissionManager missionManager = FindAnyObjectByType<MissionManager>();
                Scene scene = SceneFunctions.GetScene();

                if (missionManager != null && missionManager.running == true && scene != null && scene.mainShip != null)
                {
                    break;
                }

                elapsed = elapsed + Time.unscaledDeltaTime;
                yield return null;
            }

            if (elapsed >= timeout)
            {
                Debug.LogWarning("[CombatTest] No se pudo preparar la escena de combate.");
                yield break;
            }

            // Deja pasar un frame para que las naves ambientales terminen de crearse
            yield return null;

            RemoveAmbientShips();
            SpawnTarget();

            ready = true;

            HudFunctions.AddToMissionLog("COMBAT TEST: " + PlayerShipType + " vs " + targetType + " (estatica)");
            HudFunctions.AddToMissionLog("F5 = reiniciar escena / F6 = congelar / F7 = IA enemiga / F8 = panel");
        }

        // Elimina las naves ambientales del modo galaxia para dejar la prueba limpia
        private void RemoveAmbientShips()
        {
            Scene scene = SceneFunctions.GetScene();

            if (scene == null || scene.smallShips == null) { return; }

            for (int i = scene.smallShips.Count - 1; i >= 0; i--)
            {
                Ship ship = scene.smallShips[i];

                if (ship == null) { continue; }

                string shipName = ship.gameObject.name.ToLower();

                if (shipName.Contains("escort") || shipName.Contains("hostile"))
                {
                    ship.gameObject.SetActive(false);
                    scene.smallShips.RemoveAt(i);
                }
            }
        }

        // Coloca la nave enemiga delante del jugador y la deja estatica
        private void SpawnTarget()
        {
            Scene scene = SceneFunctions.GetScene();

            if (scene == null || scene.mainShip == null) { return; }

            if (targetGO != null)
            {
                Destroy(targetGO);
                targetGO = null;
            }

            Vector3 offset = new Vector3(0, 0, 900);
            string laserColor = "red";

            if (capitalTarget == true)
            {
                offset = new Vector3(0, 0, 1800);
                laserColor = "green";
            }

            // La nave aparece delante del jugador para poder reiniciar la prueba
            // en cualquier momento sin tener que volver a su posicion inicial
            Vector3 playerPosition = scene.mainShip.transform.position;
            float distance = offset.z;
            Vector3 worldPosition = playerPosition + (scene.mainShip.transform.forward * distance);
            offset = scene.transform.InverseTransformPoint(worldPosition);

            Vector3 direction = (playerPosition - worldPosition).normalized;

            Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);

            int poolCount = 0;

            if (scene.objectPool != null)
            {
                poolCount = scene.objectPool.Count;
            }

            SceneFunctions.LoadSingleShip(offset, rotation, targetType, "target01", EnemyAllegiance, "no cargo", false, true, false, laserColor, true);

            if (scene.objectPool != null & scene.objectPool.Count > poolCount)
            {
                targetGO = scene.objectPool[scene.objectPool.Count - 1];
            }

            if (targetGO == null & scene.objectPool != null)
            {
                foreach (GameObject shipGO in scene.objectPool)
                {
                    if (shipGO != null & shipGO.name.Contains("target01"))
                    {
                        targetGO = shipGO;
                        break;
                    }
                }
            }

            if (targetGO == null)
            {
                Debug.LogWarning("[CombatTest] No se pudo crear la nave objetivo (" + targetType + ").");
                return;
            }

            ConfigureTarget(scene);

            Debug.Log("[CombatTest] Objetivo " + targetType + " creado a " + offset.z + " del jugador.");
        }

        // Deja la nave objetivo estatica y decide si la IA puede disparar
        private void ConfigureTarget(Scene scene)
        {
            if (targetGO == null) { return; }

            Rigidbody rigidbody = targetGO.GetComponent<Rigidbody>();

            if (rigidbody != null)
            {
                rigidbody.linearVelocity = Vector3.zero;
                rigidbody.angularVelocity = Vector3.zero;

                if (freezeTarget == true)
                {
                    rigidbody.constraints = RigidbodyConstraints.FreezeAll;
                }
                else
                {
                    rigidbody.constraints = RigidbodyConstraints.None;
                }
            }

            // Nave capital: la IA de naves grandes anade sus etiquetas por defecto
            // al arrancar, asi que se marca como iniciada y se configuran a mano.
            // Las etiquetas nospeed / norotation la dejan quieta en el sitio.
            LargeShip largeShip = targetGO.GetComponent<LargeShip>();

            if (largeShip != null)
            {
                largeShip.aiStarted = true;
                largeShip.aiTags = new List<string>();
                largeShip.aiTargetingMode = "veryhigh";
                largeShip.target = scene.mainShip;

                LargeShipAIFunctions.AddTag(largeShip, "nospeed");
                LargeShipAIFunctions.AddTag(largeShip, "norotation");

                if (enemyAI == true)
                {
                    LargeShipAIFunctions.AddTag(largeShip, "large_multipletargets_all");
                    LargeShipAIFunctions.AddTag(largeShip, "small_multipletargets_all");
                }
                else
                {
                    LargeShipAIFunctions.AddTag(largeShip, "large_noweapons");
                    LargeShipAIFunctions.AddTag(largeShip, "small_noweapons");
                }
            }

            // Caza: se mantiene la IA para que dispare, pero no puede moverse
            Ship ship = targetGO.GetComponent<Ship>();

            if (ship != null)
            {
                FlightControlAI_Small flightControlAI = targetGO.GetComponent<FlightControlAI_Small>();

                if (flightControlAI != null)
                {
                    flightControlAI.enabled = enemyAI;
                }
            }
        }

        // Reinicia la escena de combate: jugador al punto de inicio y objetivo nuevo
        private void ResetTest()
        {
            Scene scene = SceneFunctions.GetScene();

            if (scene == null) { return; }

            Debug.Log("[CombatTest] Reiniciando escena de combate.");

            ResetPlayer(scene);
            ClearTargets(scene);
            SpawnTarget();

            HudFunctions.AddToMissionLog("COMBAT TEST: escena reiniciada");
        }

        // Devuelve al jugador al punto de inicio con la nave intacta
        private void ResetPlayer(Scene scene)
        {
            Vector3 startPosition = scene.transform.position + new Vector3(0, 0, -200);

            // Si el jugador fue destruido se crea una nave nueva
            if (scene.mainShip == null)
            {
                SceneFunctions.LoadSingleShip(new Vector3(0, 0, -200), Quaternion.identity, PlayerShipType, "player01", GalaxyModeFunctions.GetAllegianceForShip(PlayerShipType), "no cargo", false, false, false, "red", true);
                return;
            }

            scene.mainShip.transform.position = startPosition;
            scene.mainShip.transform.rotation = Quaternion.identity;

            Rigidbody rigidbody = scene.mainShip.GetComponent<Rigidbody>();

            if (rigidbody != null)
            {
                rigidbody.linearVelocity = Vector3.zero;
                rigidbody.angularVelocity = Vector3.zero;
                rigidbody.constraints = RigidbodyConstraints.None;
            }

            Ship ship = scene.mainShip.GetComponent<Ship>();

            if (ship != null)
            {
                ship.jumpingToHyperspace = false;
                ship.exitingHyperspace = false;

                EnergyManagementSystem energy = ship.energyManagementSystem;

                if (energy == null)
                {
                    energy = ship.GetComponent<EnergyManagementSystem>();
                }

                if (energy != null)
                {
                    energy.hullLevel = ship.hullRating;
                    energy.systemsLevel = ship.systemsRating;
                    energy.shieldLevel = ship.shieldRating;
                    energy.frontShieldLevel = ship.shieldRating / 2f;
                    energy.rearShieldLevel = ship.shieldRating / 2f;
                }
            }
        }

        // Destruye todos los objetivos enemigos de la prueba
        private void ClearTargets(Scene scene)
        {
            targetGO = null;

            if (scene.objectPool != null)
            {
                for (int i = scene.objectPool.Count - 1; i >= 0; i--)
                {
                    GameObject shipGO = scene.objectPool[i];

                    if (shipGO == null)
                    {
                        scene.objectPool.RemoveAt(i);
                        continue;
                    }

                    Ship ship = shipGO.GetComponent<Ship>();
                    LargeShip largeShip = shipGO.GetComponent<LargeShip>();

                    bool isEnemyShip = false;

                    if (ship != null & ship.allegiance == EnemyAllegiance) { isEnemyShip = true; }
                    if (largeShip != null & largeShip.allegiance == EnemyAllegiance) { isEnemyShip = true; }

                    if (isEnemyShip == false) { continue; }
                    if (shipGO == scene.mainShip) { continue; }

                    Destroy(shipGO);
                    scene.objectPool.RemoveAt(i);
                }
            }

            if (scene.smallShips != null)
            {
                scene.smallShips.RemoveAll(ship => ship == null || ship.allegiance == EnemyAllegiance);
            }

            if (scene.largeShips != null)
            {
                scene.largeShips.RemoveAll(largeShip => largeShip == null || largeShip.allegiance == EnemyAllegiance);
            }
        }

        private void Update()
        {
            if (ready == false) { return; }

            //Se usan teclas de funcion para no chocar con los controles del juego
            //(R = siguiente objetivo, F = enemigo mas cercano, G = objetivo delante)
            if (WasKeyPressed(KeyCode.F5, UnityEngine.InputSystem.Key.F5))
            {
                ResetTest();
            }

            if (WasKeyPressed(KeyCode.F6, UnityEngine.InputSystem.Key.F6))
            {
                freezeTarget = !freezeTarget;
                ConfigureTarget(SceneFunctions.GetScene());
            }

            if (WasKeyPressed(KeyCode.F7, UnityEngine.InputSystem.Key.F7))
            {
                enemyAI = !enemyAI;
                ConfigureTarget(SceneFunctions.GetScene());
            }

            if (WasKeyPressed(KeyCode.F8, UnityEngine.InputSystem.Key.F8))
            {
                showOverlay = !showOverlay;
            }
        }

        // Lee la tecla con el input antiguo y con el nuevo, por si algun backend no esta activo
        private bool WasKeyPressed(KeyCode legacyKey, UnityEngine.InputSystem.Key newKey)
        {
            bool pressed = Input.GetKeyDown(legacyKey);

            if (pressed == false)
            {
                UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;

                if (keyboard != null)
                {
                    pressed = keyboard[newKey].wasPressedThisFrame;
                }
            }

            return pressed;
        }

        // Panel de datos para ver las logicas de combate en tiempo real
        private void OnGUI()
        {
            if (showOverlay == false) { return; }

            GUIStyle style = GetOverlayStyle();

            if (ready == false)
            {
                GUILayout.BeginArea(new Rect(10, 10, 260, 40), GUI.skin.box);
                GUILayout.Label("COMBAT TEST: preparando escena...", style);
                GUILayout.EndArea();
                return;
            }

            Scene scene = SceneFunctions.GetScene();

            if (scene == null) { return; }

            GUILayout.BeginArea(new Rect(10, 10, 360, 300), GUI.skin.box);

            GUILayout.Label("COMBAT TEST", style);
            GUILayout.Label(PlayerShipType + " vs " + targetType + (capitalTarget == true ? " (capital)" : " (caza)"), style);
            GUILayout.Space(4);
            GUILayout.Label("[F5] reiniciar escena  [F6] congelar: " + (freezeTarget ? "ON" : "OFF") + "  [F7] IA: " + (enemyAI ? "ON" : "OFF") + "  [F8] panel", style);
            GUILayout.Space(6);

            DrawShipInfo(scene.mainShip, "JUGADOR", false, style);

            GUILayout.Space(6);

            DrawShipInfo(targetGO, "OBJETIVO", true, style);

            GUILayout.EndArea();
        }

        private void DrawShipInfo(GameObject shipGO, string label, bool isTarget, GUIStyle style)
        {
            if (shipGO == null)
            {
                GUILayout.Label(label + ": destruido", style);
                return;
            }

            GUILayout.Label(label + ": " + shipGO.name, style);

            Ship ship = shipGO.GetComponent<Ship>();

            if (ship != null)
            {
                EnergyManagementSystem energy = ship.energyManagementSystem;

                if (energy != null)
                {
                    GUILayout.Label("  hull " + energy.hullLevel.ToString("0") + "/" + ship.hullRating.ToString("0") + "   shield " + energy.shieldLevel.ToString("0") + "/" + ship.shieldRating.ToString("0"), style);
                    GUILayout.Label("  escudo frente " + energy.frontShieldLevel.ToString("0") + "   atras " + energy.rearShieldLevel.ToString("0"), style);
                }

                GUILayout.Label("  escudo visual: " + ShieldVisualState(ship.gameObject), style);
            }

            LargeShip largeShip = shipGO.GetComponent<LargeShip>();

            if (largeShip != null)
            {
                GUILayout.Label("  hull " + largeShip.hullLevel.ToString("0") + "/" + largeShip.hullRating.ToString("0") + "   shield " + largeShip.shieldLevel.ToString("0") + "/" + largeShip.shieldRating.ToString("0"), style);
                GUILayout.Label("  escudo frente " + largeShip.frontShieldLevel.ToString("0") + "   atras " + largeShip.rearShieldLevel.ToString("0"), style);
                GUILayout.Label("  escudo visual: " + ShieldVisualState(largeShip.gameObject), style);
            }

            if (isTarget == true & targetGO != null)
            {
                Scene scene = SceneFunctions.GetScene();

                if (scene != null & scene.mainShip != null)
                {
                    GUILayout.Label("  distancia " + Vector3.Distance(shipGO.transform.position, scene.mainShip.transform.position).ToString("0") + " m", style);
                }
            }
        }

        // Devuelve el estado del efecto de escudo para depurar
        private string ShieldVisualState(GameObject shipGO)
        {
            ShieldVisual shieldVisual = shipGO.GetComponent<ShieldVisual>();

            if (shieldVisual == null) { return "sin componente"; }
            if (shieldVisual.setUp == false) { return "sin preparar"; }
            if (shieldVisual.shieldRenderers == null || shieldVisual.shieldRenderers.Count == 0) { return "sin malla"; }
            if (shieldVisual.renderersEnabled == false) { return "oculto"; }

            return shieldVisual.shieldFraction.ToString("0.00") + (shieldVisual.impactsActive ? " (onda)" : "");
        }

        private GUIStyle GetOverlayStyle()
        {
            if (overlayStyle == null)
            {
                overlayStyle = new GUIStyle(GUI.skin.label);
                overlayStyle.fontSize = 12;
                overlayStyle.richText = false;
                overlayStyle.normal.textColor = Color.white;
            }

            return overlayStyle;
        }
    }
}
