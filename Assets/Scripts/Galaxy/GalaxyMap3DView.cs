using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// A lightweight, isolated 3D galaxy map rendered into the existing map panel.
// It uses StarSystems X/Y/Z, supports edge-pan, drag-pan, orbit, zoom and picking.
public class GalaxyMap3DView : MonoBehaviour
{
    private const int MapLayer = 31;
    private const float CoordinateScale = 50f / 15000f;
    private const float HeightScale = 25f / 15000f;

    private sealed class Marker
    {
        public StarSystem system;
        public Vector3 position;
        public GameObject gameObject;
        public Renderer renderer;
        public LineRenderer hoverRing;
        public Text label;
        public Vector3 baseScale;
    }

    private sealed class ProjectedLabel
    {
        public Vector3 position;
        public Text text;
    }

    private RectTransform viewport;
    private RawImage output;
    private Camera mapCamera;
    private RenderTexture renderTexture;
    private GameObject cameraObject;
    private GameObject worldRoot;
    private Transform markersRoot;
    private Transform gridRoot;
    private Font font;
    private Material markerMaterial;
    private Material ringMaterial;
    private Material gridMaterial;
    private readonly List<Marker> markers = new List<Marker>();
    private readonly List<ProjectedLabel> gridLabels = new List<ProjectedLabel>();
    private Action<string> onSystemSelected;

    private Vector3 viewTarget;
    private Marker hoveredMarker;
    private string currentSystem;
    private string selectedSystem;
    private bool mapVisible;
    private bool gridVisible = true;
    private bool namesVisible = true;
    private bool rightDragging;
    private bool middleDragging;
    private Vector2 lastPointer;
    private float yaw;
    private float pitch = 25f;
    private float cameraDistance = 58f;
    private float lastWidth;
    private float lastHeight;

    public void Initialize(RectTransform mapViewport, RawImage mapOutput, Font uiFont)
    {
        viewport = mapViewport;
        output = mapOutput;
        font = uiFont;

        if (renderTexture == null)
        {
            renderTexture = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32);
            renderTexture.name = "GalaxyMap3D_RT";
            renderTexture.antiAliasing = 2;
            renderTexture.Create();
        }

        output.texture = renderTexture;
        output.color = Color.white;
        output.raycastTarget = false;

        CreateWorld();
        ConfigureCamera();
        SetVisible(false);
    }

    public void SetSystems(List<StarSystem> systems, string current, string selected, Action<string> selectCallback)
    {
        currentSystem = current;
        selectedSystem = selected;
        onSystemSelected = selectCallback;
        ClearMarkers();
        ClearGrid();

        if (systems == null || systems.Count == 0 || worldRoot == null)
        {
            return;
        }

        Vector3 sum = Vector3.zero;

        foreach (StarSystem system in systems)
        {
            sum += ToMapPosition(system);
        }

        viewTarget = sum / systems.Count;

        foreach (StarSystem system in systems)
        {
            CreateMarker(system);
        }

        CreateGrid();
        UpdateMarkerStates();
        SetGridVisible(gridVisible);
        SetNamesVisible(namesVisible);
        UpdateCameraTransform();
    }

    public void SetSelection(string current, string selected)
    {
        currentSystem = current;
        selectedSystem = selected;
        UpdateMarkerStates();
    }

    public void SetGridVisible(bool visible)
    {
        gridVisible = visible;

        if (gridRoot != null)
        {
            gridRoot.gameObject.SetActive(visible);
        }

        foreach (ProjectedLabel label in gridLabels)
        {
            if (label.text != null)
            {
                label.text.gameObject.SetActive(visible);
            }
        }
    }

    public void SetNamesVisible(bool visible)
    {
        namesVisible = visible;

        foreach (Marker marker in markers)
        {
            if (marker.label != null)
            {
                marker.label.gameObject.SetActive(visible);
            }
        }
    }

    public void SetVisible(bool visible)
    {
        mapVisible = visible;

        if (cameraObject != null)
        {
            cameraObject.SetActive(visible);
        }

        if (!visible)
        {
            rightDragging = false;
            middleDragging = false;
            hoveredMarker = null;
            UpdateMarkerStates();
        }
    }

    void Update()
    {
        if (!mapVisible || viewport == null || mapCamera == null)
        {
            return;
        }

        UpdateCameraAspect();
        HandleMapInput();
        UpdateCameraTransform();
    }

    void LateUpdate()
    {
        if (!mapVisible || mapCamera == null)
        {
            return;
        }

        UpdateProjectedLabels();
    }

    void OnDestroy()
    {
        if (cameraObject != null) Destroy(cameraObject);
        if (worldRoot != null) Destroy(worldRoot);

        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
        }

        if (markerMaterial != null) Destroy(markerMaterial);
        if (ringMaterial != null) Destroy(ringMaterial);
        if (gridMaterial != null) Destroy(gridMaterial);
    }

    private void CreateWorld()
    {
        if (worldRoot == null)
        {
            worldRoot = new GameObject("GalaxyMap3DWorld");
        }

        GameObject markerRootGO = new GameObject("Systems");
        markerRootGO.transform.SetParent(worldRoot.transform, false);
        markersRoot = markerRootGO.transform;

        GameObject gridRootGO = new GameObject("Grid");
        gridRootGO.transform.SetParent(worldRoot.transform, false);
        gridRoot = gridRootGO.transform;

        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");

        if (unlit == null)
        {
            unlit = Shader.Find("Unlit/Color");
        }

        markerMaterial = new Material(unlit);
        ringMaterial = CreateUnlitMaterial(unlit, new Color(1f, 0.82f, 0.34f, 0.95f), true);
        gridMaterial = CreateUnlitMaterial(unlit, new Color(0.8f, 0.9f, 1f, 0.23f), true);
    }

    private static Material CreateUnlitMaterial(Shader shader, Color color, bool transparent)
    {
        Material material = new Material(shader);
        material.color = color;
        material.SetColor("_BaseColor", color);

        if (transparent)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        return material;
    }

    private void ConfigureCamera()
    {
        if (cameraObject == null)
        {
            cameraObject = new GameObject("GalaxyMap3DCamera");
            cameraObject.layer = MapLayer;
            mapCamera = cameraObject.AddComponent<Camera>();
            mapCamera.clearFlags = CameraClearFlags.SolidColor;
            mapCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            mapCamera.cullingMask = 1 << MapLayer;
            mapCamera.nearClipPlane = 0.1f;
            mapCamera.farClipPlane = 300f;
            mapCamera.fieldOfView = 45f;
            mapCamera.allowHDR = false;
            mapCamera.allowMSAA = false;
            mapCamera.useOcclusionCulling = false;
            mapCamera.targetTexture = renderTexture;

            UniversalAdditionalCameraData cameraData = mapCamera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false;
        }

        UpdateCameraAspect();
        UpdateCameraTransform();
    }

    private void UpdateCameraAspect()
    {
        if (viewport == null || mapCamera == null)
        {
            return;
        }

        float width = viewport.rect.width;
        float height = viewport.rect.height;

        if (width > 0f && height > 0f && (Mathf.Abs(width - lastWidth) > 1f || Mathf.Abs(height - lastHeight) > 1f))
        {
            mapCamera.aspect = width / height;
            lastWidth = width;
            lastHeight = height;
        }
    }

    private void UpdateCameraTransform()
    {
        if (mapCamera == null)
        {
            return;
        }

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 offset = rotation * new Vector3(0f, 0f, -cameraDistance);
        mapCamera.transform.position = viewTarget + offset;
        mapCamera.transform.rotation = rotation;
    }

    private static Vector3 ToMapPosition(StarSystem system)
    {
        // Matches the project's StarSystems-to-starfield axis convention.
        return new Vector3(system.X * CoordinateScale, system.Z * HeightScale, system.Y * CoordinateScale);
    }

    private void CreateMarker(StarSystem system)
    {
        Vector3 position = ToMapPosition(system);
        GameObject markerGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        markerGO.name = "System_" + system.Planet;
        markerGO.layer = MapLayer;
        markerGO.transform.SetParent(markersRoot, true);
        markerGO.transform.position = position;
        markerGO.transform.localScale = Vector3.one * 1.15f;

        Renderer renderer = markerGO.GetComponent<Renderer>();
        renderer.sharedMaterial = markerMaterial;
        GalaxyMap3DSystemMarker pickMarker = markerGO.AddComponent<GalaxyMap3DSystemMarker>();
        pickMarker.planetName = system.Planet;

        SphereCollider collider = markerGO.GetComponent<SphereCollider>();
        collider.radius = 0.75f;
        collider.isTrigger = true;

        GameObject ringGO = new GameObject("HoverRing");
        ringGO.layer = MapLayer;
        ringGO.transform.SetParent(markerGO.transform, false);
        LineRenderer ring = ringGO.AddComponent<LineRenderer>();
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.positionCount = 48;
        ring.startWidth = 0.07f;
        ring.endWidth = 0.07f;
        ring.sharedMaterial = ringMaterial;
        ring.startColor = ringMaterial.color;
        ring.endColor = ringMaterial.color;
        ring.enabled = false;

        float radius = 1.3f;

        for (int i = 0; i < ring.positionCount; i++)
        {
            float angle = (Mathf.PI * 2f * i) / ring.positionCount;
            ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }

        Text label = CreateProjectedLabel(system.Planet, font, 12);
        Marker marker = new Marker
        {
            system = system,
            position = position,
            gameObject = markerGO,
            renderer = renderer,
            hoverRing = ring,
            label = label,
            baseScale = markerGO.transform.localScale
        };

        markers.Add(marker);
    }

    private Text CreateProjectedLabel(string value, Font labelFont, int size)
    {
        GameObject labelGO = new GameObject("MapLabel_" + value);
        labelGO.transform.SetParent(viewport, false);
        RectTransform rect = labelGO.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(140f, 20f);

        Text text = labelGO.AddComponent<Text>();
        text.font = labelFont;
        text.fontSize = size;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = new Color(0.88f, 0.92f, 1f, 0.95f);
        text.raycastTarget = false;

        return text;
    }

    private void CreateGrid()
    {
        ClearGrid();

        float planeY = viewTarget.y;
        Color color = new Color(0.82f, 0.9f, 1f, 0.18f);

        for (int column = 0; column <= 19; column++)
        {
            float x = (-13500f + 1500f * column) * CoordinateScale;
            CreateGridLine(new Vector3(x, planeY, 14400f * CoordinateScale), new Vector3(x, planeY, -17100f * CoordinateScale), color);
        }

        for (int row = 0; row <= 21; row++)
        {
            float z = (14400f - 1500f * row) * CoordinateScale;
            CreateGridLine(new Vector3(-13500f * CoordinateScale, planeY, z), new Vector3(15000f * CoordinateScale, planeY, z), color);
        }

        for (int column = 0; column < 19; column++)
        {
            float x = (-13500f + 1500f * (column + 0.5f)) * CoordinateScale;
            ProjectedLabel label = new ProjectedLabel
            {
                position = new Vector3(x, planeY, 14400f * CoordinateScale),
                text = CreateProjectedLabel(((char)('C' + column)).ToString(), font, 10)
            };
            label.text.color = new Color(1f, 1f, 1f, 0.66f);
            gridLabels.Add(label);
        }

        for (int row = 1; row <= 21; row++)
        {
            float z = (14400f - 1500f * (row - 0.5f)) * CoordinateScale;
            ProjectedLabel label = new ProjectedLabel
            {
                position = new Vector3(-13500f * CoordinateScale, planeY, z),
                text = CreateProjectedLabel(row.ToString(), font, 10)
            };
            label.text.color = new Color(1f, 1f, 1f, 0.66f);
            gridLabels.Add(label);
        }
    }

    private void CreateGridLine(Vector3 start, Vector3 end, Color color)
    {
        GameObject lineGO = new GameObject("GridLine");
        lineGO.layer = MapLayer;
        lineGO.transform.SetParent(gridRoot, false);
        LineRenderer line = lineGO.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.SetPosition(0, start);
        line.SetPosition(1, end);
        line.startWidth = 0.055f;
        line.endWidth = 0.055f;
        line.sharedMaterial = gridMaterial;
        line.startColor = color;
        line.endColor = color;
    }

    private void ClearMarkers()
    {
        foreach (Marker marker in markers)
        {
            if (marker.gameObject != null) Destroy(marker.gameObject);
            if (marker.label != null) Destroy(marker.label.gameObject);
        }

        markers.Clear();
        hoveredMarker = null;
    }

    private void ClearGrid()
    {
        if (gridRoot != null)
        {
            for (int i = gridRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(gridRoot.GetChild(i).gameObject);
            }
        }

        foreach (ProjectedLabel label in gridLabels)
        {
            if (label.text != null) Destroy(label.text.gameObject);
        }

        gridLabels.Clear();
    }

    private void UpdateMarkerStates()
    {
        foreach (Marker marker in markers)
        {
            bool current = marker.system.Planet == currentSystem;
            bool selected = marker.system.Planet == selectedSystem;
            bool hovered = marker == hoveredMarker;

            Color color = current
                ? new Color(0.25f, 1f, 0.5f, 1f)
                : selected ? new Color(1f, 0.72f, 0.2f, 1f) : new Color(0.58f, 0.78f, 1f, 1f);

            marker.renderer.material.color = color;
            marker.gameObject.transform.localScale = marker.baseScale * (hovered ? 1.45f : selected ? 1.25f : 1f);
            marker.hoverRing.enabled = hovered || selected;

            if (marker.label != null)
            {
                marker.label.color = selected ? new Color(1f, 0.84f, 0.38f, 1f) : Color.white;
                marker.label.fontStyle = selected || current ? FontStyle.Bold : FontStyle.Normal;
            }
        }
    }

    private void UpdateProjectedLabels()
    {
        if (viewport == null || mapCamera == null) return;

        Rect rect = viewport.rect;
        List<Rect> occupied = new List<Rect>();

        foreach (Marker marker in markers)
        {
            UpdateLabelPosition(marker.position + Vector3.up * 0.9f, marker.label, rect, occupied, namesVisible);
        }

        foreach (ProjectedLabel label in gridLabels)
        {
            UpdateLabelPosition(label.position, label.text, rect, occupied, gridVisible);
        }
    }

    private void UpdateLabelPosition(Vector3 worldPosition, Text label, Rect rect, List<Rect> occupied, bool visible)
    {
        if (label == null) return;

        Vector3 projected = mapCamera.WorldToViewportPoint(worldPosition);
        bool onScreen = projected.z > 0f && projected.x >= 0f && projected.x <= 1f && projected.y >= 0f && projected.y <= 1f;

        if (!visible || !onScreen)
        {
            label.gameObject.SetActive(false);
            return;
        }

        label.gameObject.SetActive(true);
        RectTransform labelRect = label.rectTransform;
        float x = (projected.x - 0.5f) * rect.width;
        float y = (projected.y - 0.5f) * rect.height - 13f;
        Vector2 size = labelRect.sizeDelta;
        Rect screenRect = new Rect(x - size.x * 0.5f, y - size.y * 0.5f, size.x, size.y);

        int attempts = 0;
        while (attempts < 5 && occupied.Exists(other => other.Overlaps(screenRect)))
        {
            y -= size.y;
            screenRect.y -= size.y;
            attempts++;
        }

        occupied.Add(screenRect);
        labelRect.anchoredPosition = new Vector2(x, y);
    }

    private void HandleMapInput()
    {
        Vector2 pointer = Input.mousePosition;
        Vector2 local;
        bool inside = RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, pointer, null, out local)
            && viewport.rect.Contains(local);

        if (!inside)
        {
            rightDragging = false;
            middleDragging = false;
            UpdateHover(null);
            return;
        }

        if (Input.GetMouseButtonDown(1))
        {
            rightDragging = true;
            lastPointer = pointer;
        }
        else if (Input.GetMouseButtonUp(1))
        {
            rightDragging = false;
        }

        if (Input.GetMouseButtonDown(2))
        {
            middleDragging = true;
            lastPointer = pointer;
        }
        else if (Input.GetMouseButtonUp(2))
        {
            middleDragging = false;
        }

        Vector2 delta = pointer - lastPointer;

        if (rightDragging && Input.GetMouseButton(1))
        {
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            float panScale = cameraDistance * 0.0015f;
            viewTarget -= rotation * Vector3.right * delta.x * panScale;
            viewTarget += rotation * Vector3.up * delta.y * panScale;
        }
        else if (middleDragging && Input.GetMouseButton(2))
        {
            yaw += delta.x * 0.2f;
            pitch = Mathf.Clamp(pitch - delta.y * 0.15f, 8f, 70f);
        }
        else
        {
            ApplyEdgePan(local);
        }

        lastPointer = pointer;

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            cameraDistance = Mathf.Clamp(cameraDistance - scroll * 4f, 22f, 180f);
        }

        if (Input.GetMouseButtonDown(0) && !PointerOverUIButton(pointer))
        {
            Ray ray = mapCamera.ViewportPointToRay(new Vector3(
                (local.x - viewport.rect.xMin) / viewport.rect.width,
                (local.y - viewport.rect.yMin) / viewport.rect.height,
                0f));

            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, 500f, 1 << MapLayer))
            {
                GalaxyMap3DSystemMarker picked = hit.collider.GetComponentInParent<GalaxyMap3DSystemMarker>();
                if (picked != null)
                {
                    onSystemSelected?.Invoke(picked.planetName);
                }
            }
        }

        Marker hovered = RaycastMarker(local);
        UpdateHover(hovered);
    }

    private Marker RaycastMarker(Vector2 local)
    {
        Vector3 viewportPoint = new Vector3(
            (local.x - viewport.rect.xMin) / viewport.rect.width,
            (local.y - viewport.rect.yMin) / viewport.rect.height,
            0f);
        RaycastHit hit;

        if (Physics.Raycast(mapCamera.ViewportPointToRay(viewportPoint), out hit, 500f, 1 << MapLayer))
        {
            GalaxyMap3DSystemMarker picked = hit.collider.GetComponentInParent<GalaxyMap3DSystemMarker>();

            if (picked != null)
            {
                return markers.Find(marker => marker.system.Planet == picked.planetName);
            }
        }

        return null;
    }

    private void UpdateHover(Marker marker)
    {
        if (marker == hoveredMarker) return;
        hoveredMarker = marker;
        UpdateMarkerStates();
    }

    private void ApplyEdgePan(Vector2 local)
    {
        const float edge = 32f;
        float x = 0f;
        float y = 0f;

        if (local.x < viewport.rect.xMin + edge) x = -1f;
        else if (local.x > viewport.rect.xMax - edge) x = 1f;

        if (local.y < viewport.rect.yMin + edge) y = -1f;
        else if (local.y > viewport.rect.yMax - edge) y = 1f;

        if (x != 0f || y != 0f)
        {
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            float speed = cameraDistance * 0.45f * Time.unscaledDeltaTime;
            viewTarget += rotation * Vector3.right * x * speed;
            viewTarget += rotation * Vector3.up * y * speed;
        }
    }

    private bool PointerOverUIButton(Vector2 screenPosition)
    {
        if (EventSystem.current == null) return false;

        PointerEventData pointer = new PointerEventData(EventSystem.current);
        pointer.position = screenPosition;
        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, results);

        foreach (RaycastResult result in results)
        {
            if (result.gameObject.GetComponentInParent<Button>() != null)
            {
                return true;
            }
        }

        return false;
    }
}

public class GalaxyMap3DSystemMarker : MonoBehaviour
{
    public string planetName;
}
