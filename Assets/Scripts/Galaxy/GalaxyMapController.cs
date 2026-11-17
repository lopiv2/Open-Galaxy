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
    private static GameObject map3DViewGO;
    private static GalaxyMap3DView map3DView;
    private static RawImage map3DOutput;
    private static string pendingDestination;
    private static Text selectionText;
    private static Button jumpButton;
    private static Text jumpButtonLabel;
    private static List<StarSystem> galaxyMapSystems = new List<StarSystem>();
    private static bool previewMode;
    private static bool previewPreviousCursorVisible;
    private static CursorLockMode previewPreviousCursorLock;

    // Map layers (Layers dialog).
    private static bool showGridLayer = false;
    private static bool showNamesLayer = true;

    private float nextToggleTime;

    void Awake()
    {
        Instance = this;
        IsMapOpen = false;
        IsTraveling = false;
        previewMode = false;
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

        if (map3DViewGO != null)
        {
            GameObject.Destroy(map3DViewGO);
            map3DViewGO = null;
            map3DView = null;
            map3DOutput = null;
        }

        pendingDestination = null;
        selectionText = null;
        jumpButton = null;
        jumpButtonLabel = null;
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
        previewMode = false;
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

        pendingDestination = null;

        if (mapCanvasGO == null)
        {
            mapCanvasGO = BuildMapCanvas();
        }

        // Measure the real rect after activation so dots and grid fit the window.
        mapCanvasGO.SetActive(true);
        Canvas.ForceUpdateCanvases();

        if (map3DView != null)
        {
            map3DView.SetVisible(true);
        }

        RefreshMapButtons();
        RefreshLayers();
        RefreshLayersDialog();
        RefreshSelectionBar();

        IsMapOpen = true;

        Time.timeScale = 0;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    // Opens the map over the current Play Mode scene for visual testing.
    // Preview selection is enabled, but hyperspace travel is intentionally disabled.
    public static void ShowPreview()
    {
        if (IsMapOpen)
        {
            HideMap();
        }

        previewPreviousCursorVisible = Cursor.visible;
        previewPreviousCursorLock = Cursor.lockState;
        previewMode = true;
        pendingDestination = null;

        EnsureEventSystem();

        if (mapCanvasGO == null)
        {
            mapCanvasGO = BuildMapCanvas();
        }

        mapCanvasGO.SetActive(true);
        Canvas.ForceUpdateCanvases();

        if (map3DView != null)
        {
            map3DView.SetVisible(true);
        }

        RefreshMapButtons();
        RefreshLayers();
        RefreshLayersDialog();
        RefreshSelectionBar();

        IsMapOpen = true;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    public static void HideMap()
    {
        bool closingPreview = previewMode;

        if (mapCanvasGO != null)
        {
            Transform panel = mapCanvasGO.transform.Find("MapPanel");

            if (panel != null)
            {
                Transform dialog = panel.Find("LayersDialog");

                if (dialog != null)
                {
                    dialog.gameObject.SetActive(false);
                }
            }

            mapCanvasGO.SetActive(false);
        }

        if (map3DView != null)
        {
            map3DView.SetVisible(false);
        }

        IsMapOpen = false;
        pendingDestination = null;
        previewMode = false;

        if (closingPreview)
        {
            Cursor.visible = previewPreviousCursorVisible;
            Cursor.lockState = previewPreviousCursorLock;
            return;
        }

        // Only restore flight state if we are still in a galaxy mission.
        MissionManager missionManager = GameObject.FindFirstObjectByType<MissionManager>();

        if (missionManager != null && missionManager.missionName != null && missionManager.missionName.StartsWith("Galaxy Mode"))
        {
            Time.timeScale = 1;
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Confined;
        }
    }

    // Selecting a system only marks it as the pending destination.
    public static void SelectDestination(string planetName)
    {
        if (IsTraveling || string.IsNullOrEmpty(planetName))
        {
            return;
        }

        Scene scene = SceneFunctions.GetScene();

        if (!previewMode && (scene == null || scene.currentLocation == planetName))
        {
            return;
        }

        pendingDestination = planetName;

        if (map3DView != null)
        {
            map3DView.SetSelection(scene != null ? scene.currentLocation : "", pendingDestination);
        }
        else
        {
            RefreshMapButtons();
        }

        RefreshSelectionBar();
    }

    // The Cancel button closes the map without starting a hyperspace jump.
    public static void CancelSelection()
    {
        HideMap();
    }

    public static void TravelToSystem(string planetName)
    {
        if (previewMode || IsTraveling == true || string.IsNullOrEmpty(planetName))
        {
            return;
        }

        if (planetName != pendingDestination)
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

        pendingDestination = null;
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

        // Main panel: stretch to the window so header and buttons are never cut off.
        GameObject panelGO = new GameObject("MapPanel");
        panelGO.transform.SetParent(canvasGO.transform, false);
        RectTransform panelRT = panelGO.AddComponent<RectTransform>();
        panelRT.anchorMin = Vector2.zero;
        panelRT.anchorMax = Vector2.one;
        panelRT.offsetMin = Vector2.zero;
        panelRT.offsetMax = Vector2.zero;
        panelRT.anchoredPosition = Vector2.zero;
        Image panelImage = panelGO.AddComponent<Image>();
        panelImage.color = new Color(0.01f, 0.025f, 0.055f, 0.18f);
        panelImage.raycastTarget = false;

        // Compact navigation title, styled as a small in-game HUD.
        GameObject titleGO = new GameObject("Title");
        titleGO.transform.SetParent(panelGO.transform, false);
        RectTransform titleRT = titleGO.AddComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0f, 1f);
        titleRT.anchorMax = new Vector2(0f, 1f);
        titleRT.sizeDelta = new Vector2(480f, 44f);
        titleRT.anchoredPosition = new Vector2(365f, -30f);
        Text titleText = titleGO.AddComponent<Text>();
        titleText.font = font;
        titleText.fontSize = 19;
        titleText.fontStyle = FontStyle.Bold;
        titleText.alignment = TextAnchor.MiddleLeft;
        titleText.color = new Color(0.72f, 0.88f, 1f, 1f);
        titleText.text = "GALACTIC ATLAS  /  SYSTEM NAVIGATION";

        // The 3D starfield fills the screen; controls float above it.
        GameObject mapAreaGO = new GameObject("MapArea");
        mapAreaGO.transform.SetParent(panelGO.transform, false);
        RectTransform mapRT = mapAreaGO.AddComponent<RectTransform>();
        mapRT.anchorMin = Vector2.zero;
        mapRT.anchorMax = Vector2.one;
        mapRT.offsetMin = Vector2.zero;
        mapRT.offsetMax = Vector2.zero;
        Image mapImage = mapAreaGO.AddComponent<Image>();
        mapImage.color = new Color(0.012f, 0.035f, 0.075f, 1f);
        mapImage.preserveAspect = false;
        mapAreaGO.AddComponent<Mask>();

        GameObject map3DOutputGO = new GameObject("GalaxyMap3DOutput");
        map3DOutputGO.transform.SetParent(mapAreaGO.transform, false);
        RectTransform map3DOutputRT = map3DOutputGO.AddComponent<RectTransform>();
        map3DOutputRT.anchorMin = Vector2.zero;
        map3DOutputRT.anchorMax = Vector2.one;
        map3DOutputRT.offsetMin = Vector2.zero;
        map3DOutputRT.offsetMax = Vector2.zero;
        map3DOutput = map3DOutputGO.AddComponent<RawImage>();
        map3DOutput.color = Color.white;
        map3DOutput.raycastTarget = false;

        map3DViewGO = new GameObject("GalaxyMap3DView");
        map3DView = map3DViewGO.AddComponent<GalaxyMap3DView>();
        map3DView.Initialize(mapRT, map3DOutput, font);
        titleGO.transform.SetAsLastSibling();

        // Minimal control hints stay out of the way of the starfield.
        GameObject footerGO = new GameObject("Footer");
        footerGO.transform.SetParent(panelGO.transform, false);
        RectTransform footerRT = footerGO.AddComponent<RectTransform>();
        footerRT.anchorMin = new Vector2(0f, 0f);
        footerRT.anchorMax = new Vector2(0f, 0f);
        footerRT.sizeDelta = new Vector2(620f, 44f);
        footerRT.anchoredPosition = new Vector2(330f, 25f);
        Text footerText = footerGO.AddComponent<Text>();
        footerText.font = font;
        footerText.fontSize = 12;
        footerText.alignment = TextAnchor.MiddleLeft;
        footerText.color = new Color(0.7f, 0.82f, 1f, 0.85f);
        footerText.text = "LEFT CLICK: SELECT   •   RIGHT DRAG / ARROWS: PAN   •   MIDDLE DRAG: ORBIT   •   SCROLL: ZOOM   •   M: CLOSE";

        // Destination confirmation bar. Selecting a planet never starts travel
        // until the player presses JUMP; Cancel returns to flight without jumping.
        GameObject selectionBarGO = new GameObject("SelectionBar");
        selectionBarGO.transform.SetParent(panelGO.transform, false);
        RectTransform selectionBarRT = selectionBarGO.AddComponent<RectTransform>();
        selectionBarRT.anchorMin = new Vector2(1f, 0.5f);
        selectionBarRT.anchorMax = new Vector2(1f, 0.5f);
        selectionBarRT.sizeDelta = new Vector2(360f, 248f);
        selectionBarRT.anchoredPosition = new Vector2(-190f, 0f);
        Image selectionBarImage = selectionBarGO.AddComponent<Image>();
        selectionBarImage.color = new Color(0.015f, 0.07f, 0.13f, 0.92f);
        Outline selectionOutline = selectionBarGO.AddComponent<Outline>();
        selectionOutline.effectColor = new Color(0.12f, 0.62f, 1f, 0.95f);
        selectionOutline.effectDistance = new Vector2(2f, 2f);

        GameObject selectionTitleGO = new GameObject("SelectionTitle");
        selectionTitleGO.transform.SetParent(selectionBarGO.transform, false);
        RectTransform selectionTitleRT = selectionTitleGO.AddComponent<RectTransform>();
        selectionTitleRT.anchorMin = new Vector2(0f, 1f);
        selectionTitleRT.anchorMax = new Vector2(1f, 1f);
        selectionTitleRT.sizeDelta = new Vector2(0f, 46f);
        selectionTitleRT.anchoredPosition = new Vector2(0f, -23f);
        Text selectionTitle = selectionTitleGO.AddComponent<Text>();
        selectionTitle.font = font;
        selectionTitle.fontSize = 18;
        selectionTitle.fontStyle = FontStyle.Bold;
        selectionTitle.alignment = TextAnchor.MiddleLeft;
        selectionTitle.color = new Color(0.8f, 0.93f, 1f, 1f);
        selectionTitle.text = "   SYSTEM INTEL";

        GameObject selectionAccentGO = new GameObject("SelectionAccent");
        selectionAccentGO.transform.SetParent(selectionBarGO.transform, false);
        RectTransform selectionAccentRT = selectionAccentGO.AddComponent<RectTransform>();
        selectionAccentRT.anchorMin = new Vector2(0f, 1f);
        selectionAccentRT.anchorMax = new Vector2(1f, 1f);
        selectionAccentRT.sizeDelta = new Vector2(0f, 2f);
        selectionAccentRT.anchoredPosition = new Vector2(0f, -47f);
        Image selectionAccent = selectionAccentGO.AddComponent<Image>();
        selectionAccent.color = new Color(0.1f, 0.55f, 0.9f, 0.9f);

        GameObject selectionTextGO = new GameObject("SelectedDestination");
        selectionTextGO.transform.SetParent(selectionBarGO.transform, false);
        RectTransform selectionTextRT = selectionTextGO.AddComponent<RectTransform>();
        selectionTextRT.anchorMin = new Vector2(0f, 0f);
        selectionTextRT.anchorMax = new Vector2(1f, 1f);
        selectionTextRT.offsetMin = new Vector2(18f, 62f);
        selectionTextRT.offsetMax = new Vector2(-18f, -56f);
        selectionText = selectionTextGO.AddComponent<Text>();
        selectionText.font = font;
        selectionText.fontSize = 14;
        selectionText.alignment = TextAnchor.UpperLeft;
        selectionText.color = new Color(0.82f, 0.89f, 0.98f, 1f);
        selectionText.lineSpacing = 1.1f;
        selectionText.text = "Select a destination system...";

        GameObject jumpGO = new GameObject("JumpButton");
        jumpGO.transform.SetParent(selectionBarGO.transform, false);
        RectTransform jumpRT = jumpGO.AddComponent<RectTransform>();
        jumpRT.sizeDelta = new Vector2(150f, 38f);
        jumpRT.anchorMin = new Vector2(1f, 0f);
        jumpRT.anchorMax = new Vector2(1f, 0f);
        jumpRT.anchoredPosition = new Vector2(-98f, 28f);
        jumpButton = jumpGO.AddComponent<Button>();
        Image jumpImage = jumpGO.AddComponent<Image>();
        jumpImage.color = new Color(0.12f, 0.45f, 0.2f, 1f);
        jumpButtonLabel = SetButtonLabel(jumpGO, "JUMP", font);
        jumpButton.interactable = false;
        jumpButton.onClick.AddListener(() =>
        {
            if (!string.IsNullOrEmpty(pendingDestination))
            {
                TravelToSystem(pendingDestination);
            }
        });

        GameObject cancelGO = new GameObject("CancelButton");
        cancelGO.transform.SetParent(selectionBarGO.transform, false);
        RectTransform cancelRT = cancelGO.AddComponent<RectTransform>();
        cancelRT.sizeDelta = new Vector2(90f, 38f);
        cancelRT.anchorMin = new Vector2(0f, 0f);
        cancelRT.anchorMax = new Vector2(0f, 0f);
        cancelRT.anchoredPosition = new Vector2(65f, 28f);
        Button cancelButton = cancelGO.AddComponent<Button>();
        Image cancelImage = cancelGO.AddComponent<Image>();
        cancelImage.color = new Color(0.45f, 0.15f, 0.12f, 1f);
        SetButtonLabel(cancelGO, "CANCEL", font);
        cancelButton.onClick.AddListener(() => CancelSelection());

        RefreshSelectionBar();

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

        closeButton.onClick.AddListener(() => CancelSelection());

        // Layers button (top-left) and layers dialog.
        GameObject layersGO = new GameObject("LayersButton");
        layersGO.transform.SetParent(panelGO.transform, false);
        RectTransform layersRT = layersGO.AddComponent<RectTransform>();
        layersRT.anchorMin = new Vector2(0f, 1f);
        layersRT.anchorMax = new Vector2(0f, 1f);
        layersRT.sizeDelta = new Vector2(110f, 36f);
        layersRT.anchoredPosition = new Vector2(75f, -82f);
        Button layersButton = layersGO.AddComponent<Button>();
        Image layersImage = layersGO.AddComponent<Image>();
        layersImage.color = new Color(0.12f, 0.2f, 0.45f, 1f);
        GameObject layersTextGO = new GameObject("Text");
        layersTextGO.transform.SetParent(layersGO.transform, false);
        RectTransform layersTextRT = layersTextGO.AddComponent<RectTransform>();
        layersTextRT.anchorMin = Vector2.zero;
        layersTextRT.anchorMax = Vector2.one;
        layersTextRT.offsetMin = Vector2.zero;
        layersTextRT.offsetMax = Vector2.zero;
        Text layersText = layersTextGO.AddComponent<Text>();
        layersText.font = font;
        layersText.fontSize = 14;
        layersText.alignment = TextAnchor.MiddleCenter;
        layersText.color = Color.white;
        layersText.text = "Layers";
        layersButton.onClick.AddListener(() => ToggleLayersDialog());

        GameObject dialogGO = new GameObject("LayersDialog");
        dialogGO.transform.SetParent(panelGO.transform, false);
        RectTransform dialogRT = dialogGO.AddComponent<RectTransform>();
        dialogRT.anchorMin = new Vector2(0.5f, 0.5f);
        dialogRT.anchorMax = new Vector2(0.5f, 0.5f);
        dialogRT.sizeDelta = new Vector2(380f, 260f);
        dialogRT.anchoredPosition = Vector2.zero;
        Image dialogImage = dialogGO.AddComponent<Image>();
        dialogImage.color = new Color(0.03f, 0.04f, 0.09f, 0.98f);

        GameObject dialogTitleGO = new GameObject("Title");
        dialogTitleGO.transform.SetParent(dialogGO.transform, false);
        RectTransform dialogTitleRT = dialogTitleGO.AddComponent<RectTransform>();
        dialogTitleRT.anchorMin = new Vector2(0f, 1f);
        dialogTitleRT.anchorMax = new Vector2(1f, 1f);
        dialogTitleRT.sizeDelta = new Vector2(0f, 40f);
        dialogTitleRT.anchoredPosition = new Vector2(0f, -20f);
        Text dialogTitle = dialogTitleGO.AddComponent<Text>();
        dialogTitle.font = font;
        dialogTitle.fontSize = 18;
        dialogTitle.alignment = TextAnchor.MiddleCenter;
        dialogTitle.color = Color.white;
        dialogTitle.text = "Map Layers";

        BuildLayerRow(dialogGO, font, "Row_Grid", 0, "Grid C-U / 1-21: ON", () => SetGridLayer(!showGridLayer));
        BuildLayerRow(dialogGO, font, "Row_Names", 1, "System names: ON", () => SetNamesLayer(!showNamesLayer));

        GameObject dialogCloseGO = new GameObject("CloseButton");
        dialogCloseGO.transform.SetParent(dialogGO.transform, false);
        RectTransform dialogCloseRT = dialogCloseGO.AddComponent<RectTransform>();
        dialogCloseRT.anchorMin = new Vector2(0.5f, 0f);
        dialogCloseRT.anchorMax = new Vector2(0.5f, 0f);
        dialogCloseRT.sizeDelta = new Vector2(120f, 32f);
        dialogCloseRT.anchoredPosition = new Vector2(0f, 30f);
        Button dialogCloseButton = dialogCloseGO.AddComponent<Button>();
        Image dialogCloseImage = dialogCloseGO.AddComponent<Image>();
        dialogCloseImage.color = new Color(0.3f, 0.1f, 0.1f, 1f);
        GameObject dialogCloseTextGO = new GameObject("Text");
        dialogCloseTextGO.transform.SetParent(dialogCloseGO.transform, false);
        RectTransform dialogCloseTextRT = dialogCloseTextGO.AddComponent<RectTransform>();
        dialogCloseTextRT.anchorMin = Vector2.zero;
        dialogCloseTextRT.anchorMax = Vector2.one;
        dialogCloseTextRT.offsetMin = Vector2.zero;
        dialogCloseTextRT.offsetMax = Vector2.zero;
        Text dialogCloseText = dialogCloseTextGO.AddComponent<Text>();
        dialogCloseText.font = font;
        dialogCloseText.fontSize = 14;
        dialogCloseText.alignment = TextAnchor.MiddleCenter;
        dialogCloseText.color = Color.white;
        dialogCloseText.text = "Close";
        dialogCloseButton.onClick.AddListener(() => ToggleLayersDialog());

        dialogGO.SetActive(false);

        canvasGO.SetActive(false);

        return canvasGO;
    }

    private static void BuildLayerRow(GameObject dialogGO, Font font, string rowName, int rowIndex, string labelText, UnityEngine.Events.UnityAction onToggle)
    {
        GameObject rowGO = new GameObject(rowName);
        rowGO.transform.SetParent(dialogGO.transform, false);
        RectTransform rowRT = rowGO.AddComponent<RectTransform>();
        rowRT.anchorMin = new Vector2(0f, 1f);
        rowRT.anchorMax = new Vector2(1f, 1f);
        rowRT.offsetMin = new Vector2(20f, -100f - rowIndex * 44f);
        rowRT.offsetMax = new Vector2(-20f, -64f - rowIndex * 44f);
        Button rowButton = rowGO.AddComponent<Button>();
        Image rowImage = rowGO.AddComponent<Image>();
        rowImage.color = new Color(0.1f, 0.16f, 0.28f, 0.6f);

        GameObject statusGO = new GameObject("Status");
        statusGO.transform.SetParent(rowGO.transform, false);
        RectTransform statusRT = statusGO.AddComponent<RectTransform>();
        statusRT.anchorMin = new Vector2(0f, 0.5f);
        statusRT.anchorMax = new Vector2(0f, 0.5f);
        statusRT.sizeDelta = new Vector2(16f, 16f);
        statusRT.anchoredPosition = new Vector2(18f, 0f);
        Image statusImage = statusGO.AddComponent<Image>();
        statusImage.color = new Color(0.15f, 0.5f, 0.25f, 1f);
        statusImage.raycastTarget = false;

        GameObject nameGO = new GameObject("Name");
        nameGO.transform.SetParent(rowGO.transform, false);
        RectTransform nameRT = nameGO.AddComponent<RectTransform>();
        nameRT.anchorMin = Vector2.zero;
        nameRT.anchorMax = Vector2.one;
        nameRT.offsetMin = new Vector2(44f, 0f);
        nameRT.offsetMax = Vector2.zero;
        Text nameText = nameGO.AddComponent<Text>();
        nameText.font = font;
        nameText.fontSize = 14;
        nameText.alignment = TextAnchor.MiddleLeft;
        nameText.color = Color.white;
        nameText.text = labelText;
        nameText.raycastTarget = false;

        rowButton.onClick.AddListener(onToggle);
    }

    private static Text SetButtonLabel(GameObject buttonGO, string labelText, Font font)
    {
        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(buttonGO.transform, false);
        RectTransform textRT = textGO.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        Text label = textGO.AddComponent<Text>();
        label.font = font;
        label.fontSize = 16;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.text = labelText;
        return label;
    }

    private static void RefreshSelectionBar()
    {
        if (selectionText == null || jumpButton == null)
        {
            return;
        }

        Scene scene = SceneFunctions.GetScene();

        if (previewMode)
        {
            if (string.IsNullOrEmpty(pendingDestination))
            {
                selectionText.text = "PREVIEW MODE\n\nClick a system marker to inspect its data.";
            }
            else
            {
                StarSystem previewDestination = FindMapSystem(pendingDestination);
                selectionText.text = previewDestination != null
                    ? FormatSystemDetails("PREVIEW SYSTEM", previewDestination)
                    : "PREVIEW SYSTEM\n" + pendingDestination;
            }

            jumpButton.interactable = false;
            if (jumpButtonLabel != null) jumpButtonLabel.text = "PREVIEW";
            return;
        }

        if (jumpButtonLabel != null) jumpButtonLabel.text = "JUMP";

        if (string.IsNullOrEmpty(pendingDestination))
        {
            string current = scene != null ? scene.currentLocation : "";
            StarSystem currentData = FindMapSystem(current);
            selectionText.text = currentData != null
                ? FormatSystemDetails("CURRENT SYSTEM", currentData) + "\n\nSelect a star to chart a destination."
                : "No system selected.\n\nSelect a star marker to inspect its data and plan a hyperspace jump.";
            jumpButton.interactable = false;
        }
        else
        {
            StarSystem destination = FindMapSystem(pendingDestination);
            selectionText.text = destination != null
                ? FormatSystemDetails("DESTINATION", destination)
                : "DESTINATION\n" + pendingDestination;
            jumpButton.interactable = true;
        }
    }

    private static StarSystem FindMapSystem(string planetName)
    {
        if (string.IsNullOrEmpty(planetName) || galaxyMapSystems == null)
        {
            return null;
        }

        return galaxyMapSystems.Find(system => system != null && system.Planet == planetName);
    }

    private static string FormatSystemDetails(string heading, StarSystem system)
    {
        List<string> details = new List<string>();
        details.Add(heading);
        details.Add(system.Planet);

        if (!string.IsNullOrEmpty(system.Region)) details.Add("Region: " + system.Region);
        if (!string.IsNullOrEmpty(system.Sector)) details.Add("Sector: " + system.Sector);
        if (!string.IsNullOrEmpty(system.Grid)) details.Add("Grid: " + system.Grid);
        if (!string.IsNullOrEmpty(system.faction)) details.Add("Faction: " + system.faction);

        return string.Join("\n", details);
    }

    public static void ToggleLayersDialog()
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

        Transform dialog = panel.Find("LayersDialog");

        if (dialog != null)
        {
            dialog.gameObject.SetActive(!dialog.gameObject.activeSelf);
        }
    }

    public static void SetGridLayer(bool visible)
    {
        showGridLayer = visible;
        RefreshLayers();
        RefreshLayersDialog();
    }

    public static void SetNamesLayer(bool visible)
    {
        showNamesLayer = visible;

        if (map3DView != null)
        {
            map3DView.SetNamesVisible(showNamesLayer);
        }
        else
        {
            RefreshMapButtons();
        }

        RefreshLayersDialog();
    }

    // Official galactic grid: columns C-U left to right, rows 1-21 top to bottom.
    // Cells are 1500x1500 galactic units: letter = floor((X + 13500) / 1500) from 'C',
    // row = ceil((14400 - Y) / 1500). Verified: Tatooine (9665, -10099) -> R16.
    private static void RefreshLayers()
    {
        if (map3DView != null)
        {
            map3DView.SetGridVisible(showGridLayer);
            return;
        }

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

        if (mapArea != null)
        {
            Transform grid = mapArea.Find("GridLayer");

            if (grid != null)
            {
                grid.gameObject.SetActive(showGridLayer);
            }
        }
    }

    private static void RefreshLayersDialog()
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

        Transform dialog = panel.Find("LayersDialog");

        if (dialog == null)
        {
            return;
        }

        Transform gridRow = dialog.Find("Row_Grid");
        Transform namesRow = dialog.Find("Row_Names");

        if (gridRow != null)
        {
            Transform status = gridRow.Find("Status");
            Transform label = gridRow.Find("Name");

            if (status != null)
            {
                status.GetComponent<Image>().color = showGridLayer ? new Color(0.15f, 0.5f, 0.25f, 1f) : new Color(0.35f, 0.35f, 0.35f, 1f);
            }

            if (label != null)
            {
                label.GetComponent<Text>().text = "Grid C-U / 1-21: " + (showGridLayer ? "ON" : "OFF");
            }
        }

        if (namesRow != null)
        {
            Transform status = namesRow.Find("Status");
            Transform label = namesRow.Find("Name");

            if (status != null)
            {
                status.GetComponent<Image>().color = showNamesLayer ? new Color(0.15f, 0.5f, 0.25f, 1f) : new Color(0.35f, 0.35f, 0.35f, 1f);
            }

            if (label != null)
            {
                label.GetComponent<Text>().text = "System names: " + (showNamesLayer ? "ON" : "OFF");
            }
        }
    }

    private static void EnsureGridLayer(float minX, float maxX, float minY, float maxY, float rangeX, float rangeY, float areaW, float areaH)
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

        Transform existing = mapArea.Find("GridLayer");

        if (existing != null)
        {
            GameObject.Destroy(existing.gameObject);
        }

        Font font = GetUIFont();

        float contentW = Mathf.Max(0f, areaW - MapPadLeft - MapPadRight);
        float contentH = Mathf.Max(0f, areaH - MapPadTop - MapPadBottom);

        GameObject gridGO = new GameObject("GridLayer");
        gridGO.transform.SetParent(mapArea, false);
        RectTransform gridRT = gridGO.AddComponent<RectTransform>();
        gridRT.anchorMin = Vector2.zero;
        gridRT.anchorMax = Vector2.one;
        gridRT.offsetMin = Vector2.zero;
        gridRT.offsetMax = Vector2.zero;

        Color lineColor = new Color(1f, 1f, 1f, 0.18f);
        Color labelColor = new Color(1f, 1f, 1f, 0.6f);

        // Vertical boundaries: X = -13500 + 1500 * k, k = 0..19 (columns C..U).
        for (int k = 0; k <= 19; k++)
        {
            float xb = -13500f + 1500f * k;
            float nx = (xb - minX) / rangeX;
            float px = MapPadLeft + nx * contentW;

            GameObject lineGO = new GameObject("GridV" + k);
            lineGO.transform.SetParent(gridGO.transform, false);
            RectTransform lineRT = lineGO.AddComponent<RectTransform>();
            lineRT.anchorMin = new Vector2(0f, 1f);
            lineRT.anchorMax = new Vector2(0f, 1f);
            lineRT.pivot = new Vector2(0.5f, 1f);
            lineRT.sizeDelta = new Vector2(2f, contentH);
            lineRT.anchoredPosition = new Vector2(px, -MapPadTop);
            Image lineImage = lineGO.AddComponent<Image>();
            lineImage.color = lineColor;
            lineImage.raycastTarget = false;
        }

        // Horizontal boundaries: Y = 14400 - 1500 * r, r = 0..21 (rows 1..21 below each line).
        for (int r = 0; r <= 21; r++)
        {
            float yb = 14400f - 1500f * r;
            float ny = (yb - minY) / rangeY;
            float py = -(MapPadTop + (1f - ny) * contentH);

            GameObject lineGO = new GameObject("GridH" + r);
            lineGO.transform.SetParent(gridGO.transform, false);
            RectTransform lineRT = lineGO.AddComponent<RectTransform>();
            lineRT.anchorMin = new Vector2(0f, 1f);
            lineRT.anchorMax = new Vector2(0f, 1f);
            lineRT.pivot = new Vector2(0f, 1f);
            lineRT.sizeDelta = new Vector2(contentW, 2f);
            lineRT.anchoredPosition = new Vector2(MapPadLeft, py);
            Image lineImage = lineGO.AddComponent<Image>();
            lineImage.color = lineColor;
            lineImage.raycastTarget = false;
        }

        // Column letters C..U centered over each column.
        for (int c = 0; c < 19; c++)
        {
            float xc = -13500f + 1500f * (c + 0.5f);
            float nx = (xc - minX) / rangeX;
            float px = MapPadLeft + nx * contentW;

            GameObject labelGO = new GameObject("GridCol" + c);
            labelGO.transform.SetParent(gridGO.transform, false);
            RectTransform labelRT = labelGO.AddComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0f, 1f);
            labelRT.anchorMax = new Vector2(0f, 1f);
            labelRT.sizeDelta = new Vector2(30f, 20f);
            labelRT.anchoredPosition = new Vector2(px, -MapPadTop / 2f);
            Text label = labelGO.AddComponent<Text>();
            label.font = font;
            label.fontSize = 10;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = labelColor;
            label.text = "" + (char)('C' + c);
        }

        // Row numbers 1..21 centered on each row.
        for (int r = 1; r <= 21; r++)
        {
            float yc = 14400f - 1500f * (r - 0.5f);
            float ny = (yc - minY) / rangeY;
            float py = -(MapPadTop + (1f - ny) * contentH);

            GameObject labelGO = new GameObject("GridRow" + r);
            labelGO.transform.SetParent(gridGO.transform, false);
            RectTransform labelRT = labelGO.AddComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0f, 1f);
            labelRT.anchorMax = new Vector2(0f, 1f);
            labelRT.sizeDelta = new Vector2(40f, 20f);
            labelRT.anchoredPosition = new Vector2(MapPadLeft / 2f, py);
            Text label = labelGO.AddComponent<Text>();
            label.font = font;
            label.fontSize = 10;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = labelColor;
            label.text = r.ToString();
        }

        gridGO.SetActive(showGridLayer);
    }

    // Padding inside the map area so dots and labels never touch the edges.
    private const float MapPadLeft = 70f;
    private const float MapPadRight = 70f;
    private const float MapPadTop = 34f;
    private const float MapPadBottom = 30f;

    private static void MapToArea(float nx, float ny, float areaW, float areaH, out float px, out float py)
    {
        float cw = Mathf.Max(0f, areaW - MapPadLeft - MapPadRight);
        float ch = Mathf.Max(0f, areaH - MapPadTop - MapPadBottom);
        px = MapPadLeft + nx * cw;
        py = -(MapPadTop + (1f - ny) * ch);
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

        galaxyMapSystems = GalaxyModeFunctions.GetStartSystems();

        if (map3DView != null)
        {
            Scene currentScene = SceneFunctions.GetScene();
            string currentSystem = currentScene != null ? currentScene.currentLocation : "";
            map3DView.SetSystems(galaxyMapSystems, currentSystem, pendingDestination, SelectDestination);
            map3DView.SetGridVisible(showGridLayer);
            map3DView.SetNamesVisible(showNamesLayer);
            return;
        }

        // Clear old map content (grid is rebuilt below with the current rect).
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
        Sprite dotSprite = GetOrCreateDotSprite();
        Sprite ringSprite = GetOrCreateRingSprite();
        List<Rect> placedLabels = new List<Rect>();

        foreach (StarSystem s in systems)
        {
            float nx = (s.X - minX) / rangeX;
            float ny = (s.Y - minY) / rangeY;

            GameObject btnGO = new GameObject("Sys_" + s.Planet);
            btnGO.transform.SetParent(mapArea, false);
            RectTransform btnRT = btnGO.AddComponent<RectTransform>();
            btnRT.sizeDelta = new Vector2(72f, 52f);
            btnRT.anchorMin = new Vector2(0f, 1f);
            btnRT.anchorMax = new Vector2(0f, 1f);
            float px;
            float py;
            MapToArea(nx, ny, areaW, areaH, out px, out py);
            btnRT.anchoredPosition = new Vector2(px, py);

            Button btn = btnGO.AddComponent<Button>();

            // Transparent hit area: hover and click work over dot and text alike.
            Image hitImage = btnGO.AddComponent<Image>();
            hitImage.color = new Color(1f, 1f, 1f, 0f);

            // Planet dot (generic sphere marker).
            GameObject dotGO = new GameObject("Dot");
            dotGO.transform.SetParent(btnGO.transform, false);
            RectTransform dotRT = dotGO.AddComponent<RectTransform>();
            dotRT.anchorMin = new Vector2(0.5f, 1f);
            dotRT.anchorMax = new Vector2(0.5f, 1f);
            dotRT.sizeDelta = new Vector2(24f, 24f);
            dotRT.anchoredPosition = new Vector2(0f, -14f);
            Image dotImage = dotGO.AddComponent<Image>();
            dotImage.sprite = dotSprite;
            dotImage.raycastTarget = false;

            // Diffuse hover/selection ring around the dot.
            GameObject ringGO = new GameObject("Ring");
            ringGO.transform.SetParent(btnGO.transform, false);
            RectTransform ringRT = ringGO.AddComponent<RectTransform>();
            ringRT.anchorMin = new Vector2(0.5f, 1f);
            ringRT.anchorMax = new Vector2(0.5f, 1f);
            ringRT.sizeDelta = new Vector2(40f, 40f);
            ringRT.anchoredPosition = new Vector2(0f, -14f);
            Image ringImage = ringGO.AddComponent<Image>();
            ringImage.sprite = ringSprite;
            ringImage.color = new Color(1f, 0.85f, 0.4f, 0.95f);
            ringImage.raycastTarget = false;

            bool isCurrent = s.Planet == current;
            bool isSelected = s.Planet == pendingDestination;

            if (isCurrent)
            {
                dotImage.color = new Color(0.3f, 1f, 0.5f, 1f);
            }
            else if (isSelected)
            {
                dotImage.color = new Color(1f, 0.75f, 0.25f, 1f);
            }
            else
            {
                dotImage.color = new Color(0.55f, 0.75f, 1f, 1f);
            }

            btn.interactable = !isCurrent;
            ringGO.SetActive(isSelected);

            // Small label under the dot. Overlapping names are pushed down so
            // texts of close systems never coincide (dots keep exact positions).
            float labelShift = 0f;
            Rect labelRect = new Rect(px - 20f, -py + 38f, 112f, 14f);
            int guard = 0;

            while (guard < 4 && placedLabels.Exists(r => r.Overlaps(labelRect)) && labelRect.yMax + 14f <= areaH - 2f)
            {
                labelShift -= 14f;
                labelRect.y += 14f;
                guard++;
            }

            placedLabels.Add(labelRect);

            GameObject labelGO = new GameObject("Text");
            labelGO.transform.SetParent(btnGO.transform, false);
            RectTransform labelRT = labelGO.AddComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0f, 0f);
            labelRT.anchorMax = new Vector2(1f, 0f);
            labelRT.offsetMin = new Vector2(-20f, labelShift);
            labelRT.offsetMax = new Vector2(20f, 14f + labelShift);
            Text label = labelGO.AddComponent<Text>();
            label.font = font;
            label.fontSize = 12;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(0.85f, 0.9f, 1f, 1f);
            label.text = s.Planet;
            labelGO.SetActive(showNamesLayer);

            // Hover shows the diffuse ring; it stays on for the selection.
            EventTrigger trigger = btnGO.AddComponent<EventTrigger>();

            EventTrigger.Entry enter = new EventTrigger.Entry();
            enter.eventID = EventTriggerType.PointerEnter;
            enter.callback.AddListener((data) => { ringGO.SetActive(true); });

            EventTrigger.Entry exit = new EventTrigger.Entry();
            exit.eventID = EventTriggerType.PointerExit;
            string selectedName = s.Planet;
            exit.callback.AddListener((data) => { ringGO.SetActive(selectedName == pendingDestination); });

            trigger.triggers.Add(enter);
            trigger.triggers.Add(exit);

            string dest = s.Planet;
            btn.onClick.AddListener(() => SelectDestination(dest));
        }

        EnsureGridLayer(minX, maxX, minY, maxY, rangeX, rangeY, areaW, areaH);
    }

    private static Sprite cachedDotSprite;
    private static Sprite cachedRingSprite;

    private static Sprite GetOrCreateDotSprite()
    {
        if (cachedDotSprite != null)
        {
            return cachedDotSprite;
        }

        int size = 128;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;

        Vector2 center = new Vector2((size - 1) / 2f, (size - 1) / 2f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center) / (size / 2f);
                float alpha = Mathf.Clamp01(1f - dist / 0.52f);
                alpha *= alpha;
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();

        cachedDotSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        cachedDotSprite.name = "GalaxyMapDot";

        return cachedDotSprite;
    }

    private static Sprite GetOrCreateRingSprite()
    {
        if (cachedRingSprite != null)
        {
            return cachedRingSprite;
        }

        int size = 128;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;

        Vector2 center = new Vector2((size - 1) / 2f, (size - 1) / 2f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center) / (size / 2f);
                float alpha = Mathf.Clamp01(1f - Mathf.Abs(dist - 0.78f) / 0.16f);
                alpha *= alpha;
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();

        cachedRingSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        cachedRingSprite.name = "GalaxyMapRing";

        return cachedRingSprite;
    }
}
