using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// On-foot compass bar (Horizon-style) shown only while walking.
// Star Wars look: dark strip, cyan hairline, amber ticks, red selection ring.
// - Hides the ship "Radar" CanvasGroup while on foot, restores it on board.
// - Q/E (pad: dpad left/right) cycles PlanetNavigation POIs (H still works).
// - Shows N/E/S/W tape, POI diamonds, ship caret and selected POI readout.
public class GroundCompass : MonoBehaviour
{
    public static GroundCompass Instance { get; private set; }

    private const float BarWidth = 620f;
    private const float BarHeight = 46f;
    private const float VisibleHalfAngle = 60f;
    // How far the mission log moves down while the compass covers the top.
    private const float LogShiftY = 64f;

    private static readonly Color BgColor = new Color(0.015f, 0.025f, 0.05f, 0.88f);
    private static readonly Color LineColor = new Color(0.40f, 0.85f, 1.00f, 0.95f);
    private static readonly Color TickColor = new Color(0.55f, 0.75f, 0.90f, 0.85f);
    private static readonly Color TextColor = new Color(0.78f, 0.94f, 1.00f, 1f);
    private static readonly Color PoiColor = new Color(1.00f, 0.78f, 0.30f, 1f);
    private static readonly Color SelectedColor = new Color(1.00f, 0.24f, 0.20f, 1f);
    private static readonly Color ShipColor = new Color(0.45f, 0.90f, 1.00f, 1f);
    private static readonly Color CaretColor = new Color(1.00f, 0.85f, 0.30f, 1f);

    // Ship-only HUD widgets (shields, ship systems, target display, power bars,
    // target braces...). On foot only the compass is shown, so they stay hidden
    // until the player boards again.
    private static readonly string[] ShipHudElements =
    {
        "ShipInfo", "Shield", "TargetDisplay", "EnergyManagement",
        "Crosshair", "Crosshair_Locked", "Autoaim", "TargetSelected",
        "Target", "InterceptPoint", "Arrow_Target", "Arrow_Steering",
        "Arrow_Waypoint", "Warning",
    };

    private readonly Dictionary<GameObject, bool> hiddenShipHud = new Dictionary<GameObject, bool>();
    private GameObject canvasRoot;
    private GameObject root;
    private RectTransform markersArea;
    private readonly Dictionary<Text, Vector2> logOriginalPositions = new Dictionary<Text, Vector2>();
    private Text headingText;
    private Text readoutText;
    private readonly List<Text> tickPool = new List<Text>();
    private readonly List<PoiMarker> poiPool = new List<PoiMarker>();
    private Text shipMarker;
    private Text centerCaret;
    private bool lastOnFoot;
    private float padRepeatTimer;

    private class PoiMarker
    {
        public Text marker;
    }

    public static void Ensure()
    {
        if (Instance != null)
        {
            return;
        }

        GameObject go = new GameObject("GroundCompass");
        Instance = go.AddComponent<GroundCompass>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        Hud hud = HudFunctions.GetHud();
        GroundCharacterController walker = GroundCharacterController.Instance;
        bool onFoot = walker != null && walker.IsActive;

        if (onFoot && canvasRoot == null)
        {
            BuildUi();
        }

        if (canvasRoot == null)
        {
            return;
        }

        canvasRoot.SetActive(onFoot);

        if (!onFoot)
        {
            if (lastOnFoot)
            {
                RestoreShipHud();
                RestoreReplacedHud(hud);
                RestoreLogPositions();
            }

            lastOnFoot = false;
            return;
        }

        if (!lastOnFoot)
        {
            HideShipHud(hud);
            ShiftLogPositions(hud);
        }

        // Hide the ship radar + 3D waypoint marker while walking (both
        // restored on board below). The compass replaces them on foot.
        HideReplacedHud(hud);

        lastOnFoot = true;
        HandleSelectionInput();
        Refresh(hud, walker);
    }

    private void ShiftLogPositions(Hud hud)
    {
        logOriginalPositions.Clear();

        if (hud == null)
        {
            return;
        }

        Text[] texts = hud.GetComponentsInChildren<Text>(true);

        foreach (Text text in texts)
        {
            if (text == null || text.rectTransform == null)
            {
                continue;
            }

            if (text.name.Contains("MissionLog") == false && text.name.Contains("MessageLog") == false)
            {
                continue;
            }

            RectTransform rt = text.rectTransform;

            if (logOriginalPositions.ContainsKey(text) == false)
            {
                logOriginalPositions.Add(text, rt.anchoredPosition);
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, rt.anchoredPosition.y - LogShiftY);
            }
        }
    }

    private void RestoreLogPositions()
    {
        foreach (KeyValuePair<Text, Vector2> entry in logOriginalPositions)
        {
            if (entry.Key != null && entry.Key.rectTransform != null)
            {
                entry.Key.rectTransform.anchoredPosition = entry.Value;
            }
        }

        logOriginalPositions.Clear();
    }

    // Hides every ship-only widget once when the player steps out. Mission log,
    // objectives and hints stay visible; the compass canvas is separate.
    private void HideShipHud(Hud hud)
    {
        if (hud == null)
        {
            return;
        }

        hiddenShipHud.Clear();

        foreach (string elementName in ShipHudElements)
        {
            HideNamedHudElements(hud.transform, elementName);
        }

        // Target braces are instantiated at runtime from the "Target" template,
        // so they are not part of the prefab hierarchy walked above.
        Transform[] allChildren = hud.transform.GetComponentsInChildren<Transform>(true);

        foreach (Transform child in allChildren)
        {
            if (child.name.StartsWith("Target(") == true)
            {
                HideHudElement(child.gameObject);
            }
        }
    }

    private void HideNamedHudElements(Transform root, string elementName)
    {
        Transform[] elements = root.GetComponentsInChildren<Transform>(true);

        foreach (Transform element in elements)
        {
            if (element.name == elementName)
            {
                HideHudElement(element.gameObject);
            }
        }
    }

    private void HideHudElement(GameObject element)
    {
        if (element == null || hiddenShipHud.ContainsKey(element) == true)
        {
            return;
        }

        // Remember the previous state so widgets that start hidden (Warning,
        // the Target template...) are not revealed when boarding again.
        hiddenShipHud.Add(element, element.activeSelf);

        if (element.activeSelf == true)
        {
            element.SetActive(false);
        }
    }

    private void RestoreShipHud()
    {
        foreach (KeyValuePair<GameObject, bool> entry in hiddenShipHud)
        {
            if (entry.Key != null && entry.Value == true && entry.Key.activeSelf == false)
            {
                entry.Key.SetActive(true);
            }
        }

        hiddenShipHud.Clear();
    }

    private static void HideReplacedHud(Hud hud)
    {        if (hud == null)
        {
            return;
        }

        CanvasGroup radar = HudFunctions.FindCanvasGroup("Radar");

        if (radar != null && radar.gameObject != null && radar.gameObject.activeSelf == true)
        {
            radar.gameObject.SetActive(false);
        }

        CanvasGroup waypoint = HudFunctions.FindCanvasGroup("Waypoint");

        if (waypoint != null && waypoint.gameObject != null && waypoint.gameObject.activeSelf == true)
        {
            waypoint.gameObject.SetActive(false);
        }
    }

    private static void RestoreReplacedHud(Hud hud)
    {
        if (hud == null)
        {
            return;
        }

        CanvasGroup radar = HudFunctions.FindCanvasGroup("Radar");

        if (radar != null && radar.gameObject != null && radar.gameObject.activeSelf == false)
        {
            radar.gameObject.SetActive(true);
        }

        // DisplayWaypoint resumes control next frame; just restore its flag state.
        CanvasGroup waypoint = HudFunctions.FindCanvasGroup("Waypoint");

        if (waypoint != null && waypoint.gameObject != null)
        {
            waypoint.gameObject.SetActive(hud.waypointActive);
        }
    }

    private void HandleSelectionInput()
    {
        PlanetNavigation nav = PlanetNavigation.Active;

        if (nav == null || !nav.HasPois)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;

        if (keyboard != null)
        {
            if (keyboard.qKey.wasPressedThisFrame)
            {
                nav.StepPoi(-1);
            }
            else if (keyboard.eKey.wasPressedThisFrame)
            {
                nav.StepPoi(1);
            }
        }

        Gamepad gamepad = Gamepad.current;

        if (gamepad != null)
        {
            padRepeatTimer -= Time.unscaledDeltaTime;
            bool left = IsDpadPressed(gamepad, true);
            bool right = IsDpadPressed(gamepad, false);

            if ((left || right) && padRepeatTimer <= 0f)
            {
                nav.StepPoi(left ? -1 : 1);
                padRepeatTimer = 0.35f;
            }

            if (!left && !right)
            {
                padRepeatTimer = 0f;
            }
        }
    }

    private static bool IsDpadPressed(Gamepad gamepad, bool left)
    {
        try
        {
            // DpadControl exposes left/right buttons; guard for layouts without them.
            if (left)
            {
                return gamepad.dpad.left.wasPressedThisFrame || gamepad.dpad.left.isPressed && false;
            }

            return gamepad.dpad.right.wasPressedThisFrame || gamepad.dpad.right.isPressed && false;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    private void Refresh(Hud hud, GroundCharacterController walker)
    {
        float yaw = walker.ViewYaw;
        float heading = Wrap360(yaw);
        headingText.text = string.Format("{0:000} {1}", Mathf.RoundToInt(heading), Cardinal(heading));

        float halfW = 300f;

        if (markersArea != null && markersArea.rect.width > 10f)
        {
            halfW = markersArea.rect.width * 0.5f;
        }

        UpdateTicks(yaw, halfW);

        Vector3 charPos = walker.CharacterPosition;
        PlanetNavigation nav = PlanetNavigation.Active;

        int selected = nav != null ? nav.SelectedPoiIndex : -1;
        string readout = "SIN DATOS NAV · H PARA FIJAR OBJETIVO";
        int markerIdx = 0;

        if (nav != null && nav.HasPois)
        {
            for (int i = 0; i < nav.PoiCount; i++)
            {
                Vector3 poiPos = nav.GetPoiWorldPosition(i);

                if (poiPos == Vector3.zero)
                {
                    continue;
                }

                Vector3 flat = poiPos - charPos;
                flat.y = 0f;
                float dist = flat.magnitude;

                if (dist < 0.5f)
                {
                    continue;
                }

                float bearing = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                float rel = Mathf.DeltaAngle(yaw, bearing);
                bool isSelected = (i == selected);
                PoiMarker m = GetPoiMarker(markerIdx++);
                // Single-line markers: no separate name labels (they overlapped
                // the readout). Full name + distance lives in the readout line.
                PlaceMarker(m, rel, halfW, isSelected, isSelected);

                string distText = FormatDistance(dist);

                if (isSelected)
                {
                    m.marker.text = "◉ " + distText;
                    m.marker.color = SelectedColor;
                    m.marker.fontSize = 14;
                    readout = "◉ " + nav.GetPoiName(i) + " — " + distText + "   ·   Q/E CAMBIAR OBJETIVO";
                }
                else
                {
                    bool visited = nav.IsPoiVisited(i);
                    m.marker.text = visited ? "✓" : "◇";
                    m.marker.color = visited ? TickColor : PoiColor;
                    m.marker.fontSize = 12;
                }
            }
        }

        // Hide unused markers.
        for (int k = markerIdx; k < poiPool.Count; k++)
        {
            poiPool[k].marker.gameObject.SetActive(false);
        }

        UpdateShipMarker(hud, walker, yaw, halfW);

        if (nav != null && nav.HasPois && selected >= 0 && selected < nav.PoiCount)
        {
            readoutText.text = readout;
            readoutText.color = TextColor;
        }
        else if (nav != null && nav.HasPois)
        {
            readoutText.text = "Q/E PARA ELEGIR DESTINO";
            readoutText.color = TickColor;
        }
        else
        {
            readoutText.text = readout;
            readoutText.color = TickColor;
        }
    }

    private void UpdateShipMarker(Hud hud, GroundCharacterController walker, float yaw, float halfW)
    {
        Scene scene = SceneFunctions.GetScene();

        if (scene == null || scene.mainShip == null || shipMarker == null)
        {
            if (shipMarker != null)
            {
                shipMarker.gameObject.SetActive(false);
            }

            return;
        }

        Vector3 delta = scene.mainShip.transform.position - walker.CharacterPosition;
        delta.y = 0f;
        float dist = delta.magnitude;

        if (dist < 1f)
        {
            shipMarker.gameObject.SetActive(false);
            return;
        }

        float bearing = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
        float rel = Mathf.DeltaAngle(yaw, bearing);
        // Wide clamp margin: the ship tag is a long single-line text.
        float clamped = Mathf.Clamp(rel, -VisibleHalfAngle, VisibleHalfAngle);
        float x = clamped / VisibleHalfAngle * (halfW - 55f);
        shipMarker.gameObject.SetActive(true);
        shipMarker.rectTransform.anchoredPosition = new Vector2(x, -3f);
        shipMarker.text = "▲ NAVE · " + FormatDistance(dist);
        shipMarker.fontSize = 11;
        shipMarker.color = Mathf.Abs(rel) > VisibleHalfAngle ? new Color(ShipColor.r, ShipColor.g, ShipColor.b, 0.45f) : ShipColor;
    }

    private void UpdateTicks(float yaw, float halfW)
    {
        int slot = 0;

        for (int deg = 0; deg < 360; deg += 15)
        {
            float rel = Mathf.DeltaAngle(yaw, deg);

            if (Mathf.Abs(rel) > VisibleHalfAngle)
            {
                continue;
            }

            Text tick = GetTick(slot++);
            float x = rel / VisibleHalfAngle * (halfW - 14f);
            tick.rectTransform.anchoredPosition = new Vector2(x, -1f);

            string card = Cardinal(deg);

            if (deg % 90 == 0)
            {
                tick.text = card;
                tick.fontSize = 12;
                tick.color = TextColor;
            }
            else if (deg % 45 == 0)
            {
                tick.text = card;
                tick.fontSize = 9;
                tick.color = TickColor;
            }
            else
            {
                tick.text = "·";
                tick.fontSize = 10;
                tick.color = new Color(TickColor.r, TickColor.g, TickColor.b, 0.6f);
            }
        }

        for (int i = slot; i < tickPool.Count; i++)
        {
            tickPool[i].gameObject.SetActive(false);
        }
    }

    private Text GetTick(int slot)
    {
        while (tickPool.Count <= slot)
        {
            Text t = CreateText(markersArea, 11, TextAnchor.MiddleCenter);
            t.raycastTarget = false;
            tickPool.Add(t);
        }

        Text tick = tickPool[slot];
        tick.gameObject.SetActive(true);
        return tick;
    }

    private PoiMarker GetPoiMarker(int slot)
    {
        while (poiPool.Count <= slot)
        {
            Text marker = CreateText(markersArea, 12, TextAnchor.MiddleCenter);
            marker.raycastTarget = false;
            RectTransform mrt = marker.rectTransform;
            mrt.anchorMin = new Vector2(0.5f, 0.5f);
            mrt.anchorMax = new Vector2(0.5f, 0.5f);
            mrt.sizeDelta = new Vector2(130f, 16f);

            poiPool.Add(new PoiMarker { marker = marker });
        }

        PoiMarker m = poiPool[slot];
        m.marker.gameObject.SetActive(true);
        return m;
    }

    private void PlaceMarker(PoiMarker m, float relAngle, float halfW, bool selected, bool wide)
    {
        float margin = wide ? 55f : 16f;
        float clamped = Mathf.Clamp(relAngle, -VisibleHalfAngle, VisibleHalfAngle);
        float x = clamped / VisibleHalfAngle * (halfW - margin);
        bool edged = Mathf.Abs(relAngle) > VisibleHalfAngle;

        m.marker.rectTransform.anchoredPosition = new Vector2(x, -3f);

        if (edged && !selected)
        {
            Color c = m.marker.color;
            m.marker.color = new Color(c.r, c.g, c.b, 0.45f);
        }
    }

    private static string FormatDistance(float meters)
    {
        if (meters >= 1000f)
        {
            return (meters / 1000f).ToString("0.00") + " KM";
        }

        return Mathf.RoundToInt(meters) + " M";
    }

    private static float Wrap360(float a)
    {
        a %= 360f;

        if (a < 0f)
        {
            a += 360f;
        }

        return a;
    }

    private static string Cardinal(float heading)
    {
        float h = Wrap360(heading);
        int oct = Mathf.FloorToInt(((h + 22.5f) % 360f) / 45f);
        switch (oct)
        {
            case 0: return "N";
            case 1: return "NE";
            case 2: return "E";
            case 3: return "SE";
            case 4: return "S";
            case 5: return "SW";
            case 6: return "W";
            default: return "NW";
        }
    }

    private void BuildUi()
    {
        // Own overlay canvas: the legacy Hud uses a centered 100x100 container,
        // so anchoring a top bar there would need unit conversions. A dedicated
        // ScreenSpaceOverlay canvas with the same 800x600 reference keeps the
        // bar geometry in plain pixels.
        canvasRoot = new GameObject("FootCompassCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = canvasRoot.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(800f, 600f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0f;
        GraphicRaycaster raycaster = canvasRoot.GetComponent<GraphicRaycaster>();
        raycaster.enabled = false;

        root = new GameObject("FootCompass", typeof(RectTransform), typeof(CanvasGroup));
        root.transform.SetParent(canvasRoot.transform, false);

        RectTransform rt = root.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -10f);
        rt.sizeDelta = new Vector2(BarWidth, BarHeight);

        Image bg = root.AddComponent<Image>();
        bg.color = BgColor;
        bg.raycastTarget = false;

        // Thin cyan hairline at the bottom edge.
        GameObject lineGo = new GameObject("FootCompassLine", typeof(RectTransform), typeof(Image));
        lineGo.transform.SetParent(root.transform, false);
        RectTransform lineRt = lineGo.GetComponent<RectTransform>();
        lineRt.anchorMin = new Vector2(0f, 0f);
        lineRt.anchorMax = new Vector2(1f, 0f);
        lineRt.pivot = new Vector2(0.5f, 0f);
        lineRt.anchoredPosition = Vector2.zero;
        lineRt.sizeDelta = new Vector2(0f, 2f);
        Image line = lineGo.GetComponent<Image>();
        line.color = LineColor;
        line.raycastTarget = false;

        headingText = CreateText(root.transform, 13, TextAnchor.MiddleCenter);
        headingText.fontStyle = FontStyle.Bold;
        headingText.color = TextColor;
        RectTransform hrt = headingText.rectTransform;
        hrt.anchorMin = new Vector2(0.5f, 1f);
        hrt.anchorMax = new Vector2(0.5f, 1f);
        hrt.pivot = new Vector2(0.5f, 1f);
        hrt.anchoredPosition = new Vector2(0f, -1f);
        hrt.sizeDelta = new Vector2(300f, 17f);

        GameObject areaGo = new GameObject("FootCompassTape", typeof(RectTransform), typeof(RectMask2D));
        areaGo.transform.SetParent(root.transform, false);
        markersArea = areaGo.GetComponent<RectTransform>();
        markersArea.anchorMin = new Vector2(0f, 1f);
        markersArea.anchorMax = new Vector2(1f, 1f);
        markersArea.pivot = new Vector2(0.5f, 1f);
        markersArea.anchoredPosition = new Vector2(0f, -16f);
        markersArea.sizeDelta = new Vector2(0f, 18f);

        centerCaret = CreateText(markersArea, 9, TextAnchor.MiddleCenter);
        centerCaret.text = "▼";
        centerCaret.color = CaretColor;
        centerCaret.raycastTarget = false;
        RectTransform crt = centerCaret.rectTransform;
        crt.anchorMin = new Vector2(0.5f, 0.5f);
        crt.anchorMax = new Vector2(0.5f, 0.5f);
        crt.pivot = new Vector2(0.5f, 0.5f);
        crt.anchoredPosition = new Vector2(0f, 4f);
        crt.sizeDelta = new Vector2(30f, 10f);

        shipMarker = CreateText(markersArea, 11, TextAnchor.MiddleCenter);
        shipMarker.raycastTarget = false;
        shipMarker.rectTransform.sizeDelta = new Vector2(130f, 16f);

        readoutText = CreateText(root.transform, 10, TextAnchor.MiddleCenter);
        readoutText.color = TextColor;
        RectTransform rrt = readoutText.rectTransform;
        rrt.anchorMin = new Vector2(0.5f, 0f);
        rrt.anchorMax = new Vector2(0.5f, 0f);
        rrt.pivot = new Vector2(0.5f, 0f);
        rrt.anchoredPosition = new Vector2(0f, 2f);
        rrt.sizeDelta = new Vector2(600f, 14f);

        canvasRoot.SetActive(false);
    }

    private static Text CreateText(Transform parent, int fontSize, TextAnchor alignment)
    {
        GameObject go = new GameObject("FootCompassText", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        Text text = go.GetComponent<Text>();
        text.font = GetDefaultFont();
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = TextColor;
        text.raycastTarget = false;
        RectTransform rt = text.rectTransform;
        rt.sizeDelta = new Vector2(60f, 22f);
        return text;
    }

    private static Font GetDefaultFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        if (font == null)
        {
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        return font;
    }
}
