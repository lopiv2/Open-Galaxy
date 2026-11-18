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
    private const float GridOriginX = -13500f;
    private const float GridTopY = 13500f;
    private const float GridCellSize = 1500f;

    private sealed class Marker
    {
        public StarSystem system;
        public Vector3 position;
        public GameObject gameObject;
        public Renderer renderer;
        public GameObject halo;
        public Renderer haloRenderer;
        public LineRenderer hoverRing;
        public Text label;
    }

    private sealed class ProjectedLabel
    {
        public Vector3 position;
        public Text text;
        public bool pinToLeftEdge;
        public int gridRow;
    }

    private RectTransform viewport;
    private RawImage output;
    private Camera mapCamera;
    private RenderTexture renderTexture;
    private GameObject cameraObject;
    private GameObject worldRoot;
    private GameObject galaxyBackdrop;
    private GameObject galaxyCore;
    private ParticleSystem galaxyParticles;
    private Transform markersRoot;
    private Transform gridRoot;
    private Transform routesRoot;
    private Font font;
    private Material markerMaterial;
    private Material ringMaterial;
    private Material gridMaterial;
    private Material routeMaterial;
    private Material haloMaterial;
    private Texture2D haloTexture;
    private Material galaxyBackdropMaterial;
    private Material galaxyCoreMaterial;
    private Texture2D galaxyTexture;
    private Vector3 galaxyCenter;
    private float galaxyPlaneY;
    private const float PlanetsY = 20.2f;
    private const float GridYOffset = -0.5f;
    private const float GalaxyYOffset = -1.0f;
    private readonly List<Marker> markers = new List<Marker>();
    private readonly List<ProjectedLabel> gridLabels = new List<ProjectedLabel>();
    private readonly List<GameObject> routeLines = new List<GameObject>();
    private Action<string> onSystemSelected;

    // Small bright core + soft halo, SWTOR-atlas style. Labels only show when
    // zoomed in past labelsMaxDistance so the overview stays readable.
    // These are public so you can tune them live in Play Mode: open the map,
    // select the GalaxyMap3DView object and move the sliders.
    [Header("Marker style")]
    [Tooltip("Diameter of the 3D planet spheres.")]
    [Range(0.1f, 1.5f)]
    public float planetSize = 0.34f;
    [Tooltip("Diameter of the glow halo behind each planet.")]
    [Range(0.5f, 4f)]
    public float haloSize = 1.6f;
    [Tooltip("How far below the planet its name sits (world units).")]
    [Range(0.2f, 2f)]
    public float labelHeight = 0.55f;
    [Tooltip("Size of the planet name text.")]
    [Range(8, 20)]
    public int labelFontSize = 12;
    [Tooltip("Planet names hide when the camera is farther than this.")]
    [Range(20f, 180f)]
    public float labelsMaxDistance = 56f;
    private const float MarkerRingRadius = 2.9f;
    private const float MarkerColliderRadius = 1.8f;
    private Vector3 viewTarget;
    private Marker hoveredMarker;
    private string currentSystem;
    private string selectedSystem;
    private bool mapVisible;
    private bool gridVisible;
    private bool namesVisible = true;
    private bool rightDragging;
    private bool middleDragging;
    private Vector2 lastPointer;
    private float yaw;
    private float pitch = 57f;
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
        viewTarget.y = PlanetsY;
        // Place the spiral origin at the center of grid cell K11.
        // Coruscant (0,0) then sits naturally above/right toward K-L / rows 9-10.
        galaxyCenter = new Vector3(
            (GridOriginX + (8f + 0.5f) * GridCellSize) * CoordinateScale,
            PlanetsY,
            (GridTopY - (11f - 0.5f) * GridCellSize) * CoordinateScale);
        galaxyPlaneY = PlanetsY + GalaxyYOffset;
        UpdateGalaxyBackdropPosition();

        foreach (StarSystem system in systems)
        {
            CreateMarker(system);
        }

        CreateRouteLines();
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

        // Keep every halo quad facing the camera (billboard) and in sync with
        // the live sliders (haloSize, planetSize apply every frame).
        Quaternion faceCamera = mapCamera.transform.rotation;

        foreach (Marker marker in markers)
        {
            if (marker.halo != null)
            {
                marker.halo.transform.rotation = faceCamera;
                marker.halo.transform.localScale = Vector3.one * haloSize;
            }

            if (marker.gameObject != null)
            {
                marker.gameObject.transform.localScale = Vector3.one * planetSize * GetMarkerScaleMultiplier(marker);
            }
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
        if (routeMaterial != null) Destroy(routeMaterial);
        if (haloMaterial != null) Destroy(haloMaterial);
        if (haloTexture != null) Destroy(haloTexture);
        if (galaxyBackdropMaterial != null) Destroy(galaxyBackdropMaterial);
        if (galaxyCoreMaterial != null) Destroy(galaxyCoreMaterial);
        if (galaxyTexture != null) Destroy(galaxyTexture);
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

        GameObject routesRootGO = new GameObject("Routes");
        routesRootGO.transform.SetParent(worldRoot.transform, false);
        routesRoot = routesRootGO.transform;

        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");

        if (unlit == null)
        {
            unlit = Shader.Find("Unlit/Color");
        }

        // Planet cores are real 3D spheres with a lit material so they read as
        // volumes (highlight + shaded side) instead of flat discs. The map has
        // its own key light restricted to the map layer for a consistent look.
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");

        if (lit == null)
        {
            lit = Shader.Find("Standard");
        }

        markerMaterial = new Material(lit);

        if (markerMaterial.HasProperty("_BaseColor"))
        {
            markerMaterial.SetColor("_BaseColor", Color.white);
        }
        else
        {
            markerMaterial.SetColor("_Color", Color.white);
        }

        if (markerMaterial.HasProperty("_Smoothness"))
        {
            markerMaterial.SetFloat("_Smoothness", 0.55f);
        }
        else if (markerMaterial.HasProperty("_Glossiness"))
        {
            markerMaterial.SetFloat("_Glossiness", 0.55f);
        }

        if (markerMaterial.HasProperty("_Metallic"))
        {
            markerMaterial.SetFloat("_Metallic", 0.1f);
        }

        GameObject lightGO = new GameObject("MapKeyLight");
        lightGO.layer = MapLayer;
        lightGO.transform.SetParent(worldRoot.transform, false);
        lightGO.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
        Light keyLight = lightGO.AddComponent<Light>();
        keyLight.type = LightType.Directional;
        keyLight.color = new Color(1f, 0.96f, 0.9f);
        keyLight.intensity = 1.4f;
        keyLight.cullingMask = 1 << MapLayer;
        keyLight.shadows = LightShadows.None;

        ringMaterial = CreateUnlitMaterial(unlit, Color.white, true);
        gridMaterial = CreateUnlitMaterial(unlit, new Color(0.55f, 0.7f, 1f, 0.45f), true);
        routeMaterial = CreateUnlitMaterial(unlit, new Color(1f, 0.62f, 0.15f, 0.85f), true);

        // Soft radial glow textured onto a camera-facing quad per system.
        haloTexture = CreateHaloTexture();
        haloTexture.name = "GalaxyMap_Halo";
        haloTexture.wrapMode = TextureWrapMode.Clamp;
        haloTexture.filterMode = FilterMode.Bilinear;
        haloTexture.Apply(false, true);
        haloMaterial = CreateUnlitMaterial(unlit, Color.white, true);
        haloMaterial.name = "GalaxyMap_HaloMaterial";
        haloMaterial.mainTexture = haloTexture;
        haloMaterial.SetTexture("_BaseMap", haloTexture);
        haloMaterial.SetTexture("_MainTex", haloTexture);
        CreateGalaxyBackdrop(unlit);
    }

    private void CreateGalaxyBackdrop(Shader unlit)
    {
        const int armCount = 8;
        const int particlesPerArm = 3600;
        const int centralStarParticles = 1;
        const int coreParticles = 3500;
        const int fieldParticles = 1600;
        const int nebulaParticles = 3500;
        const int particleCount = centralStarParticles + coreParticles + armCount * particlesPerArm + fieldParticles + nebulaParticles;

        System.Random random = new System.Random(41729);
        ParticleSystem.Particle[] particles = new ParticleSystem.Particle[particleCount];
        int particleIndex = 0;

        SetGalaxyParticle(particles, particleIndex++, Vector3.zero, new Color(1f, 0.96f, 0.82f), 5f, 1f);

        // Dense, luminous core: tight and near-white so the blue arms read clearly.
        for (int i = 0; i < coreParticles; i++)
        {
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            float radius = Mathf.Pow((float)random.NextDouble(), 1.8f) * 9f;
            Vector3 position = new Vector3(Mathf.Cos(angle) * radius, RandomSigned(random) * 0.12f, Mathf.Sin(angle) * radius * 0.9f);
            Color color = Color.Lerp(new Color(1f, 0.93f, 0.78f), new Color(1f, 1f, 0.95f), (float)random.NextDouble());
            float alpha = 0.94f + (float)random.NextDouble() * 0.06f;
            float size = 0.24f + (float)random.NextDouble() * 0.16f;
            SetGalaxyParticle(particles, particleIndex++, position, color, size, alpha);
        }

        // Eight spiral arms with dust-like scatter.
        for (int i = 0; i < armCount * particlesPerArm; i++)
        {
            int arm = i % armCount;
            float radius = 1.5f + Mathf.Pow((float)random.NextDouble(), 0.78f) * 54f;
            float radialJitter = RandomSigned(random) * (0.75f + radius * 0.05f);
            float angle = arm * (Mathf.PI * 2f / armCount)
                + radius * 0.105f
                + RandomSigned(random) * (0.035f + radius * 0.001f);
            float scatteredRadius = Mathf.Max(0.5f, radius + radialJitter);
            Vector3 position = new Vector3(
                Mathf.Cos(angle) * scatteredRadius,
                RandomSigned(random) * (0.08f + radius * 0.002f),
                Mathf.Sin(angle) * scatteredRadius * 0.9f);
            float coolness = Mathf.InverseLerp(22f, 82f, radius);
            Color color = Color.Lerp(
                new Color(0.62f, 0.82f, 1f),
                new Color(0.2f, 0.45f, 1f),
                coolness);
            float alpha = 0.82f + (float)random.NextDouble() * 0.18f;
            float size = 0.14f + (float)random.NextDouble() * 0.1f;
            SetGalaxyParticle(particles, particleIndex++, position, color, size, alpha);
        }

        // A sparse field of blue-white stars outside the main arms.
        for (int i = 0; i < fieldParticles; i++)
        {
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            float radius = 22f + (float)random.NextDouble() * 40f;
            Vector3 position = new Vector3(
                Mathf.Cos(angle) * radius,
                RandomSigned(random) * 0.25f,
                Mathf.Sin(angle) * radius * 0.9f);
            Color color = Color.Lerp(
                new Color(0.6f, 0.8f, 1f),
                new Color(0.85f, 0.92f, 1f),
                (float)random.NextDouble());
            float alpha = 0.35f + (float)random.NextDouble() * 0.45f;
            float size = 0.045f + (float)random.NextDouble() * 0.06f;
            SetGalaxyParticle(particles, particleIndex++, position, color, size, alpha);
        }

        // Soft blue dust around each arm gives the star particles a nebula glow.
        for (int i = 0; i < nebulaParticles; i++)
        {
            int arm = i % armCount;
            float radius = 4f + Mathf.Pow((float)random.NextDouble(), 0.82f) * 50f;
            float spread = RandomSigned(random) * (0.9f + radius * 0.045f);
            float angle = arm * (Mathf.PI * 2f / armCount)
                + radius * 0.105f
                + RandomSigned(random) * (0.12f + radius * 0.002f);
            float scatteredRadius = Mathf.Max(1f, radius + spread);
            Vector3 position = new Vector3(
                Mathf.Cos(angle) * scatteredRadius,
                RandomSigned(random) * (0.12f + radius * 0.004f),
                Mathf.Sin(angle) * scatteredRadius * 0.9f);
            Color color = Color.Lerp(
                new Color(0.16f, 0.32f, 0.62f),
                new Color(0.42f, 0.58f, 0.82f),
                (float)random.NextDouble());
            float alpha = 0.08f + (float)random.NextDouble() * 0.07f;
            float size = 0.6f + (float)random.NextDouble() * 0.6f;
            SetGalaxyParticle(particles, particleIndex++, position, color, size, alpha);
        }

        galaxyTexture = CreateParticleTexture();
        galaxyTexture.name = "GalaxyMap_StarParticle";
        galaxyTexture.wrapMode = TextureWrapMode.Clamp;
        galaxyTexture.filterMode = FilterMode.Bilinear;
        galaxyTexture.Apply(false, true);

        Shader particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (particleShader == null) particleShader = Shader.Find("Particles/Standard Unlit");
        if (particleShader == null) particleShader = unlit;

        galaxyBackdropMaterial = CreateUnlitMaterial(particleShader, Color.white, true);
        galaxyBackdropMaterial.name = "GalaxyMap_SpiralParticles";
        galaxyBackdropMaterial.mainTexture = galaxyTexture;
        galaxyBackdropMaterial.SetTexture("_BaseMap", galaxyTexture);
        galaxyBackdropMaterial.SetTexture("_MainTex", galaxyTexture);
        galaxyBackdropMaterial.SetInt("_DstBlend", (int)BlendMode.One);
        galaxyBackdropMaterial.SetInt("_BlendOp", (int)BlendOp.Add);
        galaxyBackdropMaterial.EnableKeyword("_ALPHABLEND_ON");
        galaxyBackdropMaterial.renderQueue = (int)RenderQueue.Transparent;

        galaxyBackdrop = new GameObject("GalaxySpiralParticles");
        galaxyBackdrop.layer = MapLayer;
        galaxyBackdrop.transform.SetParent(worldRoot.transform, false);
        galaxyParticles = galaxyBackdrop.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = galaxyParticles.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = particleCount;
        main.startSpeed = 0f;
        main.startLifetime = 100000f;
        main.startSize = 1f;
        main.startColor = Color.white;
        main.gravityModifier = 0f;
        ParticleSystem.EmissionModule emission = galaxyParticles.emission;
        emission.enabled = false;

        ParticleSystemRenderer particleRenderer = galaxyBackdrop.GetComponent<ParticleSystemRenderer>();
        particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        particleRenderer.alignment = ParticleSystemRenderSpace.View;
        particleRenderer.sortMode = ParticleSystemSortMode.None;
        particleRenderer.sharedMaterial = galaxyBackdropMaterial;
        particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
        particleRenderer.receiveShadows = false;

        galaxyCore = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        galaxyCore.name = "GalaxyCore";
        galaxyCore.layer = MapLayer;
        galaxyCore.transform.SetParent(worldRoot.transform, false);
        galaxyCore.transform.localScale = Vector3.one * 3.2f;
        galaxyCoreMaterial = CreateUnlitMaterial(unlit, new Color(1f, 0.96f, 0.82f, 1f), false);
        galaxyCore.GetComponent<Renderer>().sharedMaterial = galaxyCoreMaterial;
        Collider coreCollider = galaxyCore.GetComponent<Collider>();
        if (coreCollider != null) Destroy(coreCollider);

        galaxyParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        galaxyParticles.SetParticles(particles, particleIndex);
        galaxyParticles.Play(false);
        UpdateGalaxyBackdropPosition();
    }

    private static void SetGalaxyParticle(ParticleSystem.Particle[] particles, int index, Vector3 position, Color color, float size, float alpha)
    {
        ParticleSystem.Particle particle = new ParticleSystem.Particle();
        particle.position = position;
        particle.startColor = new Color(color.r, color.g, color.b, alpha);
        particle.startSize = size;
        particle.startLifetime = 100000f;
        particle.remainingLifetime = 100000f;
        particle.velocity = Vector3.zero;
        particles[index] = particle;
    }

    private static float RandomSigned(System.Random random)
    {
        return (float)random.NextDouble() * 2f - 1f;
    }

    private static Texture2D CreateHaloTexture()
    {
        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - (size - 1) * 0.5f) / (size * 0.5f);
                float dy = (y - (size - 1) * 0.5f) / (size * 0.5f);
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float core = Mathf.Exp(-r * r * 18f);
                float glow = Mathf.Exp(-r * r * 4.5f) * 0.55f;
                float alpha = Mathf.Clamp01(core + glow) * Mathf.Clamp01(1.15f - r);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        return texture;
    }

    private static Texture2D CreateParticleTexture()
    {
        const int size = 32;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - (size - 1) * 0.5f) / (size * 0.5f);
                float dy = (y - (size - 1) * 0.5f) / (size * 0.5f);
                float radiusSquared = dx * dx + dy * dy;
                float halo = Mathf.Exp(-radiusSquared * 2.6f) * 0.72f;
                float core = Mathf.Exp(-radiusSquared * 14f) * 0.4f;
                float alpha = Mathf.Clamp01((halo + core) * Mathf.Clamp01(1.25f - radiusSquared));
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        return texture;
    }

    private void UpdateGalaxyBackdropPosition()
    {
        if (galaxyBackdrop != null)
        {
            galaxyBackdrop.transform.position = new Vector3(galaxyCenter.x, galaxyPlaneY, galaxyCenter.z);
        }

        if (galaxyCore != null)
        {
            galaxyCore.transform.position = new Vector3(galaxyCenter.x, galaxyPlaneY + 0.3f, galaxyCenter.z);
        }
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
            // Opaque background: additive particles composite directly in the
            // RenderTexture. With a transparent clear color the accumulated
            // particle alpha darkens the quad edges when the RawImage blends
            // over the panel ("black square" artifact).
            mapCamera.backgroundColor = new Color(0.012f, 0.035f, 0.075f, 1f);
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
        // Flat map: every system shares the same Y so nothing floats above/below.
        return new Vector3(system.X * CoordinateScale, PlanetsY, system.Y * CoordinateScale);
    }

    private void CreateMarker(StarSystem system)
    {
        Vector3 position = ToMapPosition(system);
        GameObject markerGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        markerGO.name = "System_" + system.Planet;
        markerGO.layer = MapLayer;
        markerGO.transform.SetParent(markersRoot, true);
        markerGO.transform.position = position;
        markerGO.transform.localScale = Vector3.one * planetSize;

        Renderer renderer = markerGO.GetComponent<Renderer>();
        renderer.sharedMaterial = markerMaterial;
        GalaxyMap3DSystemMarker pickMarker = markerGO.AddComponent<GalaxyMap3DSystemMarker>();
        pickMarker.planetName = system.Planet;

        SphereCollider collider = markerGO.GetComponent<SphereCollider>();
        collider.radius = MarkerColliderRadius;
        collider.isTrigger = true;

        // Soft glow disc behind the core. Kept as a sibling (not a child) so
        // hover/selection scaling of the core never stretches the halo.
        GameObject haloGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
        haloGO.name = "Halo_" + system.Planet;
        haloGO.layer = MapLayer;
        haloGO.transform.SetParent(markersRoot, true);
        haloGO.transform.position = position;
        haloGO.transform.localScale = Vector3.one * haloSize;
        Collider haloCollider = haloGO.GetComponent<Collider>();

        if (haloCollider != null)
        {
            Destroy(haloCollider);
        }

        Renderer haloRenderer = haloGO.GetComponent<Renderer>();
        haloRenderer.sharedMaterial = haloMaterial;

        GameObject ringGO = new GameObject("HoverRing");
        ringGO.layer = MapLayer;
        ringGO.transform.SetParent(markerGO.transform, false);
        LineRenderer ring = ringGO.AddComponent<LineRenderer>();
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.positionCount = 48;
        ring.startWidth = 0.04f;
        ring.endWidth = 0.04f;
        ring.sharedMaterial = ringMaterial;
        ring.startColor = ringMaterial.color;
        ring.endColor = ringMaterial.color;
        ring.enabled = false;

        float radius = MarkerRingRadius;

        for (int i = 0; i < ring.positionCount; i++)
        {
            float angle = (Mathf.PI * 2f * i) / ring.positionCount;
            ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }

        Text label = CreateProjectedLabel(system.Planet, font, labelFontSize);
        Marker marker = new Marker
        {
            system = system,
            position = position,
            gameObject = markerGO,
            renderer = renderer,
            halo = haloGO,
            haloRenderer = haloRenderer,
            hoverRing = ring,
            label = label
        };

        markers.Add(marker);
    }

    private Text CreateProjectedLabel(string value, Font labelFont, int size, float width = 140f)
    {
        GameObject labelGO = new GameObject("MapLabel_" + value);
        labelGO.transform.SetParent(viewport, false);
        RectTransform rect = labelGO.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, 20f);

        Text text = labelGO.AddComponent<Text>();
        text.font = labelFont;
        text.fontSize = size;
        text.text = value;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = new Color(0.88f, 0.92f, 1f, 0.95f);
        text.raycastTarget = false;

        return text;
    }

    private void CreateGrid()
    {
        ClearGrid();

        float planeY = viewTarget.y + GridYOffset;
        Color color = new Color(0.65f, 0.78f, 1f, 0.55f);

        for (int column = 0; column <= 19; column++)
        {
            float x = (GridOriginX + GridCellSize * column) * CoordinateScale;
            CreateGridLine(new Vector3(x, planeY, GridTopY * CoordinateScale), new Vector3(x, planeY, (GridTopY - 21f * GridCellSize) * CoordinateScale), color);
        }

        for (int row = 0; row <= 21; row++)
        {
            float z = (GridTopY - GridCellSize * row) * CoordinateScale;
            CreateGridLine(new Vector3(GridOriginX * CoordinateScale, planeY, z), new Vector3((GridOriginX + 19f * GridCellSize) * CoordinateScale, planeY, z), color);
        }

        for (int column = 0; column < 19; column++)
        {
            float x = (GridOriginX + GridCellSize * (column + 0.5f)) * CoordinateScale;
            ProjectedLabel label = new ProjectedLabel
            {
                position = new Vector3(x, planeY, (GridTopY - GridCellSize * 0.5f) * CoordinateScale),
                text = CreateProjectedLabel(((char)('C' + column)).ToString(), font, 12, 40f)
            };
            label.text.color = new Color(1f, 1f, 1f, 0.9f);
            gridLabels.Add(label);
        }

        for (int row = 1; row <= 21; row++)
        {
            float z = (GridTopY - GridCellSize * (row - 0.5f)) * CoordinateScale;
            ProjectedLabel label = new ProjectedLabel
            {
                position = new Vector3((GridOriginX + GridCellSize * 0.5f) * CoordinateScale, planeY, z),
                text = CreateProjectedLabel(row.ToString(), font, 14, 40f),
                gridRow = row
            };
            label.text.color = Color.white;
            label.text.fontStyle = FontStyle.Bold;
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
        line.startWidth = 0.045f;
        line.endWidth = 0.045f;
        line.sharedMaterial = gridMaterial;
        line.startColor = color;
        line.endColor = color;
    }

    private void ClearMarkers()
    {
        foreach (Marker marker in markers)
        {
            if (marker.gameObject != null) Destroy(marker.gameObject);
            if (marker.halo != null) Destroy(marker.halo);
            if (marker.label != null) Destroy(marker.label.gameObject);
        }

        markers.Clear();
        hoveredMarker = null;
        ClearRouteLines();
    }

    // Tints a lit (URP Lit / Standard) material: base color plus a proportional
    // emission lift so the shaded side of the sphere never goes full black.
    private static void SetLitBaseColor(Renderer renderer, Color color, float emissionBoost)
    {
        Material material = renderer.material;

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        else
        {
            material.SetColor("_Color", color);
        }

        if (material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * emissionBoost);
        }
    }

    private void CreateRouteLines()
    {
        ClearRouteLines();

        if (routesRoot == null || GalaxyRoutes.Routes == null)
        {
            return;
        }

        // Corellian Run gold (per-route colors can be added as more routes land).
        Color routeColor = new Color(1f, 0.62f, 0.15f, 0.85f);

        foreach (GalaxyRoutes.Hyperroute route in GalaxyRoutes.Routes)
        {
            if (route == null || route.SystemsInOrder == null || route.SystemsInOrder.Length < 2)
            {
                continue;
            }

            for (int i = 0; i < route.SystemsInOrder.Length - 1; i++)
            {
                Marker a = markers.Find(marker => marker.system.Planet == route.SystemsInOrder[i]);
                Marker b = markers.Find(marker => marker.system.Planet == route.SystemsInOrder[i + 1]);

                // Only draw legs where both stops are on the current map.
                if (a == null || b == null)
                {
                    continue;
                }

                CreateRouteLine(a.position, b.position, routeColor);
            }
        }
    }

    private void CreateRouteLine(Vector3 start, Vector3 end, Color color)
    {
        GameObject lineGO = new GameObject("RouteLine");
        lineGO.layer = MapLayer;
        lineGO.transform.SetParent(routesRoot, false);
        LineRenderer line = lineGO.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.SetPosition(0, start);
        line.SetPosition(1, end);
        line.startWidth = 0.09f;
        line.endWidth = 0.09f;
        line.sharedMaterial = routeMaterial;
        line.startColor = color;
        line.endColor = color;
        routeLines.Add(lineGO);
    }

    private void ClearRouteLines()
    {
        foreach (GameObject line in routeLines)
        {
            if (line != null) Destroy(line);
        }

        routeLines.Clear();
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

    private float GetMarkerScaleMultiplier(Marker marker)
    {
        if (marker == hoveredMarker)
        {
            return 1.5f;
        }

        return marker.system.Planet == selectedSystem ? 1.3f : 1f;
    }

    private void UpdateMarkerStates()
    {        foreach (Marker marker in markers)
        {
            bool current = marker.system.Planet == currentSystem;
            bool selected = marker.system.Planet == selectedSystem;
            bool hovered = marker == hoveredMarker;

            Color color = current
                ? new Color(0.25f, 1f, 0.5f, 1f)
                : selected ? new Color(1f, 0.72f, 0.2f, 1f) : new Color(0.82f, 0.9f, 1f, 1f);

            Color haloColor = current
                ? new Color(0.25f, 0.85f, 0.45f, 0.65f)
                : selected || hovered ? new Color(1f, 0.6f, 0.2f, 0.7f) : new Color(0.62f, 0.16f, 0.13f, 0.55f);

            SetLitBaseColor(marker.renderer, color, 0.35f);

            if (marker.haloRenderer != null)
            {
                marker.haloRenderer.material.color = haloColor;
            }

            marker.gameObject.transform.localScale = Vector3.one * planetSize * GetMarkerScaleMultiplier(marker);
            Color ringColor = hovered || selected
                ? new Color(1f, 0.78f, 0.3f, 0.95f)
                : current ? new Color(0.3f, 1f, 0.58f, 0.9f) : new Color(0.25f, 0.62f, 1f, 0.62f);
            marker.hoverRing.startColor = ringColor;
            marker.hoverRing.endColor = ringColor;
            marker.hoverRing.enabled = true;

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

        // Planet names only show when zoomed in: the overview stays clean and
        // the markers can stay small without overlapping text.
        bool showPlanetLabels = namesVisible && cameraDistance <= labelsMaxDistance;

        foreach (Marker marker in markers)
        {
            if (marker.label != null)
            {
                marker.label.fontSize = labelFontSize;
            }

            UpdateLabelPosition(marker.position + Vector3.down * labelHeight, marker.label, rect, occupied, showPlanetLabels);
        }

        foreach (ProjectedLabel label in gridLabels)
        {
            Vector3 labelPosition = label.gridRow > 0
                ? FindLeftmostVisibleCellCenter(label.gridRow, label.position.y)
                : label.position;
            UpdateLabelPosition(labelPosition, label.text, rect, occupied, gridVisible, label.pinToLeftEdge);
        }
    }

    // Keep row numbers inside the leftmost grid cell that is actually visible
    // after the user pans/zooms the 3D map. Column C may be outside the view.
    private Vector3 FindLeftmostVisibleCellCenter(int row, float planeY)
    {
        float rowZ = (GridTopY - GridCellSize * (row - 0.5f)) * CoordinateScale;
        Vector3 fallback = new Vector3((GridOriginX + GridCellSize * 0.5f) * CoordinateScale, planeY, rowZ);
        float leftmostViewportX = float.MaxValue;
        Vector3 best = fallback;

        for (int column = 0; column < 19; column++)
        {
            float cellX = (GridOriginX + GridCellSize * (column + 0.5f)) * CoordinateScale;
            Vector3 candidate = new Vector3(cellX, planeY, rowZ);
            Vector3 projected = mapCamera.WorldToViewportPoint(candidate);

            if (projected.z <= 0f || projected.x < 0.02f || projected.x > 0.98f || projected.y < 0.02f || projected.y > 0.98f)
            {
                continue;
            }

            if (projected.x < leftmostViewportX)
            {
                leftmostViewportX = projected.x;
                best = candidate;
            }
        }

        return best;
    }

    private void UpdateLabelPosition(Vector3 worldPosition, Text label, Rect rect, List<Rect> occupied, bool visible, bool pinToLeftEdge = false)
    {
        if (label == null) return;

        Vector3 projected = mapCamera.WorldToViewportPoint(worldPosition);
        bool horizontalVisible = projected.x >= 0f && projected.x <= 1f;
        bool onScreen = projected.z > 0f && (horizontalVisible || pinToLeftEdge) && projected.y >= 0f && projected.y <= 1f;

        if (!visible || !onScreen)
        {
            label.gameObject.SetActive(false);
            return;
        }

        label.gameObject.SetActive(true);
        RectTransform labelRect = label.rectTransform;
        float x = pinToLeftEdge
            ? rect.xMin + labelRect.sizeDelta.x * 0.5f + 8f
            : (projected.x - 0.5f) * rect.width;
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
        lastPointer = pointer;

        ApplyArrowKeysPan();

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

    private void ApplyArrowKeysPan()
    {
        float x = 0f;
        float y = 0f;

        if (Input.GetKey(KeyCode.LeftArrow)) x -= 1f;
        if (Input.GetKey(KeyCode.RightArrow)) x += 1f;
        if (Input.GetKey(KeyCode.UpArrow)) y += 1f;
        if (Input.GetKey(KeyCode.DownArrow)) y -= 1f;

        if (x != 0f || y != 0f)
        {
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            float speed = cameraDistance * 0.9f * Time.unscaledDeltaTime;
            viewTarget += rotation * Vector3.right * x * speed;

            // Up/down pan across the map plane toward the top/bottom of the screen,
            // keeping the same height (same as left/right, but on the other 2D axis).
            Vector3 forward = rotation * Vector3.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude > 0.0001f)
            {
                viewTarget += forward.normalized * y * speed;
            }
        }
    }

    private bool PointerOverUIButton(Vector2 screenPosition)
    {
        if (EventSystem.current == null) return false;

        PointerEventData pointer = new PointerEventData(EventSystem.current);
        pointer.position = screenPosition;
        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, results);

        Transform panel = viewport != null ? viewport.parent : null;
        Transform dialog = panel != null ? panel.Find("LayersDialog") : null;
        Transform selectionBar = panel != null ? panel.Find("SelectionBar") : null;

        foreach (RaycastResult result in results)
        {
            if (result.gameObject == null) continue;

            if (result.gameObject.GetComponentInParent<Button>() != null)
            {
                return true;
            }

            Transform hit = result.gameObject.transform;

            // Clicking on dialog or selection-bar chrome must not pick a planet behind it.
            if (dialog != null && (hit == dialog || hit.IsChildOf(dialog)))
            {
                return true;
            }

            if (selectionBar != null && (hit == selectionBar || hit.IsChildOf(selectionBar)))
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
