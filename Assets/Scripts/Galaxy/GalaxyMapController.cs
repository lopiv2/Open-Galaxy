using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

// In-game 2D galaxy map for Galaxy Mode.
// M toggles the map. Clicking another system jumps via hyperspace
// (starfield stretch -> swap planet -> shrink) without reloading the scene.
public class GalaxyMapController : MonoBehaviour
{
    public static GalaxyMapController Instance;
    public static bool IsMapOpen = false;
    public static bool IsTraveling = false;

    private static GameObject mapCanvasGO;
    private static GameObject toggleCanvasGO;
    private static GameObject countdownCanvasGO;
    private static Text countdownText;
    private float nextToggleTime;

    void Awake()
    {
        Instance = this;
        IsMapOpen = false;
        IsTraveling = false;
        mapCanvasGO = null;
        nextToggleTime = 0f;
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        IsMapOpen = false;
        IsTraveling = false;

        if (mapCanvasGO != null)
        {
            GameObject.Destroy(mapCanvasGO);
            mapCanvasGO = null;
        }

        if (toggleCanvasGO != null)
        {
            GameObject.Destroy(toggleCanvasGO);
            toggleCanvasGO = null;
        }

        GalaxySunFunctions.ClearSuns();

        if (countdownCanvasGO != null)
        {
            GameObject.Destroy(countdownCanvasGO);
            countdownCanvasGO = null;
            countdownText = null;
        }
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

        if (IsTraveling == true)
        {
            return;
        }

        // Don't fight the exit menu / briefing.
        GameObject exitMenu = GameObject.Find("ExitMenu");
        if (exitMenu != null && exitMenu.activeSelf == true)
        {
            return;
        }

        bool mapKeyPressed = false;

        Keyboard keyboard = Keyboard.current;

        if (keyboard != null && keyboard.mKey.wasPressedThisFrame == true)
        {
            mapKeyPressed = true;
        }

        // Fallback for old input manager (project mixes Input.GetKey and InputSystem).
        if (Input.GetKeyDown(KeyCode.M) == true)
        {
            mapKeyPressed = true;
        }

        Gamepad gamepad = Gamepad.current;

        if (gamepad != null && gamepad.selectButton.wasPressedThisFrame == true)
        {
            mapKeyPressed = true;
        }

        if (mapKeyPressed == true && Time.unscaledTime >= nextToggleTime)
        {
            nextToggleTime = Time.unscaledTime + 0.5f;
            Debug.Log("[GalaxyMap] Toggle key pressed. Open=" + IsMapOpen);
            ToggleMap();
            Debug.Log("[GalaxyMap] After toggle. Open=" + IsMapOpen);
        }

        EnsureToggleButton(missionManager);
    }

    public static void ToggleMap()
    {
        if (IsMapOpen == true)
        {
            HideMap();
        }
        else
        {
            ShowMap();
        }
    }

    public static void ShowMap()
    {
        Scene scene = SceneFunctions.GetScene();

        if (scene == null)
        {
            Debug.LogWarning("[GalaxyMap] ShowMap blocked: no Scene.");
            return;
        }

        if (scene.mainShip == null)
        {
            Debug.LogWarning("[GalaxyMap] ShowMap blocked: no mainShip yet.");
            return;
        }

        EnsureEventSystem();

        if (mapCanvasGO == null)
        {
            mapCanvasGO = BuildMapCanvas();
        }

        RefreshMapButtons();

        mapCanvasGO.SetActive(true);
        IsMapOpen = true;

        Time.timeScale = 0;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    public static void HideMap()
    {
        if (mapCanvasGO != null)
        {
            mapCanvasGO.SetActive(false);
        }

        IsMapOpen = false;

        // Only restore flight state if we are still in a galaxy mission.
        MissionManager missionManager = GameObject.FindFirstObjectByType<MissionManager>();

        if (missionManager != null && missionManager.missionName != null && missionManager.missionName.StartsWith("Galaxy Mode"))
        {
            Time.timeScale = 1;
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Confined;
        }
    }

    public static void TravelToSystem(string planetName)
    {
        if (IsTraveling == true || string.IsNullOrEmpty(planetName))
        {
            return;
        }

        Scene scene = SceneFunctions.GetScene();

        if (scene == null)
        {
            return;
        }

        if (scene.currentLocation == planetName)
        {
            return;
        }

        HideMap();

        if (Instance != null)
        {
            IsTraveling = true;
            Instance.StartCoroutine(JumpRoutine(planetName));
        }
    }

    private static IEnumerator JumpRoutine(string destination)
    {
        Scene scene = SceneFunctions.GetScene();
        MissionManager missionManager = GameObject.FindFirstObjectByType<MissionManager>();

        if (scene == null || scene.mainShip == null)
        {
            IsTraveling = false;
            yield break;
        }

        GameObject mainShip = scene.mainShip;

        Ship ship = mainShip.GetComponent<Ship>();
        FlightControlSystem_Small flight = mainShip.GetComponent<FlightControlSystem_Small>();
        OGCamera ogCamera = OGCameraFunctions.GetOGCamera();

        if (ship == null || flight == null)
        {
            Debug.LogError("[GalaxyMap] Cannot hyperspace jump: player ship components are missing.");
            IsTraveling = false;
            yield break;
        }

        // Give the player a visible five-second notice before the regular hyperspace sequence.
        ShowCountdown("HYPERSPACE JUMP TO " + destination.ToUpper());

        for (int seconds = 5; seconds > 0; seconds--)
        {
            if (countdownText != null)
            {
                countdownText.text = "HYPERSPACE JUMP TO " + destination.ToUpper() + "\n\n" + seconds;
            }

            yield return new WaitForSecondsRealtime(1f);
        }

        if (countdownText != null)
        {
            countdownText.text = "HYPERSPACE JUMP TO " + destination.ToUpper() + "\n\nENGAGING";
        }

        bool previousControlLock = flight.controlLock;
        bool previousInvincible = ship.damageSystem != null && ship.damageSystem.invincible;
        bool previousFocusCamera = flight.focusCamera;

        scene.hyperspace = true;
        flight.controlLock = true;
        flight.focusCamera = false;

        if (ship.damageSystem != null)
        {
            ship.damageSystem.invincible = true;
        }

        WingSystemFunctions.CloseWings(ship.wingSystems);
        yield return new WaitForSecondsRealtime(3.4f);

        AudioFunctions.PlayAudioClip(ship.audioManager, "hyperspace01_entry", "Cockpit", mainShip.transform.position, 0, 1, 500, 1, 100);
        flight.inHyperspace = true;
        yield return new WaitForSecondsRealtime(2f);

        float hyperspaceStartTime = Time.unscaledTime;
        HudFunctions.AddToMissionLog("Jumping to " + destination + "...");

        if (ogCamera != null && ogCamera.planetCamera != null)
        {
            Camera planetCamera = ogCamera.planetCamera.GetComponent<Camera>();
            if (planetCamera != null) planetCamera.enabled = false;
        }

        // Match mission jumps: clear the old location, stretch stars and show the tunnel.
        GalaxySunFunctions.ClearSuns();
        SceneFunctions.ClearLocation();
        mainShip.transform.localPosition = new Vector3(0, 0, -200);

        Time.timeScale = 0f;
        Task stretch = new Task(SceneFunctions.StretchStarfield());
        while (stretch.Running == true)
        {
            yield return null;
        }

        if (scene.hyperspaceTunnel == null && scene.hyperspaceTunnelPrefab != null)
        {
            scene.hyperspaceTunnel = GameObject.Instantiate(scene.hyperspaceTunnelPrefab);
        }

        if (scene.hyperspaceTunnel != null)
        {
            scene.hyperspaceTunnel.transform.SetParent(mainShip.transform);
            scene.hyperspaceTunnel.transform.localPosition = Vector3.zero;
            scene.hyperspaceTunnel.transform.localRotation = Quaternion.identity;
            scene.hyperspaceTunnel.SetActive(true);
            SceneFunctions.SetStarfieldToInvisible(true);
        }

        string planetPrefab = GalaxyModeFunctions.GetPlanetPrefabForSystemPublic(destination);
        SceneFunctions.GeneratePlanet(planetPrefab, "none", true, GalaxyModeFunctions.GetPlanetDistanceForSystem(destination), 0, 0, 0, 0, 0, 0);
        GalaxySunFunctions.SpawnSunsForSystem(destination);
        scene.currentLocation = destination;
        scene.availibleLocations = new List<string> { destination };

        if (missionManager != null)
        {
            missionManager.missionName = "Galaxy Mode - " + destination;
            missionManager.scene = scene;
        }

        // Regular mission hyperspace holds the tunnel for at least ten seconds.
        while (hyperspaceStartTime + 10f > Time.unscaledTime)
        {
            yield return null;
        }

        Time.timeScale = 1f;
        yield return new WaitForSecondsRealtime(1f);
        flight.inHyperspace = false;

        if (scene.hyperspaceTunnel != null)
        {
            SceneFunctions.SetStarfieldToInvisible(false);
            scene.hyperspaceTunnel.SetActive(false);
            scene.hyperspaceTunnel.transform.SetParent(null);
        }

        AudioFunctions.PlayAudioClip(ship.audioManager, "hyperspace03_exit", "Cockpit", mainShip.transform.position, 0, 1, 500, 1, 100);

        if (ogCamera != null && ogCamera.starfieldCamera != null)
        {
            Camera starfieldCamera = ogCamera.starfieldCamera.GetComponent<Camera>();
            if (starfieldCamera != null) starfieldCamera.enabled = true;
        }

        Task shrink = new Task(SceneFunctions.ShrinkStarfield());
        while (shrink.Running == true)
        {
            yield return null;
        }

        if (ogCamera != null && ogCamera.planetCamera != null)
        {
            Camera planetCamera = ogCamera.planetCamera.GetComponent<Camera>();
            if (planetCamera != null) planetCamera.enabled = true;
        }

        flight.controlLock = previousControlLock;
        flight.focusCamera = previousFocusCamera;

        if (ship.damageSystem != null)
        {
            ship.damageSystem.invincible = previousInvincible;
        }

        scene.hyperspace = false;
        if (ship.wingSystems != null)
        {
            WingSystemFunctions.OpenWings(ship.wingSystems);
        }
        Time.timeScale = 1f;

        HideCountdown();
        HudFunctions.AddToMissionLog("Arrived at " + destination + " - press M for galaxy map.");
        IsTraveling = false;
    }

    private static void ShowCountdown(string title)
    {
        if (countdownCanvasGO == null)
        {
            Font font = GetUIFont();
            countdownCanvasGO = new GameObject("GalaxyHyperspaceCountdown");
            Canvas canvas = countdownCanvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 110;
            countdownCanvasGO.AddComponent<CanvasScaler>();

            GameObject panel = new GameObject("CountdownPanel");
            panel.transform.SetParent(countdownCanvasGO.transform, false);
            RectTransform panelRect = panel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.2f, 0.3f);
            panelRect.anchorMax = new Vector2(0.8f, 0.7f);
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0f, 0.02f, 0.08f, 0.75f);

            GameObject textGO = new GameObject("CountdownText");
            textGO.transform.SetParent(panel.transform, false);
            RectTransform textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(20f, 20f);
            textRect.offsetMax = new Vector2(-20f, -20f);
            countdownText = textGO.AddComponent<Text>();
            countdownText.font = font;
            countdownText.fontSize = 36;
            countdownText.alignment = TextAnchor.MiddleCenter;
            countdownText.color = Color.white;
        }

        countdownText.text = title + "\n\n5";
        countdownCanvasGO.SetActive(true);
    }

    private static void HideCountdown()
    {
        if (countdownCanvasGO != null)
        {
            countdownCanvasGO.SetActive(false);
        }
    }

    private static void EnsureToggleButton(MissionManager missionManager)
    {
        bool inGalaxyMode = missionManager != null
            && missionManager.missionName != null
            && missionManager.missionName.StartsWith("Galaxy Mode")
            && missionManager.running == true;

        if (inGalaxyMode == false)
        {
            if (toggleCanvasGO != null)
            {
                toggleCanvasGO.SetActive(false);
            }

            return;
        }

        if (toggleCanvasGO == null)
        {
            toggleCanvasGO = BuildToggleCanvas();
        }

        // Hide the mini-button while the full map is open or jumping.
        bool showToggle = IsMapOpen == false && IsTraveling == false;
        toggleCanvasGO.SetActive(showToggle);
    }

    private static GameObject BuildToggleCanvas()
    {
        Font font = GetUIFont();

        GameObject canvasGO = new GameObject("GalaxyMapToggleCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        GameObject btnGO = new GameObject("OpenMapButton");
        btnGO.transform.SetParent(canvasGO.transform, false);
        RectTransform btnRT = btnGO.AddComponent<RectTransform>();
        btnRT.anchorMin = new Vector2(1f, 1f);
        btnRT.anchorMax = new Vector2(1f, 1f);
        btnRT.sizeDelta = new Vector2(140f, 40f);
        btnRT.anchoredPosition = new Vector2(-90f, -60f);

        Button btn = btnGO.AddComponent<Button>();
        Image img = btnGO.AddComponent<Image>();
        img.color = new Color(0.1f, 0.2f, 0.45f, 0.9f);

        GameObject labelGO = new GameObject("Text");
        labelGO.transform.SetParent(btnGO.transform, false);
        RectTransform labelRT = labelGO.AddComponent<RectTransform>();
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;
        Text label = labelGO.AddComponent<Text>();
        label.font = font;
        label.fontSize = 16;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.text = "MAP [M]";

        btn.onClick.AddListener(() => ShowMap());

        return canvasGO;
    }

    private static void EnsureEventSystem()
    {
        EventSystem eventSystem = GameObject.FindFirstObjectByType<EventSystem>();

        if (eventSystem == null)
        {
            GameObject esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();
            esGO.AddComponent<InputSystemUIInputModule>();
        }
    }

    private static Font cachedFont;

    private static Font GetUIFont()
    {
        if (cachedFont != null)
        {
            return cachedFont;
        }

        // NOTE: In Unity 6000 "Arial.ttf" is no longer a valid builtin font and
        // Resources.GetBuiltinResource throws ArgumentException. Use LegacyRuntime.ttf directly.
        cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        return cachedFont;
    }

    private static GameObject BuildMapCanvas()
    {
        Font font = GetUIFont();

        GameObject canvasGO = new GameObject("GalaxyMapCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        // Dim background.
        GameObject bgGO = new GameObject("Background");
        bgGO.transform.SetParent(canvasGO.transform, false);
        RectTransform bgRT = bgGO.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;
        Image bgImage = bgGO.AddComponent<Image>();
        bgImage.color = new Color(0f, 0f, 0f, 0.7f);

        // Main panel.
        GameObject panelGO = new GameObject("MapPanel");
        panelGO.transform.SetParent(canvasGO.transform, false);
        RectTransform panelRT = panelGO.AddComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta = new Vector2(1100f, 750f);
        panelRT.anchoredPosition = Vector2.zero;
        Image panelImage = panelGO.AddComponent<Image>();
        panelImage.color = new Color(0.04f, 0.05f, 0.1f, 0.97f);

        // Title.
        GameObject titleGO = new GameObject("Title");
        titleGO.transform.SetParent(panelGO.transform, false);
        RectTransform titleRT = titleGO.AddComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0f, 1f);
        titleRT.anchorMax = new Vector2(1f, 1f);
        titleRT.sizeDelta = new Vector2(0f, 50f);
        titleRT.anchoredPosition = new Vector2(0f, -25f);
        Text titleText = titleGO.AddComponent<Text>();
        titleText.font = font;
        titleText.fontSize = 24;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.color = Color.white;
        titleText.text = "GALAXY MAP - click a system to hyperspace jump  (M to close)";

        // Map area (buttons get positioned in here by galactic X/Y).
        GameObject mapAreaGO = new GameObject("MapArea");
        mapAreaGO.transform.SetParent(panelGO.transform, false);
        RectTransform mapRT = mapAreaGO.AddComponent<RectTransform>();
        mapRT.anchorMin = new Vector2(0f, 0f);
        mapRT.anchorMax = new Vector2(1f, 1f);
        mapRT.offsetMin = new Vector2(20f, 80f);
        mapRT.offsetMax = new Vector2(-20f, -60f);
        Image mapImage = mapAreaGO.AddComponent<Image>();
        mapImage.color = new Color(0.02f, 0.03f, 0.08f, 1f);

        // Footer info.
        GameObject footerGO = new GameObject("Footer");
        footerGO.transform.SetParent(panelGO.transform, false);
        RectTransform footerRT = footerGO.AddComponent<RectTransform>();
        footerRT.anchorMin = new Vector2(0f, 0f);
        footerRT.anchorMax = new Vector2(1f, 0f);
        footerRT.sizeDelta = new Vector2(0f, 60f);
        footerRT.anchoredPosition = new Vector2(0f, 30f);
        Text footerText = footerGO.AddComponent<Text>();
        footerText.font = font;
        footerText.fontSize = 16;
        footerText.alignment = TextAnchor.MiddleCenter;
        footerText.color = new Color(0.7f, 0.8f, 1f);
        footerText.text = "Green = current system. Travel uses hyperspace stretch effect.";

        // Close button.
        GameObject closeGO = new GameObject("CloseButton");
        closeGO.transform.SetParent(panelGO.transform, false);
        RectTransform closeRT = closeGO.AddComponent<RectTransform>();
        closeRT.anchorMin = new Vector2(1f, 1f);
        closeRT.anchorMax = new Vector2(1f, 1f);
        closeRT.sizeDelta = new Vector2(90f, 36f);
        closeRT.anchoredPosition = new Vector2(-55f, -25f);
        Button closeButton = closeGO.AddComponent<Button>();
        Image closeImage = closeGO.GetComponent<Image>();

        if (closeImage == null)
        {
            closeImage = closeGO.AddComponent<Image>();
        }

        closeImage.color = new Color(0.3f, 0.1f, 0.1f, 1f);

        GameObject closeTextGO = new GameObject("Text");
        closeTextGO.transform.SetParent(closeGO.transform, false);
        RectTransform closeTextRT = closeTextGO.AddComponent<RectTransform>();
        closeTextRT.anchorMin = Vector2.zero;
        closeTextRT.anchorMax = Vector2.one;
        closeTextRT.offsetMin = Vector2.zero;
        closeTextRT.offsetMax = Vector2.zero;
        Text closeText = closeTextGO.AddComponent<Text>();
        closeText.font = font;
        closeText.fontSize = 16;
        closeText.alignment = TextAnchor.MiddleCenter;
        closeText.color = Color.white;
        closeText.text = "Close";

        closeButton.onClick.AddListener(() => HideMap());

        canvasGO.SetActive(false);

        return canvasGO;
    }

    private static void RefreshMapButtons()
    {
        if (mapCanvasGO == null)
        {
            return;
        }

        Transform panel = mapCanvasGO.transform.Find("MapPanel");

        if (panel == null)
        {
            return;
        }

        Transform mapArea = panel.Find("MapArea");

        if (mapArea == null)
        {
            return;
        }

        // Clear old system buttons (keep nothing else in MapArea).
        for (int i = mapArea.childCount - 1; i >= 0; i--)
        {
            GameObject.Destroy(mapArea.GetChild(i).gameObject);
        }

        List<StarSystem> systems = GalaxyModeFunctions.GetStartSystems();

        if (systems.Count == 0)
        {
            return;
        }

        Scene scene = SceneFunctions.GetScene();
        string current = scene != null ? scene.currentLocation : "";

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minY = float.MaxValue;
        float maxY = float.MinValue;

        foreach (StarSystem s in systems)
        {
            if (s.X < minX) minX = s.X;
            if (s.X > maxX) maxX = s.X;
            if (s.Y < minY) minY = s.Y;
            if (s.Y > maxY) maxY = s.Y;
        }

        float rangeX = Mathf.Max(1f, maxX - minX);
        float rangeY = Mathf.Max(1f, maxY - minY);

        RectTransform mapRT = mapArea.GetComponent<RectTransform>();
        float areaW = mapRT.rect.width;

        if (areaW <= 0f)
        {
            areaW = 1060f;
        }

        float areaH = mapRT.rect.height;

        if (areaH <= 0f)
        {
            areaH = 610f;
        }

        Font font = GetUIFont();

        foreach (StarSystem s in systems)
        {
            float nx = (s.X - minX) / rangeX;
            float ny = (s.Y - minY) / rangeY;

            GameObject btnGO = new GameObject("Sys_" + s.Planet);
            btnGO.transform.SetParent(mapArea, false);
            RectTransform btnRT = btnGO.AddComponent<RectTransform>();
            btnRT.sizeDelta = new Vector2(150f, 30f);
            btnRT.anchorMin = new Vector2(0f, 1f);
            btnRT.anchorMax = new Vector2(0f, 1f);
            float px = 10f + nx * Mathf.Max(0f, areaW - 170f);
            float py = -(10f + (1f - ny) * Mathf.Max(0f, areaH - 40f));
            btnRT.anchoredPosition = new Vector2(px, py);

            Button btn = btnGO.AddComponent<Button>();
            Image img = btnGO.AddComponent<Image>();

            bool isCurrent = s.Planet == current;
            img.color = isCurrent ? new Color(0.15f, 0.5f, 0.25f, 1f) : new Color(0.12f, 0.2f, 0.45f, 1f);
            btn.interactable = !isCurrent;

            GameObject labelGO = new GameObject("Text");
            labelGO.transform.SetParent(btnGO.transform, false);
            RectTransform labelRT = labelGO.AddComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = Vector2.zero;
            labelRT.offsetMax = Vector2.zero;
            Text label = labelGO.AddComponent<Text>();
            label.font = font;
            label.fontSize = 14;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.text = s.Planet;

            string dest = s.Planet;
            btn.onClick.AddListener(() => TravelToSystem(dest));
        }
    }
}
