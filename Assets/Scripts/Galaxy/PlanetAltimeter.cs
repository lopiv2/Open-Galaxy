using UnityEngine;
using UnityEngine.UI;

// Digital altitude tape shown only while flying inside a planet atmosphere.
// Reads the height above the streamed terrain tiles (AGL) and displays it as
// a scrolling scale plus a vertical fill bar and a numeric readout.
public class PlanetAltimeter : MonoBehaviour
{
    [Header("Scale")]
    public float maxAltitude = 1600f; // Above this the display clamps (planet exit ceiling is 1500 m)
    public float pixelsPerMeter = 0.35f;
    public float majorStep = 200f;
    public float minorStep = 50f;

    [Header("Position")]
    public Vector2 screenOffset = new Vector2(-32f, 0f); // From the right edge, screen centre

    private RectTransform root;
    private RectTransform tapeContent;
    private RectTransform fillBar;
    private Text readout;
    private Image fillImage;
    private Canvas canvas;
    private Scene scene;
    private float displayedAltitude;
    private bool hasGround;

    private static readonly Color BackgroundColor = new Color(0.02f, 0.05f, 0.08f, 0.72f);
    private static readonly Color MajorColor = new Color(0.72f, 0.95f, 1f, 0.95f);
    private static readonly Color MinorColor = new Color(0.45f, 0.72f, 0.82f, 0.65f);
    private static readonly Color AccentColor = new Color(0.55f, 1f, 0.85f, 1f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (FindAnyObjectByType<PlanetAltimeter>() != null)
        {
            return;
        }

        GameObject go = new GameObject("PlanetAltimeter");
        go.AddComponent<PlanetAltimeter>();
    }

    private void Start()
    {
        BuildUI();
        root.gameObject.SetActive(false);
    }

    private void Update()
    {
        // The altimeter is a ship instrument: never show it on foot.
        bool onFoot = GroundCharacterController.Instance != null
            && GroundCharacterController.Instance.IsActive == true;

        // Never show the altimeter in space.
        bool visible = GalaxyPlanetEntry.IsOnPlanet == true
            && GalaxyPlanetEntry.IsTransitioning == false
            && onFoot == false;

        if (root != null && root.gameObject.activeSelf != visible)
        {
            root.gameObject.SetActive(visible);
        }

        if (visible == false)
        {
            return;
        }

        if (scene == null)
        {
            scene = SceneFunctions.GetScene();
        }

        if (scene == null || scene.mainShip == null || root == null)
        {
            return;
        }

        float groundHeight = SampleGroundHeight(scene.mainShip.transform.position);
        hasGround = float.IsNaN(groundHeight) == false;

        if (hasGround == true)
        {
            float altitude = Mathf.Clamp(scene.mainShip.transform.position.y - groundHeight, 0f, maxAltitude);
            displayedAltitude = Mathf.Lerp(displayedAltitude, altitude, 1f - Mathf.Exp(-12f * Time.deltaTime));

            // The content is anchored to the bottom of the window: raising the
            // altitude scrolls the tape down so higher numbers rise into view.
            tapeContent.anchoredPosition = new Vector2(0f, 160f - displayedAltitude * pixelsPerMeter);

            float fraction = Mathf.Clamp01(displayedAltitude / maxAltitude);
            fillImage.fillAmount = fraction;
            fillBar.gameObject.SetActive(fraction > 0.001f);

            readout.text = Mathf.RoundToInt(displayedAltitude).ToString("0000") + " m";
            readout.color = displayedAltitude < 25f ? AccentColor : MajorColor;
        }
        else
        {
            readout.text = "---- m";
            readout.color = MinorColor;
            fillBar.gameObject.SetActive(false);
        }
    }

    private void BuildUI()
    {
        if (canvas == null)
        {
            GameObject canvasGO = new GameObject("PlanetAltimeterCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;

            CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        root = CreateRect("PlanetAltimeter", canvas.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(1f, 0.5f), new Vector2(150f, 420f), screenOffset);
        AddImage(root, BackgroundColor);

        Outline outline = root.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.45f, 0.85f, 0.95f, 0.55f);
        outline.effectDistance = new Vector2(1f, -1f);

        CreateText("Title", root, "AGL / ALT", 13, MajorColor, TextAnchor.MiddleCenter,
            new Vector2(150f, 24f), new Vector2(0f, 190f));

        CreateText("Unit", root, "METRES", 10, MinorColor, TextAnchor.MiddleCenter,
            new Vector2(150f, 20f), new Vector2(0f, -193f));

        // Scrolling tape window (masked).
        RectTransform window = CreateRect("TapeWindow", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(74f, 320f), new Vector2(16f, -8f));
        AddImage(window, new Color(0f, 0f, 0f, 0.45f));
        window.gameObject.AddComponent<RectMask2D>();

        tapeContent = CreateRect("TapeContent", window, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f), new Vector2(70f, maxAltitude * pixelsPerMeter + 60f), Vector2.zero);

        Font font = GetFont();

        for (float altitude = 0f; altitude <= maxAltitude; altitude += minorStep)
        {
            bool major = Mathf.Abs(altitude % majorStep) < 0.01f;
            float y = altitude * pixelsPerMeter;

            RectTransform tick = CreateRect("Tick_" + altitude, tapeContent,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(major ? 24f : 12f, major ? 2f : 1f), new Vector2(0f, y));
            AddImage(tick, major ? MajorColor : MinorColor);

            if (major == true)
            {
                CreateText("Label_" + altitude, tapeContent, Mathf.RoundToInt(altitude).ToString(), 12,
                    MajorColor, TextAnchor.MiddleLeft, new Vector2(44f, 18f), new Vector2(28f, y), font);
            }
        }

        // Fixed pointer across the centre of the tape.
        RectTransform pointer = CreateRect("Pointer", window, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(0f, 2f), Vector2.zero);
        AddImage(pointer, AccentColor);

        // Digital fill bar to the left of the tape.
        RectTransform barBackground = CreateRect("BarBackground", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(8f, 320f), new Vector2(-48f, -8f));
        AddImage(barBackground, new Color(0.1f, 0.18f, 0.22f, 0.85f));

        fillBar = CreateRect("BarFill", barBackground, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f), new Vector2(8f, 320f), Vector2.zero);
        fillImage = AddImage(fillBar, AccentColor);
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Vertical;
        fillImage.fillOrigin = (int)Image.OriginVertical.Bottom;
        fillImage.fillAmount = 0f;

        // Fixed numeric readout in a highlighted window.
        RectTransform readoutBox = CreateRect("ReadoutBox", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(140f, 44f), new Vector2(16f, -8f));
        AddImage(readoutBox, new Color(0.01f, 0.03f, 0.04f, 0.92f));

        Outline boxOutline = readoutBox.gameObject.AddComponent<Outline>();
        boxOutline.effectColor = AccentColor;
        boxOutline.effectDistance = new Vector2(1f, -1f);

        readout = CreateText("Readout", readoutBox, "---- m", 22, AccentColor, TextAnchor.MiddleCenter,
            readoutBox.sizeDelta, Vector2.zero, font);
        readout.fontStyle = FontStyle.Bold;
    }

    // Only terrain tiles count: the ray starts above the ship and would
    // otherwise hit the ship itself and report zero altitude.
    private static float SampleGroundHeight(Vector3 worldPosition)
    {
        Vector3 origin = new Vector3(worldPosition.x, worldPosition.y + 400f, worldPosition.z);
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, origin.y + 2000f, ~0, QueryTriggerInteraction.Ignore);
        float bestHeight = float.NegativeInfinity;

        foreach (RaycastHit hitInfo in hits)
        {
            if (hitInfo.transform != null && hitInfo.transform.name.StartsWith("Tile_") == true && hitInfo.point.y > bestHeight)
            {
                bestHeight = hitInfo.point.y;
            }
        }

        return float.IsNegativeInfinity(bestHeight) ? float.NaN : bestHeight;
    }

    private static Font GetFont()
    {
        Font font = null;

        try
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
        catch (System.Exception)
        {
            font = null;
        }

        if (font == null)
        {
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        return font;
    }

    private static RectTransform CreateRect(string objectName, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 pivot, Vector2 size, Vector2 position)
    {
        GameObject go = new GameObject(objectName, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;

        return rect;
    }

    private static Image AddImage(RectTransform rect, Color color)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Text CreateText(string objectName, Transform parent, string value, int fontSize, Color color,
        TextAnchor alignment, Vector2 size, Vector2 position, Font font = null)
    {
        RectTransform rect = CreateRect(objectName, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), size, position);

        Text text = rect.gameObject.AddComponent<Text>();
        text.text = value;
        text.font = font != null ? font : GetFont();
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.supportRichText = false;

        return text;
    }
}
