#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// Editor-only preview: reads a planet from StarSystems.json and builds a small
// terrain patch with the exact same generator, material, cliff maps, detail
// maps and procedural overlays used by TerrainStreamer at runtime.
public class PlanetTerrainPreviewWindow : EditorWindow
{
    private const string MenuPath = "Tools/Open Galaxy/Planet Terrain Preview";
    private const string RootNamePrefix = "PlanetTerrainPreview_";
    private const string SelectedPlanetKey = "OpenGalaxy.TerrainPreview.SelectedPlanet";
    private const float PreviewTileSize = 1000f;
    private const double LiveRefreshDelaySeconds = 0.4;

    private static readonly BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [SerializeField] private string selectedPlanet = "";
    [SerializeField] private string searchText = "";
    [SerializeField] private int gridSize = 2;
    [SerializeField] private int centerTileX;
    [SerializeField] private int centerTileZ;
    [SerializeField] private bool showOverlays = true;
    [SerializeField] private bool showDetail = true;
    [SerializeField] private bool showVegetation = true;
    [SerializeField] private bool showProps = true;
    [SerializeField] private bool showCreatures = true;
    [SerializeField] private bool showPoiMarkers = true;
    [SerializeField] private bool showCalculatedRoute = true;
    [SerializeField] private bool poseCreatures = true;
    [SerializeField] private bool regenerateAfterApply = true;
    [SerializeField] private bool liveUpdate = true;
    [SerializeField] private bool liveRefreshPending;
    [SerializeField] private double liveRefreshTime;
    [SerializeField] private string lastPreviewSignature = "";
    [SerializeField] private Vector2 panelScroll = Vector2.zero;

    // Editable copy of the selected planet. The preview uses these values;
    // "Aplicar a JSON" writes them back to StarSystems.json.
    [SerializeField] private string draftPlanet = "";
    [SerializeField] private string draftPlanetType = "";
    [SerializeField] private int draftSeed = 1138;
    [SerializeField] private string draftTerrainTexture = "";
    [SerializeField] private string draftDetailTexture = "";
    [SerializeField] private bool draftHasCliffs;
    [SerializeField] private bool draftCliffsFromJson;
    [SerializeField] private CliffSettings draftCliffs = new CliffSettings();
    [SerializeField] private TerrainOverlay[] draftOverlays = new TerrainOverlay[0];
    [SerializeField] private VegetationSettings[] draftVegetation = new VegetationSettings[0];

    // Editable copy of the planet's PlanetPOIs.json entry (POIs, isolated
    // props, settlements and creature herds). The preview instantiates the
    // real prefabs on the generated terrain; "Guardar en PlanetPOIs.json"
    // writes this block back.
    [SerializeField] private PlanetPOILocation draftLocation = new PlanetPOILocation();
    [SerializeField] private bool draftHasLocation;
    [SerializeField] private string draftLocationPlanet = "";

    [SerializeField] private bool configFoldout = true;
    [SerializeField] private bool generalFoldout = true;
    [SerializeField] private bool cliffsFoldout;
    [SerializeField] private bool overlaysFoldout = true;
    [SerializeField] private bool vegetationFoldout = true;
    [SerializeField] private bool[] vegetationFoldouts = new bool[8];
    [SerializeField] private bool[] overlayFoldouts = new bool[3];
    [SerializeField] private bool[] overlayAdvancedFoldouts = new bool[3];
    [SerializeField] private bool propsFoldout = true;
    [SerializeField] private bool poisFoldout = true;
    [SerializeField] private bool[] poiFoldouts = new bool[4];
    [SerializeField] private bool[] propFoldouts = new bool[8];
    [SerializeField] private bool[] siteFoldouts = new bool[8];
    [SerializeField] private bool[] siteRuleFoldouts = new bool[8];
    [SerializeField] private bool[] creatureFoldouts = new bool[8];

    private static readonly string[] OverlayModes = { "patch", "cliffFoot", "path", "poiPath" };
    private static readonly string[] OverlayCurvePresets = { "straight", "balanced", "winding" };
    private static readonly string[] OverlayCurvePresetLabels = { "Recto", "Equilibrado", "Serpenteante" };

    private StarSystems starSystems;
    private PlanetPOIs planetPoiDefs;
    private PropTypes propTypes;
    private string[] propPrefabOptions = new string[0];
    private string[] creaturePrefabOptions = new string[0];
    private string[] vegetationPrefabOptions = new string[0];
    private string[] vegetationMaterialOptions = new string[0];
    private bool liveRefreshCallbackRegistered;
    private bool sceneGuiCallbackRegistered;
    private string[] allPlanets = new string[0];
    private string[] filteredPlanets = new string[0];
    private GameObject previewRoot;
    private Transform previewVegetationViewer;
    private TerrainGenerator previewGenerator;
    private int previewPropsInside;
    private int previewPropsOutside;
    private int previewPropsRelaxed;
    private int previewCreaturesInside;
    private int previewCreaturesOutside;
    private bool previewHasPoiContent;
    private readonly List<Mesh> generatedMeshes = new List<Mesh>();
    private readonly List<Material> generatedMaterials = new List<Material>();

    private string statusMessage = "Selecciona un planeta y pulsa Generar vista previa.";
    private MessageType statusType = MessageType.Info;
    private bool dataLoaded;

    [UnityEditor.MenuItem(MenuPath)]
    private static void ShowWindow()
    {
        PlanetTerrainPreviewWindow window =
            GetWindow<PlanetTerrainPreviewWindow>("Planet Terrain Preview");
        window.minSize = new Vector2(430f, 460f);
    }

    private void OnEnable()
    {
        selectedPlanet = SessionState.GetString(SelectedPlanetKey, selectedPlanet);
        LoadData();

        if (!liveRefreshCallbackRegistered)
        {
            EditorApplication.update += HandleLiveRefresh;
            liveRefreshCallbackRegistered = true;
        }

        if (!sceneGuiCallbackRegistered)
        {
            SceneView.duringSceneGui += HandleSceneGui;
            sceneGuiCallbackRegistered = true;
        }
    }

    private void OnDisable()
    {
        SessionState.SetString(SelectedPlanetKey, selectedPlanet ?? "");

        if (liveRefreshCallbackRegistered)
        {
            EditorApplication.update -= HandleLiveRefresh;
            liveRefreshCallbackRegistered = false;
        }

        if (sceneGuiCallbackRegistered)
        {
            SceneView.duringSceneGui -= HandleSceneGui;
            sceneGuiCallbackRegistered = false;
        }
    }

    private void OnDestroy()
    {
        CleanupPreview();
    }

    private void LoadData()
    {
        TextAsset asset = Resources.Load<TextAsset>(OGGetAddress.files + "StarSystems");

        if (asset == null)
        {
            dataLoaded = false;
            allPlanets = new string[0];
            filteredPlanets = new string[0];
            SetStatus("No se encontró Assets/Resources/files/StarSystems.json.", MessageType.Error);
            return;
        }

        starSystems = JsonUtility.FromJson<StarSystems>(asset.text);

        if (starSystems == null || starSystems.starSystemsData == null)
        {
            dataLoaded = false;
            allPlanets = new string[0];
            filteredPlanets = new string[0];
            SetStatus("StarSystems.json no se pudo parsear.", MessageType.Error);
            return;
        }

        allPlanets = starSystems.starSystemsData
            .Where(s => s != null && !string.IsNullOrEmpty(s.Planet))
            .Select(s => s.Planet)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        dataLoaded = allPlanets.Length > 0;

        if (string.IsNullOrEmpty(selectedPlanet)
            || !allPlanets.Any(name => string.Equals(name, selectedPlanet, StringComparison.OrdinalIgnoreCase)))
        {
            selectedPlanet = allPlanets.FirstOrDefault(
                name => string.Equals(name, "Tatooine", StringComparison.OrdinalIgnoreCase))
                ?? (allPlanets.Length > 0 ? allPlanets[0] : "");
        }

        RebuildFilteredPlanets();
        LoadPoiData();
        SyncDraftIfNeeded(true);
        lastPreviewSignature = ComputePreviewSignature();
        SetStatus("StarSystems.json cargado. " + allPlanets.Length + " planetas disponibles.", MessageType.Info);
    }

    private void LoadPoiData()
    {
        TextAsset poiAsset = Resources.Load<TextAsset>(OGGetAddress.files + "PlanetPOIs");
        planetPoiDefs = poiAsset != null ? JsonUtility.FromJson<PlanetPOIs>(poiAsset.text) : null;

        TextAsset propAsset = Resources.Load<TextAsset>(OGGetAddress.files + "PropsTypes");
        propTypes = propAsset != null ? JsonUtility.FromJson<PropTypes>(propAsset.text) : null;

        propPrefabOptions = propTypes != null && propTypes.propTypesData != null
            ? propTypes.propTypesData
                .Where(def => def != null
                    && (!string.IsNullOrEmpty(def.type) || !string.IsNullOrEmpty(def.prefab)))
                .Select(def => string.IsNullOrEmpty(def.type) ? def.prefab : def.type)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : new string[0];

        creaturePrefabOptions = Resources.LoadAll<GameObject>("objects/creatures")
            .Where(prefab => prefab != null)
            .Select(prefab => prefab.name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Vegetation can be trees/bushes or any prop-like model (rocks, stumps,
        // debris...). Offer everything that lives in the usual object folders.
        string[] vegetationFolders = { "objects/trees/", "objects/props/" };
        vegetationPrefabOptions = vegetationFolders
            .SelectMany(folder => Resources.LoadAll<GameObject>(folder))
            .Where(prefab => prefab != null)
            .Select(prefab => prefab.name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        vegetationMaterialOptions = vegetationFolders
            .SelectMany(folder => Resources.LoadAll<Material>(folder))
            .Where(material => material != null)
            .Select(material => material.name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private PlanetPOILocation FindSelectedLocation()
    {
        if (planetPoiDefs == null || planetPoiDefs.planetPOIsData == null)
            return null;

        foreach (PlanetPOILocation location in planetPoiDefs.planetPOIsData)
        {
            if (location != null
                && string.Equals(location.planet, selectedPlanet, StringComparison.OrdinalIgnoreCase))
            {
                return location;
            }
        }

        return null;
    }

    private void RebuildFilteredPlanets()
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            filteredPlanets = allPlanets;
            return;
        }

        string filter = searchText.Trim();
        filteredPlanets = allPlanets
            .Where(name => name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToArray();
    }

    private StarSystem FindSelectedSystem()
    {
        if (starSystems == null || starSystems.starSystemsData == null)
            return null;

        foreach (StarSystem system in starSystems.starSystemsData)
        {
            if (system != null && string.Equals(system.Planet, selectedPlanet, StringComparison.OrdinalIgnoreCase))
                return system;
        }

        return null;
    }

    private void OnGUI()
    {
        SyncDraftIfNeeded();

        panelScroll = EditorGUILayout.BeginScrollView(
            panelScroll, false, true,
            GUI.skin.horizontalScrollbar,
            GUI.skin.verticalScrollbar,
            GUI.skin.scrollView,
            GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));

        EditorGUILayout.LabelField("Planet Terrain Preview", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Edita los parámetros del planeta y la vista previa se actualizará sola. " +
            "'Aplicar a JSON' sobrescribe la configuración en StarSystems.json, incluida la vegetación.",
            MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        string newSearch = EditorGUILayout.TextField("Buscar", searchText);
        if (newSearch != searchText)
        {
            searchText = newSearch;
            RebuildFilteredPlanets();
        }

        if (GUILayout.Button("Recargar JSON", GUILayout.Width(110f)))
            LoadData();
        EditorGUILayout.EndHorizontal();

        if (filteredPlanets.Length > 0)
        {
            int index = Array.FindIndex(filteredPlanets,
                name => string.Equals(name, selectedPlanet, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                index = 0;

            int newIndex = EditorGUILayout.Popup("Planeta", index, filteredPlanets);
            if (newIndex != index)
            {
                selectedPlanet = filteredPlanets[newIndex];
                SyncDraftIfNeeded(true);
            }
        }
        else
        {
            EditorGUILayout.HelpBox("No hay planetas que coincidan con la búsqueda.", MessageType.Warning);
        }

        StarSystem system = FindSelectedSystem();
        bool hasPlanet = dataLoaded && system != null;

        EditorGUILayout.Space(4f);
        configFoldout = EditorGUILayout.Foldout(
            configFoldout, "Configuración editable (se guarda en StarSystems.json)", true,
            EditorStyles.foldoutHeader);

        if (configFoldout)
        {
            EditorGUI.BeginDisabledGroup(!hasPlanet);
            DrawGeneralConfig();
            DrawCliffConfig();
            DrawOverlayConfig();
            DrawVegetationConfig();
            DrawPoiConfig();
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(4f);
            EditorGUI.BeginDisabledGroup(!hasPlanet);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Restablecer desde JSON"))
            {
                SyncDraftIfNeeded(true);
                SetStatus("Parámetros de terreno y POIs restablecidos desde los JSON.", MessageType.Info);
            }

            if (GUILayout.Button("Aplicar a JSON", GUILayout.Height(28f)))
                ApplyConfigToJson();
            EditorGUILayout.EndHorizontal();

            regenerateAfterApply = EditorGUILayout.Toggle(
                "Regenerar vista previa al aplicar", regenerateAfterApply);
            EditorGUI.EndDisabledGroup();
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Vista previa", EditorStyles.boldLabel);
        gridSize = EditorGUILayout.IntSlider("Tiles por lado", gridSize, 1, 6);
        centerTileX = EditorGUILayout.IntField("Centro tile X", centerTileX);
        centerTileZ = EditorGUILayout.IntField("Centro tile Z", centerTileZ);
        if (GUILayout.Button("Ajustar rejilla a POIs, props y asentamientos"))
            FrameGridToPoiContent();
        showOverlays = EditorGUILayout.Toggle("Overlays (caminos/arena)", showOverlays);
        showDetail = EditorGUILayout.Toggle("Detalle cercano", showDetail);
        showVegetation = EditorGUILayout.Toggle("Vegetación (bloque vegetation)", showVegetation);
        showProps = EditorGUILayout.Toggle("Props y asentamientos", showProps);
        showCreatures = EditorGUILayout.Toggle("Criaturas (rebaños)", showCreatures);
        showPoiMarkers = EditorGUILayout.Toggle("Marcar POIs y radios", showPoiMarkers);
        showCalculatedRoute = EditorGUILayout.Toggle("Mostrar ruta calculada", showCalculatedRoute);
        poseCreatures = EditorGUILayout.Toggle("Posar criaturas (animación)", poseCreatures);

        EditorGUI.BeginChangeCheck();
        liveUpdate = EditorGUILayout.Toggle("Actualización en vivo", liveUpdate);
        if (EditorGUI.EndChangeCheck())
        {
            if (liveUpdate)
                ScheduleLiveRefresh();
            else
                CancelLiveRefresh();
        }

        EditorGUILayout.Space(6f);
        EditorGUI.BeginDisabledGroup(!hasPlanet);
        if (GUILayout.Button("Generar vista previa", GUILayout.Height(30f)))
            GeneratePreview(null);
        EditorGUI.EndDisabledGroup();

        if (GUILayout.Button("Limpiar vista previa"))
            CleanupPreview();

        EditorGUILayout.Space(6f);
        EditorGUILayout.HelpBox(statusMessage, statusType);

        EditorGUILayout.EndScrollView();

        TrackLivePreviewChanges();
    }

    private void DrawGeneralConfig()
    {
        generalFoldout = EditorGUILayout.Foldout(generalFoldout, "General", true);
        if (!generalFoldout)
            return;

        draftSeed = EditorGUILayout.IntField("Seed", draftSeed);
        draftPlanetType = EditorGUILayout.TextField("Planet type", draftPlanetType ?? "");
        draftTerrainTexture = EditorGUILayout.TextField("Texturas base", draftTerrainTexture ?? "");
        draftDetailTexture = EditorGUILayout.TextField("Textura detalle", draftDetailTexture ?? "");
        EditorGUILayout.HelpBox(
            "Texturas base admite palabras clave separadas por comas. Textura detalle usa <nombre>_d/_n/_h/_ao.",
            MessageType.None);
    }

    private void DrawCliffConfig()
    {
        cliffsFoldout = EditorGUILayout.Foldout(cliffsFoldout, "Acantilados / mesas / cañones", true);
        if (!cliffsFoldout)
            return;

        bool hasCliffs = EditorGUILayout.Toggle("Editar bloque de cliffs", draftHasCliffs);
        if (hasCliffs != draftHasCliffs)
        {
            draftHasCliffs = hasCliffs;
            if (draftHasCliffs && draftCliffs == null)
                draftCliffs = new CliffSettings();

            if (!draftHasCliffs && draftCliffsFromJson && draftCliffs != null)
                draftCliffs.enabled = false;
        }

        if (!draftHasCliffs || draftCliffs == null)
        {
            EditorGUILayout.HelpBox(
                draftCliffsFromJson
                    ? "El bloque existente se guardará con enabled = false."
                    : "Este planeta no tiene bloque de cliffs. Actívalo para añadirlo.",
                MessageType.None);
            return;
        }

        EditorGUI.indentLevel++;
        draftCliffs.enabled = EditorGUILayout.Toggle("Enabled", draftCliffs.enabled);
        draftCliffs.mesaHeight = EditorGUILayout.FloatField("Mesa height", draftCliffs.mesaHeight);
        draftCliffs.mesaRoughness = EditorGUILayout.FloatField("Mesa roughness", draftCliffs.mesaRoughness);
        draftCliffs.edgeWidth = EditorGUILayout.FloatField("Edge width", draftCliffs.edgeWidth);
        draftCliffs.canyonDepth = EditorGUILayout.FloatField("Canyon depth", draftCliffs.canyonDepth);
        draftCliffs.canyonWidth = EditorGUILayout.FloatField("Canyon width", draftCliffs.canyonWidth);
        draftCliffs.strength = EditorGUILayout.FloatField("Strength", draftCliffs.strength);
        draftCliffs.coverage = EditorGUILayout.FloatField("Coverage", draftCliffs.coverage);
        draftCliffs.regionFrequency = EditorGUILayout.FloatField("Region frequency", draftCliffs.regionFrequency);
        draftCliffs.regionFeather = EditorGUILayout.FloatField("Region feather", draftCliffs.regionFeather);
        draftCliffs.maskThreshold = EditorGUILayout.FloatField("Mask threshold", draftCliffs.maskThreshold);
        draftCliffs.maskFrequency = EditorGUILayout.FloatField("Mask frequency", draftCliffs.maskFrequency);
        draftCliffs.slopeStart = EditorGUILayout.FloatField("Slope start", draftCliffs.slopeStart);
        draftCliffs.slopeSoftness = EditorGUILayout.FloatField("Slope softness", draftCliffs.slopeSoftness);
        draftCliffs.textureType = EditorGUILayout.TextField("Texture type", draftCliffs.textureType ?? "");
        draftCliffs.verticesPerSide = EditorGUILayout.IntField("Vertices per side", draftCliffs.verticesPerSide);
        draftCliffs.outerVerticesPerSide = EditorGUILayout.IntField("Outer vertices per side", draftCliffs.outerVerticesPerSide);
        draftCliffs.highDetailRadiusKm = EditorGUILayout.FloatField("High detail radius km", draftCliffs.highDetailRadiusKm);
        EditorGUI.indentLevel--;
    }

    private void DrawOverlayConfig()
    {
        overlaysFoldout = EditorGUILayout.Foldout(overlaysFoldout, "Overlays (máx. 3)", true);
        if (!overlaysFoldout)
            return;

        if (draftOverlays == null)
            draftOverlays = new TerrainOverlay[0];

        for (int i = 0; i < draftOverlays.Length; i++)
        {
            TerrainOverlay overlay = draftOverlays[i];
            if (overlay == null)
                continue;

            if (i >= overlayFoldouts.Length)
                Array.Resize(ref overlayFoldouts, i + 1);

            string label = string.IsNullOrWhiteSpace(overlay.texture)
                ? "Overlay " + (i + 1) + " (sin textura)"
                : "Overlay " + (i + 1) + ": " + overlay.texture + " [" + overlay.mode + "]";

            EditorGUILayout.BeginHorizontal();
            overlayFoldouts[i] = EditorGUILayout.Foldout(overlayFoldouts[i], label, true);
            if (GUILayout.Button("Quitar", GUILayout.Width(60f)))
            {
                RemoveOverlayAt(i);
                EditorGUILayout.EndHorizontal();
                break;
            }
            EditorGUILayout.EndHorizontal();

            if (!overlayFoldouts[i])
                continue;

            EditorGUI.indentLevel++;
            overlay.enabled = EditorGUILayout.Toggle("Enabled", overlay.enabled);
            overlay.label = EditorGUILayout.TextField("Label", overlay.label ?? "");
            overlay.texture = EditorGUILayout.TextField("Texture", overlay.texture ?? "");

            int modeIndex = ModeToIndex(overlay.mode);
            int newModeIndex = EditorGUILayout.Popup("Mode", modeIndex, OverlayModes);
            if (newModeIndex != modeIndex)
                overlay.mode = OverlayModes[newModeIndex];

            overlay.tiling = EditorGUILayout.FloatField("Tiling", overlay.tiling);
            overlay.strength = EditorGUILayout.FloatField("Strength", overlay.strength);
            overlay.width = EditorGUILayout.FloatField("Ancho (m)", overlay.width);
            overlay.feather = EditorGUILayout.FloatField("Borde suave (m)", overlay.feather);

            string currentPreset = string.IsNullOrEmpty(overlay.curvePreset)
                ? "balanced"
                : overlay.curvePreset;
            int presetIndex = Mathf.Max(0, Array.FindIndex(
                OverlayCurvePresets,
                candidate => string.Equals(candidate, currentPreset,
                    StringComparison.OrdinalIgnoreCase)));
            int newPresetIndex = EditorGUILayout.Popup(
                "Curvatura", presetIndex, OverlayCurvePresetLabels);

            if (newPresetIndex != presetIndex)
                overlay.curvePreset = OverlayCurvePresets[newPresetIndex];

            EditorGUILayout.HelpBox(
                "Las paredes nunca se cruzan. Mesetas y fondos de cañón sí, "
                + "pero solo se baja entre ellos por rampas.",
                MessageType.None);

            if (i >= overlayAdvancedFoldouts.Length)
                Array.Resize(ref overlayAdvancedFoldouts, i + 1);

            overlayAdvancedFoldouts[i] = EditorGUILayout.Foldout(
                overlayAdvancedFoldouts[i], "Avanzado", true);

            if (overlayAdvancedFoldouts[i])
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                overlay.curviness = EditorGUILayout.FloatField(
                    "Curvatura manual (0..1, -1 = preset)", overlay.curviness);

                if (EditorGUI.EndChangeCheck())
                    overlay.curvinessSet = overlay.curviness >= 0f;

                overlay.maxDetour = EditorGUILayout.FloatField(
                    "Desvío máximo (m, -1 = preset)", overlay.maxDetour);
                overlay.wallClearance = EditorGUILayout.FloatField(
                    "Margen a paredes (m)", overlay.wallClearance);
                overlay.wanderAmplitude = EditorGUILayout.FloatField(
                    "Ruido fino del borde (m)", overlay.wanderAmplitude);
                overlay.wanderFrequency = EditorGUILayout.FloatField(
                    "Frecuencia del ruido", overlay.wanderFrequency);
                overlay.fadeDistance = EditorGUILayout.FloatField(
                    "Fade distance", overlay.fadeDistance);
                overlay.normalStrength = EditorGUILayout.FloatField(
                    "Normal strength", overlay.normalStrength);
                overlay.aoStrength = EditorGUILayout.FloatField(
                    "AO strength", overlay.aoStrength);
                overlay.angle = EditorGUILayout.FloatField("Angle", overlay.angle);
                overlay.offset = EditorGUILayout.FloatField("Offset", overlay.offset);
                overlay.noiseScale = EditorGUILayout.FloatField(
                    "Noise scale", overlay.noiseScale);
                overlay.threshold = EditorGUILayout.FloatField(
                    "Threshold", overlay.threshold);
                overlay.patchFeather = EditorGUILayout.FloatField(
                    "Patch feather", overlay.patchFeather);
                overlay.seedOffset = EditorGUILayout.FloatField(
                    "Seed offset", overlay.seedOffset);
                overlay.connectPois = EditorGUILayout.Toggle(
                    "Connect POIs", overlay.connectPois);
                EditorGUI.indentLevel--;
            }

            EditorGUI.indentLevel--;
        }

        EditorGUI.BeginDisabledGroup(draftOverlays.Length >= 3);
        if (GUILayout.Button("Añadir overlay"))
        {
            Array.Resize(ref draftOverlays, draftOverlays.Length + 1);
            draftOverlays[draftOverlays.Length - 1] = new TerrainOverlay
            {
                label = "Nuevo overlay",
                texture = "",
                mode = "patch"
            };
        }
        EditorGUI.EndDisabledGroup();
    }

    private void DrawVegetationConfig()
    {
        vegetationFoldout = EditorGUILayout.Foldout(
            vegetationFoldout, "Vegetación: árboles, arbustos y rocas (StarSystems.json)", true,
            EditorStyles.foldoutHeader);
        if (!vegetationFoldout)
            return;

        if (draftVegetation == null)
            draftVegetation = new VegetationSettings[0];

        EnsureFoldouts(ref vegetationFoldouts, draftVegetation.Length + 1);

        for (int i = 0; i < draftVegetation.Length; i++)
        {
            VegetationSettings entry = draftVegetation[i];
            if (entry == null)
                continue;

            string label = string.IsNullOrWhiteSpace(entry.label)
                ? "Vegetación " + (i + 1) + (string.IsNullOrEmpty(entry.prefab) ? " (sin prefab)" : ": " + entry.prefab)
                : "Vegetación " + (i + 1) + ": " + entry.label;

            EditorGUILayout.BeginHorizontal();
            vegetationFoldouts[i] = EditorGUILayout.Foldout(vegetationFoldouts[i], label, true);
            if (GUILayout.Button("Quitar", GUILayout.Width(60f)))
            {
                draftVegetation = RemoveAt(draftVegetation, i);
                ShiftFoldouts(vegetationFoldouts, i);
                EditorGUILayout.EndHorizontal();
                break;
            }
            EditorGUILayout.EndHorizontal();

            if (!vegetationFoldouts[i])
                continue;

            EditorGUI.indentLevel++;
            entry.enabled = EditorGUILayout.Toggle("Enabled", entry.enabled);
            entry.label = EditorGUILayout.TextField("Label", entry.label ?? "");

            EditorGUILayout.HelpBox(
                "El prefab se carga desde Resources/<carpeta><prefab>. Usa 'objects/trees/' para árboles y arbustos, "
                + "u 'objects/props/' para rocas y otros modelos instanciados.",
                MessageType.None);

            entry.folder = EditorGUILayout.TextField(
                "Carpeta", string.IsNullOrEmpty(entry.folder) ? "objects/trees/" : entry.folder);
            entry.folder = (entry.folder ?? "").Trim().Replace('\\', '/');
            if (!entry.folder.EndsWith("/", StringComparison.Ordinal))
                entry.folder += "/";

            entry.prefab = DrawPrefabPicker("Prefab", entry.prefab, vegetationPrefabOptions);
            entry.material = DrawPrefabPicker("Material (opcional)", entry.material, vegetationMaterialOptions);

            entry.countPerTile = Mathf.Max(0, EditorGUILayout.IntField("Instancias por tile", entry.countPerTile));
            entry.minScale = Mathf.Max(0.01f, EditorGUILayout.FloatField("Escala mínima", entry.minScale));
            entry.maxScale = Mathf.Max(entry.minScale, EditorGUILayout.FloatField("Escala máxima", entry.maxScale));
            entry.maxSlope = Mathf.Max(0f, EditorGUILayout.FloatField("Pendiente máx. (0 = todas)", entry.maxSlope));
            entry.maxDistance = Mathf.Max(0f, EditorGUILayout.FloatField("Distancia máx. (m)", entry.maxDistance));
            entry.placementAttempts = Mathf.Max(1, EditorGUILayout.IntField("Intentos de colocación", entry.placementAttempts));
            entry.seedOffset = EditorGUILayout.IntField("Seed offset", entry.seedOffset);

            EditorGUILayout.Space(2f);
            entry.windStrength = EditorGUILayout.FloatField("Fuerza del viento", entry.windStrength);
            entry.windSpeed = EditorGUILayout.FloatField("Velocidad del viento", entry.windSpeed);
            entry.windFrequency = EditorGUILayout.FloatField("Frecuencia del viento", entry.windFrequency);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Añadir vegetación"))
        {
            AddVegetationEntry(new VegetationSettings
            {
                label = "Nueva vegetación",
                folder = "objects/trees/",
                prefab = vegetationPrefabOptions != null && vegetationPrefabOptions.Length > 0
                    ? vegetationPrefabOptions[0]
                    : ""
            });
        }

        if (GUILayout.Button("Añadir roca"))
        {
            AddVegetationEntry(new VegetationSettings
            {
                label = "Rocas",
                folder = "objects/props/",
                prefab = "",
                countPerTile = 40,
                minScale = 1f,
                maxScale = 2f,
                maxSlope = 45f,
                maxDistance = 700f
            });
        }
        EditorGUILayout.EndHorizontal();
    }

    private void AddVegetationEntry(VegetationSettings entry)
    {
        if (draftVegetation == null)
            draftVegetation = new VegetationSettings[0];

        if (entry.seedOffset == 0)
            entry.seedOffset = 17 + draftVegetation.Length * 17;

        Array.Resize(ref draftVegetation, draftVegetation.Length + 1);
        draftVegetation[draftVegetation.Length - 1] = entry;
        EnsureFoldouts(ref vegetationFoldouts, draftVegetation.Length + 1);
        vegetationFoldouts[draftVegetation.Length - 1] = true;
    }

    private void DrawPoiConfig()
    {
        propsFoldout = EditorGUILayout.Foldout(
            propsFoldout, "POIs, props, asentamientos y criaturas (PlanetPOIs.json)", true,
            EditorStyles.foldoutHeader);
        if (!propsFoldout)
            return;

        EditorGUILayout.HelpBox(
            "Landmarks aislados, asentamientos, rebaños y balizas de POI tal y como los instancia el juego. " +
            "'Guardar POIs en JSON' escribe este bloque en PlanetPOIs.json.",
            MessageType.None);

        if (draftLocation == null)
            draftLocation = new PlanetPOILocation();

        NormalizeLocationArrays(draftLocation);

        if (!draftHasLocation)
        {
            if (GUILayout.Button("Crear entrada de POIs para " + selectedPlanet))
            {
                draftHasLocation = true;
                draftLocation.planet = selectedPlanet;
                draftLocation.pois = new PlanetPOIEntry[0];
                draftLocation.props = new PlanetPOIProp[0];
                draftLocation.sites = new PlanetPOISite[0];
                draftLocation.creatures = new PlanetPOICreature[0];
            }

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.LabelField("Sin entrada en PlanetPOIs.json", EditorStyles.miniLabel);
            EditorGUI.EndDisabledGroup();
            return;
        }

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Planeta", selectedPlanet, EditorStyles.boldLabel);
        if (GUILayout.Button("Restablecer POIs desde JSON", GUILayout.Width(190f)))
        {
            SyncPoiDraftIfNeeded(true);
            SetStatus("POIs restablecidos desde PlanetPOIs.json.", MessageType.Info);
        }
        EditorGUILayout.EndHorizontal();

        draftLocation.planet = selectedPlanet;
        draftLocation.entryX = EditorGUILayout.FloatField("Entrada X", draftLocation.entryX);
        draftLocation.entryZ = EditorGUILayout.FloatField("Entrada Z", draftLocation.entryZ);

        EditorGUILayout.Space(4f);
        DrawPoiEntries();
        EditorGUILayout.Space(4f);
        DrawPropEntries();
        EditorGUILayout.Space(4f);
        DrawSiteEntries();
        EditorGUILayout.Space(4f);
        DrawCreatureEntries();

        EditorGUILayout.Space(4f);
        if (GUILayout.Button("Guardar POIs en PlanetPOIs.json", GUILayout.Height(28f)))
            ApplyPoiConfigToJson();
    }

    private void DrawPoiEntries()
    {
        poisFoldout = EditorGUILayout.Foldout(
            poisFoldout, "POIs / balizas (" + draftLocation.pois.Length + ")", true);
        if (!poisFoldout)
            return;

        EnsureFoldouts(ref poiFoldouts, draftLocation.pois.Length + 1);

        for (int i = 0; i < draftLocation.pois.Length; i++)
        {
            PlanetPOIEntry poi = draftLocation.pois[i];
            if (poi == null)
                continue;

            EditorGUILayout.BeginHorizontal();
            poiFoldouts[i] = EditorGUILayout.Foldout(
                poiFoldouts[i],
                string.IsNullOrEmpty(poi.name) ? "POI " + (i + 1) : poi.name,
                true);
            if (GUILayout.Button("Quitar", GUILayout.Width(60f)))
            {
                draftLocation.pois = RemoveAt(draftLocation.pois, i);
                ShiftFoldouts(poiFoldouts, i);
                EditorGUILayout.EndHorizontal();
                break;
            }
            EditorGUILayout.EndHorizontal();

            if (!poiFoldouts[i])
                continue;

            EditorGUI.indentLevel++;
            poi.name = EditorGUILayout.TextField("Nombre", poi.name ?? "");
            poi.id = EditorGUILayout.TextField("Id (enlaces y caminos)", poi.id ?? "");
            poi.x = EditorGUILayout.FloatField("X (m)", poi.x);
            poi.z = EditorGUILayout.FloatField("Z (m)", poi.z);
            poi.radius = EditorGUILayout.FloatField("Radio (m)", poi.radius);
            if (GUILayout.Button("Centrar rejilla aquí"))
                CenterGridOn(poi.x, poi.z, poi.radius);
            EditorGUI.indentLevel--;
        }

        if (GUILayout.Button("Añadir POI"))
        {
            Array.Resize(ref draftLocation.pois, draftLocation.pois.Length + 1);
            draftLocation.pois[draftLocation.pois.Length - 1] = new PlanetPOIEntry
            {
                name = "NUEVO POI",
                x = 0f,
                z = 0f,
                radius = 300f
            };
        }
    }

    private void DrawPropEntries()
    {
        EditorGUILayout.LabelField("Props aislados (" + draftLocation.props.Length + ")",
            EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "Un prop es un objeto fijo: x/z, rotación y escala. Los que tienen "
            + "cantidad > 1 son el formato antiguo de grupo (swarm). Para pueblos "
            + "con casas y vehículos mezclados usa 'Asentamientos'.",
            MessageType.None);

        EnsureFoldouts(ref propFoldouts, draftLocation.props.Length + 1);

        for (int i = 0; i < draftLocation.props.Length; i++)
        {
            PlanetPOIProp prop = draftLocation.props[i];
            if (prop == null)
                continue;

            string type = PlanetContentUtility.GetPropType(prop);
            string title = string.IsNullOrEmpty(type) ? "Prop " + (i + 1) : type;

            if (prop.count > 1)
                title += " (grupo legacy x" + prop.count + ")";
            else
                title += " (aislado)";

            EditorGUILayout.BeginHorizontal();
            propFoldouts[i] = EditorGUILayout.Foldout(propFoldouts[i], title, true);
            if (GUILayout.Button("Quitar", GUILayout.Width(60f)))
            {
                draftLocation.props = RemoveAt(draftLocation.props, i);
                ShiftFoldouts(propFoldouts, i);
                EditorGUILayout.EndHorizontal();
                break;
            }
            EditorGUILayout.EndHorizontal();

            if (!propFoldouts[i])
                continue;

            EditorGUI.indentLevel++;
            prop.id = EditorGUILayout.TextField("Id (opcional)", prop.id ?? "");
            prop.type = DrawPrefabPicker("Tipo (PropsTypes)", type, propPrefabOptions);

            // Keep legacy prefab in sync while the entry has no catalog type;
            // old PlanetPOIs.json files only have "prefab".
            if (string.IsNullOrEmpty(prop.type))
                prop.prefab = EditorGUILayout.TextField("Prefab legacy", prop.prefab ?? "");

            prop.site = DrawSitePicker("Sitio (opcional)", prop.site);
            prop.x = EditorGUILayout.FloatField("X (m)", prop.x);
            prop.z = EditorGUILayout.FloatField("Z (m)", prop.z);
            prop.rotY = EditorGUILayout.FloatField("Rotación Y", prop.rotY);
            prop.scale = EditorGUILayout.FloatField("Escala", prop.scale);
            prop.count = Mathf.Max(1, EditorGUILayout.IntField("Cantidad", prop.count));

            if (prop.count > 1)
            {
                prop.radius = EditorGUILayout.FloatField("Radio del grupo (m)", prop.radius);
                prop.seed = EditorGUILayout.IntField("Seed", prop.seed);
                prop.randomYaw = EditorGUILayout.Toggle("Rotación aleatoria", prop.randomYaw);
                prop.scaleVar = Mathf.Clamp01(EditorGUILayout.FloatField("Variación escala (0-1)", prop.scaleVar));
                prop.minDist = EditorGUILayout.FloatField("Separación mínima (m)", prop.minDist);
                DrawSwarmHint(prop);

                if (GUILayout.Button("Convertir este grupo en asentamiento"))
                    ConvertLegacyPropToSite(i);
            }

            if (GUILayout.Button("Centrar rejilla aquí"))
                CenterGridOn(prop.x, prop.z, Mathf.Max(prop.radius, 40f));
            EditorGUI.indentLevel--;
        }

        if (GUILayout.Button("Añadir prop aislado"))
        {
            Array.Resize(ref draftLocation.props, draftLocation.props.Length + 1);
            draftLocation.props[draftLocation.props.Length - 1] = new PlanetPOIProp
            {
                type = propPrefabOptions.Length > 0 ? propPrefabOptions[0] : "",
                x = 0f,
                z = 0f,
                rotY = 0f,
                scale = 1f,
                count = 1,
                radius = 0f,
                seed = 1,
                randomYaw = false,
                scaleVar = 0f,
                minDist = 30f
            };
        }
    }

    private void DrawSiteEntries()
    {
        EditorGUILayout.LabelField("Asentamientos / pueblos (" + draftLocation.sites.Length + ")",
            EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "Un sitio agrupa varios tipos de PropsTypes (casas, vehículos...) y los "
            + "reparte con un layout: scatter (disco), grid (manzanas), radial o "
            + "alongPath (calle). Puede enlazarse a un POI para que caminos y "
            + "navegación sigan al pueblo.",
            MessageType.None);

        EnsureFoldouts(ref siteFoldouts, draftLocation.sites.Length + 1);
        EnsureFoldouts(ref siteRuleFoldouts, draftLocation.sites.Length * 8 + 8);

        for (int i = 0; i < draftLocation.sites.Length; i++)
        {
            PlanetPOISite site = draftLocation.sites[i];
            if (site == null)
                continue;

            string title = string.IsNullOrEmpty(site.name)
                ? (string.IsNullOrEmpty(site.id) ? "Sitio " + (i + 1) : site.id)
                : site.name;

            EditorGUILayout.BeginHorizontal();
            siteFoldouts[i] = EditorGUILayout.Foldout(siteFoldouts[i],
                title + " (" + (site.rules != null ? site.rules.Length : 0) + " tipos)", true);
            if (GUILayout.Button("Quitar", GUILayout.Width(60f)))
            {
                draftLocation.sites = RemoveAt(draftLocation.sites, i);
                ShiftFoldouts(siteFoldouts, i);
                EditorGUILayout.EndHorizontal();
                break;
            }
            EditorGUILayout.EndHorizontal();

            if (!siteFoldouts[i])
                continue;

            EditorGUI.indentLevel++;
            site.id = EditorGUILayout.TextField("Id", site.id ?? "");
            site.name = EditorGUILayout.TextField("Nombre", site.name ?? "");
            site.poi = DrawPoiPicker("POI enlazado", site.poi);
            DrawSitePoiLink(site);

            string[] kinds = { "town", "outpost", "camp", "ruins" };
            int kindIndex = Mathf.Max(0, Array.FindIndex(kinds,
                candidate => string.Equals(candidate, site.kind, StringComparison.OrdinalIgnoreCase)));
            kindIndex = EditorGUILayout.Popup("Tipo", kindIndex, kinds);
            site.kind = kinds[kindIndex];

            string[] layouts = { "scatter", "grid", "radial", "alongPath" };
            int layoutIndex = Mathf.Max(0, Array.FindIndex(layouts,
                candidate => string.Equals(candidate, site.layout, StringComparison.OrdinalIgnoreCase)));
            layoutIndex = EditorGUILayout.Popup("Layout", layoutIndex, layouts);
            site.layout = layouts[layoutIndex];

            site.x = EditorGUILayout.FloatField("X (m)", site.x);
            site.z = EditorGUILayout.FloatField("Z (m)", site.z);
            site.radius = EditorGUILayout.FloatField("Radio (m)", site.radius);
            site.rotation = EditorGUILayout.FloatField("Rotación del layout", site.rotation);
            site.seed = EditorGUILayout.IntField("Seed", site.seed);
            site.spacing = EditorGUILayout.FloatField("Separación / paso (m)", site.spacing);

            if (GUILayout.Button("Centrar rejilla aquí"))
                CenterGridOn(site.x, site.z, Mathf.Max(site.radius, 40f));

            DrawSiteHint(site);
            DrawSiteRules(site);
            EditorGUI.indentLevel--;
        }

        if (GUILayout.Button("Añadir asentamiento"))
        {
            Array.Resize(ref draftLocation.sites, draftLocation.sites.Length + 1);
            draftLocation.sites[draftLocation.sites.Length - 1] = new PlanetPOISite
            {
                id = MakeUniqueSiteId("site"),
                name = "Nuevo asentamiento",
                kind = "town",
                layout = "grid",
                x = 0f,
                z = 0f,
                radius = 300f,
                rotation = 0f,
                seed = 1,
                spacing = 45f,
                rules = new[]
                {
                    new PlanetPOISiteRule
                    {
                        type = GetDefaultSettlementType(),
                        count = 6,
                        weight = 1f,
                        scale = 1f,
                        scaleVar = 0.1f,
                        randomYaw = true
                    }
                }
            };
        }
    }

    private void DrawSitePoiLink(PlanetPOISite site)
    {
        if (string.IsNullOrEmpty(site.poi))
            return;

        PlanetPOIEntry linked = FindPoiById(site.poi);

        if (linked == null)
        {
            EditorGUILayout.HelpBox(
                "El POI '" + site.poi + "' no existe en este planeta.",
                MessageType.Warning);
            return;
        }

        float distance = Vector2.Distance(
            new Vector2(site.x, site.z), new Vector2(linked.x, linked.z));

        if (distance < 1f)
        {
            EditorGUILayout.LabelField("Alineado con el POI.", EditorStyles.miniLabel);
            return;
        }

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(
            "Separado " + Mathf.RoundToInt(distance) + " m del POI.",
            EditorStyles.miniLabel);

        if (GUILayout.Button("Alinear", GUILayout.Width(70f)))
        {
            site.x = linked.x;
            site.z = linked.z;
            ScheduleLiveRefresh();
        }

        EditorGUILayout.EndHorizontal();
    }

    private PlanetPOIEntry FindPoiById(string id)
    {
        if (draftLocation == null || draftLocation.pois == null || string.IsNullOrEmpty(id))
            return null;

        foreach (PlanetPOIEntry poi in draftLocation.pois)
        {
            if (poi != null && string.Equals(PlanetContentUtility.GetPoiId(poi), id,
                    StringComparison.OrdinalIgnoreCase))
            {
                return poi;
            }
        }

        return null;
    }

    private string GetDefaultSettlementType()
    {
        if (propTypes != null && propTypes.propTypesData != null)
        {
            foreach (PropType def in propTypes.propTypesData)
            {
                if (def == null)
                    continue;

                bool isSettlement = string.Equals(def.category, "building",
                        StringComparison.OrdinalIgnoreCase)
                    || (def.tags != null && def.tags.Any(tag => string.Equals(tag,
                        "settlement", StringComparison.OrdinalIgnoreCase)));

                if (isSettlement)
                {
                    string type = string.IsNullOrEmpty(def.type) ? def.prefab : def.type;

                    if (!string.IsNullOrEmpty(type))
                        return type;
                }
            }
        }

        return propPrefabOptions.Length > 0 ? propPrefabOptions[0] : "";
    }

    private void DrawSiteRules(PlanetPOISite site)
    {
        if (site.rules == null)
            site.rules = new PlanetPOISiteRule[0];

        EditorGUILayout.LabelField("Tipos del asentamiento", EditorStyles.miniBoldLabel);

        int baseIndex = Array.IndexOf(draftLocation.sites, site) * 8;
        if (baseIndex < 0)
            baseIndex = 0;

        for (int i = 0; i < site.rules.Length; i++)
        {
            PlanetPOISiteRule rule = site.rules[i];
            if (rule == null)
                continue;

            EditorGUILayout.BeginHorizontal();
            int foldIndex = baseIndex + i;
            EnsureFoldouts(ref siteRuleFoldouts, foldIndex + 1);
            siteRuleFoldouts[foldIndex] = EditorGUILayout.Foldout(
                siteRuleFoldouts[foldIndex],
                (string.IsNullOrEmpty(rule.type) ? "Tipo " + (i + 1) : rule.type)
                + " x" + Mathf.Max(rule.count, 0),
                true);

            if (GUILayout.Button("Quitar", GUILayout.Width(60f)))
            {
                site.rules = RemoveAt(site.rules, i);
                EditorGUILayout.EndHorizontal();
                break;
            }
            EditorGUILayout.EndHorizontal();

            if (!siteRuleFoldouts[foldIndex])
                continue;

            EditorGUI.indentLevel++;
            rule.type = DrawPrefabPicker("Tipo (PropsTypes)", rule.type, propPrefabOptions);
            rule.count = Mathf.Max(0, EditorGUILayout.IntField("Cantidad", rule.count));
            rule.weight = EditorGUILayout.FloatField("Peso de mezcla", rule.weight);
            rule.scale = EditorGUILayout.FloatField("Escala", rule.scale);
            rule.scaleVar = Mathf.Clamp01(EditorGUILayout.FloatField("Variación escala (0-1)", rule.scaleVar));
            rule.randomYaw = EditorGUILayout.Toggle("Rotación aleatoria", rule.randomYaw);
            rule.mode = EditorGUILayout.TextField("Modo (parked, street...)", rule.mode ?? "");
            EditorGUI.indentLevel--;
        }

        if (GUILayout.Button("Añadir tipo al asentamiento"))
        {
            Array.Resize(ref site.rules, site.rules.Length + 1);
            site.rules[site.rules.Length - 1] = new PlanetPOISiteRule
            {
                type = GetDefaultSettlementType(),
                count = 1,
                weight = 1f,
                scale = 1f,
                scaleVar = 0f,
                randomYaw = true
            };
        }
    }

    private void DrawCreatureEntries()
    {
        EditorGUILayout.LabelField("Criaturas / rebaños (" + draftLocation.creatures.Length + ")",
            EditorStyles.boldLabel);
        EnsureFoldouts(ref creatureFoldouts, draftLocation.creatures.Length + 1);

        for (int i = 0; i < draftLocation.creatures.Length; i++)
        {
            PlanetPOICreature creature = draftLocation.creatures[i];
            if (creature == null)
                continue;

            EditorGUILayout.BeginHorizontal();
            creatureFoldouts[i] = EditorGUILayout.Foldout(
                creatureFoldouts[i],
                string.IsNullOrEmpty(creature.prefab)
                    ? "Rebaño " + (i + 1)
                    : creature.prefab + " (" + creature.herdMin + "-" + creature.herdMax + ")",
                true);
            if (GUILayout.Button("Quitar", GUILayout.Width(60f)))
            {
                draftLocation.creatures = RemoveAt(draftLocation.creatures, i);
                ShiftFoldouts(creatureFoldouts, i);
                EditorGUILayout.EndHorizontal();
                break;
            }
            EditorGUILayout.EndHorizontal();

            if (!creatureFoldouts[i])
                continue;

            EditorGUI.indentLevel++;
            creature.prefab = DrawPrefabPicker("Prefab", creature.prefab, creaturePrefabOptions);
            creature.x = EditorGUILayout.FloatField("X (m)", creature.x);
            creature.z = EditorGUILayout.FloatField("Z (m)", creature.z);
            creature.site = DrawSitePicker("Sitio (opcional)", creature.site);
            creature.herdMin = EditorGUILayout.IntField("Miembros mín.", creature.herdMin);
            creature.herdMax = EditorGUILayout.IntField("Miembros máx.", creature.herdMax);
            creature.herdRadius = EditorGUILayout.FloatField("Radio rebaño (m)", creature.herdRadius);
            creature.wanderRadius = EditorGUILayout.FloatField("Radio deambular (m)", creature.wanderRadius);
            creature.speed = EditorGUILayout.FloatField("Velocidad", creature.speed);
            creature.seed = EditorGUILayout.IntField("Seed", creature.seed);
            if (GUILayout.Button("Centrar rejilla aquí"))
                CenterGridOn(creature.x, creature.z, Mathf.Max(creature.wanderRadius, 60f));
            EditorGUI.indentLevel--;
        }

        if (GUILayout.Button("Añadir rebaño"))
        {
            Array.Resize(ref draftLocation.creatures, draftLocation.creatures.Length + 1);
            draftLocation.creatures[draftLocation.creatures.Length - 1] = new PlanetPOICreature
            {
                prefab = creaturePrefabOptions.Length > 0 ? creaturePrefabOptions[0] : "",
                x = 0f,
                z = 0f,
                herdMin = 3,
                herdMax = 6,
                herdRadius = 14f,
                wanderRadius = 90f,
                speed = 2.2f,
                seed = 1
            };
        }
    }

    private static void DrawSiteHint(PlanetPOISite site)
    {
        int total = 0;

        if (site.rules != null)
        {
            foreach (PlanetPOISiteRule rule in site.rules)
            {
                if (rule != null)
                    total += Mathf.Max(rule.count, 0);
            }
        }

        if (total <= 0)
        {
            EditorGUILayout.HelpBox("El asentamiento no tiene props que colocar.",
                MessageType.Warning);
            return;
        }

        if (site.radius <= 0f)
        {
            EditorGUILayout.HelpBox(
                "Radio 0: los " + total + " props se apilan en el mismo punto.",
                MessageType.Warning);
            return;
        }

        float spacing = Mathf.Max(site.spacing, 1f);
        int capacity;

        switch (PlanetContentUtility.NormalizeLayout(site.layout))
        {
            case PlanetContentUtility.LayoutGrid:
            {
                int steps = Mathf.Max(1, Mathf.FloorToInt(site.radius / spacing));
                capacity = Mathf.FloorToInt(Mathf.PI * steps * steps);
                break;
            }
            case PlanetContentUtility.LayoutRadial:
                capacity = Mathf.Max(1, Mathf.FloorToInt(
                    0.65f * 4f * site.radius * site.radius / (spacing * spacing)));
                break;
            case PlanetContentUtility.LayoutAlongPath:
                capacity = 2 * (2 * Mathf.FloorToInt(site.radius / spacing) + 1);
                break;
            default:
                capacity = Mathf.Max(1, Mathf.FloorToInt(
                    0.7f * 4f * site.radius * site.radius / (spacing * spacing)));
                break;
        }

        if (capacity < total)
        {
            EditorGUILayout.HelpBox(
                "Cabida estimada ~" + capacity + " de " + total + " props (radio "
                + site.radius + " m, separación " + spacing + " m). Se colocarán todos, "
                + "pero el layout puede rellenar el resto con separación reducida.",
                MessageType.Warning);
        }
        else
        {
            EditorGUILayout.LabelField(
                "Total: " + total + " props. Cabida estimada: ~" + capacity + ".",
                EditorStyles.miniLabel);
        }
    }

    private string DrawPoiPicker(string label, string current)
    {
        if (draftLocation.pois == null || draftLocation.pois.Length == 0)
            return EditorGUILayout.TextField(label, current ?? "");

        List<string> ids = new List<string>();

        foreach (PlanetPOIEntry poi in draftLocation.pois)
        {
            if (poi == null)
                continue;

            string id = PlanetContentUtility.GetPoiId(poi);

            if (!string.IsNullOrEmpty(id) && !ids.Contains(id))
                ids.Add(id);
        }

        return DrawIdPicker(label, current, ids);
    }

    private string DrawSitePicker(string label, string current)
    {
        if (draftLocation.sites == null || draftLocation.sites.Length == 0)
            return EditorGUILayout.TextField(label, current ?? "");

        List<string> ids = new List<string>();

        foreach (PlanetPOISite site in draftLocation.sites)
        {
            if (site == null)
                continue;

            string id = PlanetContentUtility.GetSiteId(site);

            if (!string.IsNullOrEmpty(id) && !ids.Contains(id))
                ids.Add(id);
        }

        return DrawIdPicker(label, current, ids);
    }

    private static string DrawIdPicker(string label, string current, List<string> ids)
    {
        if (!string.IsNullOrEmpty(current) && !ids.Contains(current))
            ids.Add(current);

        string[] options = new string[ids.Count + 1];
        options[0] = "(ninguno)";

        for (int i = 0; i < ids.Count; i++)
            options[i + 1] = ids[i];

        int index = string.IsNullOrEmpty(current)
            ? 0
            : Mathf.Max(0, Array.FindIndex(options,
                candidate => string.Equals(candidate, current, StringComparison.OrdinalIgnoreCase)));

        int newIndex = EditorGUILayout.Popup(label, index, options);
        return newIndex <= 0 ? "" : options[newIndex];
    }

    private string MakeUniqueSiteId(string baseId)
    {
        string root = SanitizeName(string.IsNullOrEmpty(baseId) ? "site" : baseId)
            .Trim('_', ' ');

        if (string.IsNullOrEmpty(root))
            root = "site";

        string candidate = root;
        int suffix = 2;

        while (SiteIdExists(candidate))
        {
            candidate = root + "_" + suffix;
            suffix++;
        }

        return candidate;
    }

    private bool SiteIdExists(string id)
    {
        if (draftLocation == null || draftLocation.sites == null)
            return false;

        foreach (PlanetPOISite site in draftLocation.sites)
        {
            if (site != null && string.Equals(PlanetContentUtility.GetSiteId(site), id,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void ConvertLegacyPropToSite(int index)
    {
        if (draftLocation == null || draftLocation.props == null
            || index < 0 || index >= draftLocation.props.Length)
        {
            return;
        }

        PlanetPOIProp prop = draftLocation.props[index];
        string type = PlanetContentUtility.GetPropType(prop);

        if (prop == null || string.IsNullOrEmpty(type))
        {
            SetStatus("El prop no tiene un tipo válido.", MessageType.Warning);
            return;
        }

        PlanetPOISite site = new PlanetPOISite
        {
            id = MakeUniqueSiteId(type + "_site"),
            name = type + " (asentamiento)",
            kind = "town",
            layout = PlanetContentUtility.LayoutScatter,
            x = prop.x,
            z = prop.z,
            radius = prop.radius,
            rotation = prop.rotY,
            seed = prop.seed,
            spacing = Mathf.Max(prop.minDist, 1f),
            rules = new[]
            {
                new PlanetPOISiteRule
                {
                    type = type,
                    count = Mathf.Max(prop.count, 1),
                    weight = 1f,
                    scale = prop.scale,
                    scaleVar = prop.scaleVar,
                    randomYaw = prop.randomYaw
                }
            }
        };

        draftLocation.props = RemoveAt(draftLocation.props, index);
        ShiftFoldouts(propFoldouts, index);
        Array.Resize(ref draftLocation.sites, draftLocation.sites.Length + 1);
        draftLocation.sites[draftLocation.sites.Length - 1] = site;
        EnsureFoldouts(ref siteFoldouts, draftLocation.sites.Length + 1);
        siteFoldouts[draftLocation.sites.Length - 1] = true;

        SetStatus("Grupo convertido en asentamiento '" + site.id
            + "'. Revisa el layout y guarda PlanetPOIs.json.", MessageType.Info);
        ScheduleLiveRefresh();
    }

    private static void DrawSwarmHint(PlanetPOIProp prop)
    {
        int count = Mathf.Max(prop.count, 1);
        if (count <= 1)
            return;

        if (prop.radius <= 0f)
        {
            EditorGUILayout.HelpBox(
                "Radio 0: los " + count
                + " props se colocan en el mismo punto y parecerán uno solo.",
                MessageType.Warning);
            return;
        }

        float spacing = Mathf.Max(prop.minDist, 1f);
        int approx = Mathf.Max(1, Mathf.FloorToInt(
            0.7f * 4f * prop.radius * prop.radius / (spacing * spacing)));

        if (approx < count)
        {
            EditorGUILayout.HelpBox(
                "Cabida estimada ~" + approx + " de " + count + " props (radio " + prop.radius
                + " m, separación " + spacing + " m). Se colocarán todos, pero los que no caben "
                + "quedarán más juntos. Amplía el radio o reduce la separación/cantidad.",
                MessageType.Warning);
        }
        else
        {
            EditorGUILayout.LabelField("Cabida estimada: ~" + approx + " props.",
                EditorStyles.miniLabel);
        }
    }

    private static string DrawPrefabPicker(string label, string current, string[] options)
    {
        string result = EditorGUILayout.TextField(label, current ?? "");

        if (options != null && options.Length > 0)
        {
            int index = Array.FindIndex(options,
                candidate => string.Equals(candidate, result, StringComparison.OrdinalIgnoreCase));
            int newIndex = EditorGUILayout.Popup("Elegir", index, options);
            if (newIndex >= 0 && newIndex != index && newIndex < options.Length)
                result = options[newIndex];
        }

        return result;
    }

    private static T[] RemoveAt<T>(T[] source, int index)
    {
        List<T> list = new List<T>(source);
        list.RemoveAt(index);
        return list.ToArray();
    }

    private static void EnsureFoldouts(ref bool[] foldouts, int length)
    {
        if (foldouts == null)
            foldouts = new bool[length];
        else if (foldouts.Length < length)
            Array.Resize(ref foldouts, length);
    }

    private static void ShiftFoldouts(bool[] foldouts, int index)
    {
        if (foldouts == null || index < 0 || index >= foldouts.Length)
            return;

        for (int i = index; i < foldouts.Length - 1; i++)
            foldouts[i] = foldouts[i + 1];
        foldouts[foldouts.Length - 1] = false;
    }

    private static void NormalizeLocationArrays(PlanetPOILocation location)
    {
        PlanetContentUtility.Normalize(location);
    }

    private void CenterGridOn(float x, float z, float radius)
    {
        centerTileX = Mathf.FloorToInt(x / PreviewTileSize);
        centerTileZ = Mathf.FloorToInt(z / PreviewTileSize);
        gridSize = Mathf.Clamp(
            Mathf.CeilToInt(radius * 2f / PreviewTileSize) + 1, 1, 6);
        ScheduleLiveRefresh();
    }

    private void FrameGridToPoiContent()
    {
        if (draftLocation == null || !draftHasLocation)
        {
            SetStatus("Este planeta no tiene POIs, props, asentamientos ni criaturas definidos.", MessageType.Warning);
            return;
        }

        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minZ = float.PositiveInfinity;
        float maxZ = float.NegativeInfinity;
        bool any = false;

        NormalizeLocationArrays(draftLocation);

        foreach (PlanetPOIEntry poi in draftLocation.pois)
        {
            if (poi == null)
                continue;
            any |= Accumulate(poi.x - poi.radius, poi.x + poi.radius,
                poi.z - poi.radius, poi.z + poi.radius, ref minX, ref maxX, ref minZ, ref maxZ);
        }

        foreach (PlanetPOIProp prop in draftLocation.props)
        {
            if (prop == null)
                continue;
            float radius = Mathf.Max(prop.radius, 40f);
            any |= Accumulate(prop.x - radius, prop.x + radius,
                prop.z - radius, prop.z + radius, ref minX, ref maxX, ref minZ, ref maxZ);
        }

        foreach (PlanetPOISite site in draftLocation.sites)
        {
            if (site == null)
                continue;
            float radius = Mathf.Max(site.radius, 40f);
            any |= Accumulate(site.x - radius, site.x + radius,
                site.z - radius, site.z + radius, ref minX, ref maxX, ref minZ, ref maxZ);
        }

        foreach (PlanetPOICreature creature in draftLocation.creatures)
        {
            if (creature == null)
                continue;
            float radius = Mathf.Max(creature.wanderRadius, 60f);
            any |= Accumulate(creature.x - radius, creature.x + radius,
                creature.z - radius, creature.z + radius, ref minX, ref maxX, ref minZ, ref maxZ);
        }

        if (!any)
        {
            SetStatus("No hay POIs, props, asentamientos ni criaturas que encuadrar.", MessageType.Warning);
            return;
        }

        int startTileX = Mathf.FloorToInt(minX / PreviewTileSize);
        int endTileX = Mathf.FloorToInt(maxX / PreviewTileSize);
        int startTileZ = Mathf.FloorToInt(minZ / PreviewTileSize);
        int endTileZ = Mathf.FloorToInt(maxZ / PreviewTileSize);

        int tilesX = endTileX - startTileX + 1;
        int tilesZ = endTileZ - startTileZ + 1;
        gridSize = Mathf.Clamp(Mathf.Max(tilesX, tilesZ), 1, 6);
        centerTileX = (startTileX + endTileX) / 2;
        centerTileZ = (startTileZ + endTileZ) / 2;
        ScheduleLiveRefresh();
        SetStatus("Rejilla ajustada a " + (centerTileX * PreviewTileSize) + ", "
            + (centerTileZ * PreviewTileSize) + " m (" + gridSize + " tiles/lado).", MessageType.Info);
    }

    private static bool Accumulate(float x0, float x1, float z0, float z1,
        ref float minX, ref float maxX, ref float minZ, ref float maxZ)
    {
        minX = Mathf.Min(minX, x0);
        maxX = Mathf.Max(maxX, x1);
        minZ = Mathf.Min(minZ, z0);
        maxZ = Mathf.Max(maxZ, z1);
        return true;
    }

    private void GeneratePreview(StarSystem system)
    {
        // The preview always uses the editable draft, so changes can be
        // checked before they are written to StarSystems.json.
        SyncDraftIfNeeded();
        CancelLiveRefresh();
        lastPreviewSignature = ComputePreviewSignature();

        if (system == null)
            system = BuildDraftSystem();

        if (system == null)
        {
            SetStatus("Selecciona un planeta válido.", MessageType.Warning);
            return;
        }

        CleanupPreview();

        previewRoot = new GameObject(RootNamePrefix + SanitizeName(system.Planet));
        previewRoot.transform.position = Vector3.zero;
        previewRoot.transform.rotation = Quaternion.identity;
        previewRoot.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontUnloadUnusedAsset;

        TerrainStreamer streamer = previewRoot.AddComponent<TerrainStreamer>();
        streamer.enabled = false;
        streamer.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontUnloadUnusedAsset;
        ConfigureStreamer(streamer, system);
        // Unsave POI edits feed "poiPath" road overlays in the preview too.
        streamer.poiOverride = draftHasLocation ? draftLocation : null;

        Material baseMaterial = Invoke<Material>(streamer, "LoadBaseMaterial", "objects/terrain/material/terrain");
        if (baseMaterial == null)
        {
            SetStatus("No se encontró el material objects/terrain/material/terrain.", MessageType.Error);
            CleanupPreview();
            return;
        }

        // Clone before LoadDetailTextures/LoadOverlays: those helpers write to
        // the material and the loaded Resources asset must stay untouched.
        Material previewBaseMaterial = new Material(baseMaterial);
        previewBaseMaterial.name = "PreviewBase_" + SanitizeName(system.Planet);
        previewBaseMaterial.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontUnloadUnusedAsset;
        generatedMaterials.Add(previewBaseMaterial);
        SetField(streamer, "terrainMaterial", previewBaseMaterial);
        SetField(streamer, "tileSize", PreviewTileSize);
        SetField(streamer, "heightCurve", AnimationCurve.Linear(0f, 0f, 1f, 1f));

        Texture2D[] groundTextures = Invoke<Texture2D[]>(
            streamer, "LoadTexturesFromResources", "objects/terrain/textures/");
        SetField(streamer, "terrainTextures", groundTextures);

        if (groundTextures == null || groundTextures.Length == 0)
        {
            SetStatus("No hay texturas de terreno que coincidan con: " + streamer.terrainTextureType, MessageType.Error);
            CleanupPreview();
            return;
        }

        // Cast to object: Texture2D[] would otherwise be spread across the
        // reflection params array as individual arguments.
        Invoke(streamer, "ResolveMapSiblings",
            (object)Resources.LoadAll<Texture2D>("objects/terrain/textures/"));

        Texture2D[] cliffTextures = Invoke<Texture2D[]>(
            streamer, "LoadCliffTextures", "objects/terrain/cliffs/");
        SetField(streamer, "cliffTextures", cliffTextures ?? new Texture2D[0]);

        if (!showDetail)
            streamer.detailTextureType = null;
        if (!showOverlays)
            streamer.overlayConfig = null;

        Invoke(streamer, "LoadDetailTextures");

        // ApplyDetailMaps() returns early when no detail set is loaded, which
        // would leave the base material's default _DetailEnabled value intact.
        if (!showDetail)
            previewBaseMaterial.SetFloat("_DetailEnabled", 0f);

        int verticesPerSide = streamer.cliffsEnabled && streamer.verticesPerSide > 0
            ? streamer.verticesPerSide
            : 32;

        TerrainGenerator generator = new TerrainGenerator(
            streamer.seed,
            PreviewTileSize,
            verticesPerSide,
            streamer.maxTerrainHeight,
            AnimationCurve.Linear(0f, 0f, 1f, 1f));

        CopyGeneratorSettings(streamer, generator);
        SetField(streamer, "terrainGenerator", generator);

        // Road overlays need the configured generator before masks are sampled.
        Invoke(streamer, "LoadOverlays");

        // Build the same mesh/material prototypes used by TerrainStreamer at
        // runtime. Runtime materials are tracked so repeated previews don't leak.
        VegetationPrototype[] vegetationPrototypes = null;
        if (showVegetation && streamer.vegetationConfig != null
            && streamer.vegetationConfig.Length > 0)
        {
            vegetationPrototypes = Invoke<VegetationPrototype[]>(
                streamer, "BuildVegetationPrototypes");

            if (vegetationPrototypes != null)
            {
                foreach (VegetationPrototype prototype in vegetationPrototypes)
                {
                    if (prototype != null && prototype.material != null)
                        generatedMaterials.Add(prototype.material);
                }
            }
        }

        // Distance culling needs a viewer. In the editor there is no ship, so
        // place a dummy one over the centre tile being previewed.
        GameObject viewer = new GameObject("PreviewVegetationViewer");
        viewer.transform.SetParent(previewRoot.transform);
        viewer.transform.localPosition = new Vector3(
            (centerTileX + 0.5f) * PreviewTileSize,
            streamer.maxTerrainHeight,
            (centerTileZ + 0.5f) * PreviewTileSize);
        viewer.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontUnloadUnusedAsset;
        previewVegetationViewer = viewer.transform;

        int startX = centerTileX - Mathf.FloorToInt(gridSize / 2f);
        int startZ = centerTileZ - Mathf.FloorToInt(gridSize / 2f);
        int generatedTiles = 0;

        for (int z = startZ; z < startZ + gridSize; z++)
        {
            for (int x = startX; x < startX + gridSize; x++)
            {
                Vector2Int coordinate = new Vector2Int(x, z);
                Mesh mesh = generator.GenerateMesh(coordinate);
                mesh.name = "PreviewMesh_" + x + "_" + z;
                mesh.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontUnloadUnusedAsset;
                generatedMeshes.Add(mesh);

                GameObject tile = new GameObject("PreviewTile_" + x + "_" + z);
                tile.transform.SetParent(previewRoot.transform);
                tile.transform.localPosition = new Vector3(
                    x * PreviewTileSize, 0f, z * PreviewTileSize);
                tile.transform.localRotation = Quaternion.identity;
                tile.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontUnloadUnusedAsset;

                MeshFilter meshFilter = tile.AddComponent<MeshFilter>();
                meshFilter.sharedMesh = mesh;

                // Ground sampling for props/herds uses these colliders, so the
                // placement matches the interpolated runtime mesh (not just the
                // continuous noise height) and prefabs do not float or sink.
                MeshCollider meshCollider = tile.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = mesh;

                MeshRenderer renderer = tile.AddComponent<MeshRenderer>();

                Material tileMaterial = new Material(previewBaseMaterial);
                tileMaterial.name = "PreviewTileMaterial_" + x + "_" + z;
                tileMaterial.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontUnloadUnusedAsset;
                generatedMaterials.Add(tileMaterial);

                int textureIndex = Invoke<int>(streamer, "GetFinalTextureIndex", coordinate);
                Invoke(streamer, "SetTileGroundMaps", tileMaterial, textureIndex);
                Invoke(streamer, "SetNeighborTextures", tileMaterial, coordinate);
                Invoke(streamer, "SetTileCliffMaps", tileMaterial, coordinate);

                renderer.sharedMaterial = tileMaterial;

                if (vegetationPrototypes != null)
                {
                    foreach (VegetationPrototype prototype in vegetationPrototypes)
                    {
                        TileVegetation.Attach(
                            tile, coordinate, PreviewTileSize,
                            generator, prototype, previewVegetationViewer);
                    }
                }

                generatedTiles++;
            }
        }

        previewGenerator = generator;
        Physics.SyncTransforms();
        SpawnPoiContent(generator, startX, startZ);

        FramePreview();
        SetStatus(
            "Generadas " + generatedTiles + " tiles de " + system.Planet
            + " con " + verticesPerSide + " vértices/lado. " + DescribeOverlays(streamer)
            + " " + DescribeVegetation(vegetationPrototypes, generatedTiles)
            + " " + DescribePoiContent(),
            MessageType.Info);
    }

    // Instantiates the real PlanetPOIs.json prefabs (POIs are drawn by the
    // scene-view gizmos) on top of the preview terrain, using the same
    // swarm/herd layout, scale rules and ground clamping as the runtime.
    private void SpawnPoiContent(TerrainGenerator generator, int startTileX, int startTileZ)
    {
        previewHasPoiContent = false;
        previewPropsInside = 0;
        previewPropsOutside = 0;
        previewPropsRelaxed = 0;
        previewCreaturesInside = 0;
        previewCreaturesOutside = 0;

        if (draftLocation == null || !draftHasLocation)
            return;

        NormalizeLocationArrays(draftLocation);

        GameObject contentRoot = new GameObject("PoiProps");
        contentRoot.transform.SetParent(previewRoot.transform, false);
        contentRoot.transform.localPosition = Vector3.zero;
        contentRoot.transform.localRotation = Quaternion.identity;
        contentRoot.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontUnloadUnusedAsset;

        bool hasContent = draftLocation.pois.Length > 0
            || draftLocation.props.Length > 0
            || draftLocation.sites.Length > 0
            || draftLocation.creatures.Length > 0;
        previewHasPoiContent = hasContent;

        float minX = startTileX * PreviewTileSize;
        float maxX = minX + gridSize * PreviewTileSize;
        float minZ = startTileZ * PreviewTileSize;
        float maxZ = minZ + gridSize * PreviewTileSize;

        if (showProps)
        {
            int relaxedPlacements;
            List<string> placementWarnings;
            List<PlanetPropPlacement> placements = PlanetContentUtility.BuildPlacements(
                draftLocation, out relaxedPlacements, out placementWarnings);
            previewPropsRelaxed = relaxedPlacements;

            foreach (string warning in placementWarnings)
                Debug.LogWarning("[TerrainPreview] " + warning);

            foreach (PlanetPropPlacement placement in placements)
            {
                if (string.IsNullOrEmpty(placement.type))
                    continue;

                SpawnPropPreview(placement, contentRoot.transform, generator,
                    minX, maxX, minZ, maxZ);
            }
        }

        if (showCreatures)
        {
            foreach (PlanetPOICreature creature in draftLocation.creatures)
            {
                if (creature == null || string.IsNullOrEmpty(creature.prefab))
                    continue;

                SpawnCreaturePreview(creature, contentRoot.transform, generator, minX, maxX, minZ, maxZ);
            }
        }
    }

    private void SpawnPropPreview(PlanetPropPlacement placement, Transform parent,
        TerrainGenerator generator, float minX, float maxX, float minZ, float maxZ)
    {
        PropType def = FindPropType(placement.type);
        string folder = def != null && !string.IsNullOrEmpty(def.folder)
            ? def.folder
            : "objects/vehicles/";
        string prefabName = def != null && !string.IsNullOrEmpty(def.prefab)
            ? def.prefab
            : placement.type;
        GameObject prefab = Resources.Load<GameObject>(folder + prefabName);

        if (prefab == null)
        {
            Debug.LogWarning("[TerrainPreview] Prop prefab not found: " + folder + prefabName);
            return;
        }

        bool inside = placement.x >= minX && placement.x <= maxX
            && placement.z >= minZ && placement.z <= maxZ;

        if (!inside)
        {
            previewPropsOutside++;
            return;
        }

        previewPropsInside++;
        Quaternion baseRotation = prefab.transform.localRotation;
        GameObject go = Instantiate(prefab);
        go.name = "PreviewProp_" + placement.type;
        go.transform.SetParent(parent, true);
        float groundY = SamplePreviewGround(generator, placement.x, placement.z);
        go.transform.position = new Vector3(placement.x, groundY, placement.z);
        // Keep the prefab's own correction rotation (e.g. -90 on X). The
        // placement yaw is applied afterwards, on top of it.
        go.transform.localRotation = baseRotation;
        SetPreviewHideFlags(go);

        if (def != null && def.targetLength > 0f)
        {
            // Measure before yaw: otherwise a 45-degree prop gets a bigger
            // AABB and would be scaled differently from the same prop at 0.
            Bounds rawBounds = GetPreviewBounds(go);
            float longest = Mathf.Max(rawBounds.size.x, rawBounds.size.y, rawBounds.size.z);

            if (longest > 0.01f)
                go.transform.localScale *= def.targetLength / longest;
        }

        go.transform.localScale *= placement.scaleMul;
        Bounds bounds = GetPreviewBounds(go);
        float lift = groundY - bounds.min.y;
        go.transform.position += Vector3.up * lift;
        go.transform.localRotation = Quaternion.Euler(0f, placement.rotY, 0f) * baseRotation;
    }

    private void SpawnCreaturePreview(PlanetPOICreature creature, Transform parent,
        TerrainGenerator generator, float minX, float maxX, float minZ, float maxZ)
    {
        GameObject prefab = Resources.Load<GameObject>("objects/creatures/" + creature.prefab);

        if (prefab == null)
        {
            Debug.LogWarning("[TerrainPreview] Creature prefab not found: objects/creatures/" + creature.prefab);
            return;
        }

        Quaternion baseRotation = prefab.transform.localRotation;
        System.Random rng = new System.Random(creature.seed);
        int min = Mathf.Max(1, creature.herdMin);
        int max = Mathf.Max(min, creature.herdMax);
        int count = min + rng.Next(max - min + 1);
        float herdRadius = Mathf.Max(creature.herdRadius, 1f);
        float groundOffset = GetCreatureGroundOffset(prefab);
        Vector3 home = new Vector3(creature.x, 0f, creature.z);
        System.Random layoutRng = new System.Random(creature.seed * 31 + 7);

        for (int i = 0; i < count; i++)
        {
            double angle = layoutRng.NextDouble() * Mathf.PI * 2.0;
            double distance = herdRadius * Math.Sqrt((float)layoutRng.NextDouble());
            float px = home.x + (float)(Math.Cos(angle) * distance);
            float pz = home.z + (float)(Math.Sin(angle) * distance);

            bool inside = px >= minX && px <= maxX && pz >= minZ && pz <= maxZ;
            if (!inside)
            {
                previewCreaturesOutside++;
                continue;
            }

            previewCreaturesInside++;
            float ground = SamplePreviewGround(generator, px, pz);
            GameObject go = Instantiate(prefab);
            go.name = "PreviewCreature_" + creature.prefab + "_" + i;
            go.transform.SetParent(parent, true);
            go.transform.position = new Vector3(px, ground + groundOffset, pz);
            go.transform.localRotation = Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f) * baseRotation;
            SetPreviewHideFlags(go);

            Animator animator = go.GetComponentInChildren<Animator>(true);

            if (poseCreatures && animator != null)
            {
                try
                {
                    animator.Play("Unreal Take", 0, (float)rng.NextDouble());
                    animator.Update(0f);
                }
                catch (Exception)
                {
                    // Edit-mode posing is best-effort; the prefab still shows
                    // its bind pose when the controller can't be sampled.
                }
            }
        }
    }

    private float SamplePreviewHeight(TerrainGenerator generator, float x, float z)
    {
        return generator.GetHeightAtLocalPosition(Vector2Int.zero, new Vector3(x, 0f, z));
    }

    // Raycasts the generated preview tiles (the same mesh colliders the game
    // uses), falling back to the continuous noise height when there is no tile
    // under that spot. Props already placed are ignored by name.
    private float SamplePreviewGround(TerrainGenerator generator, float x, float z)
    {
        if (previewRoot != null)
        {
            Vector3 origin = new Vector3(x, 2000f, z);
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 4000f, ~0,
                QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;

            foreach (RaycastHit hit in hits)
            {
                if (hit.transform == null
                    || !hit.transform.name.StartsWith("PreviewTile_", StringComparison.Ordinal))
                {
                    continue;
                }

                if (hit.point.y > best)
                    best = hit.point.y;
            }

            if (!float.IsNegativeInfinity(best))
                return best;
        }

        return SamplePreviewHeight(generator, x, z);
    }

    private static void SetPreviewHideFlags(GameObject go)
    {
        foreach (Transform child in go.GetComponentsInChildren<Transform>(true))
            child.gameObject.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontUnloadUnusedAsset;
    }

    private static Bounds GetPreviewBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = new Bounds(go.transform.position, Vector3.zero);
        bool initialized = false;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            if (!initialized)
            {
                bounds = renderer.bounds;
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return bounds;
    }

    private static float GetCreatureGroundOffset(GameObject prefab)
    {
        SkinnedMeshRenderer[] renderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        bool found = false;
        float lowest = 0f;

        foreach (SkinnedMeshRenderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            Vector3 min = renderer.transform.TransformPoint(renderer.localBounds.min);

            if (!found)
            {
                lowest = min.y;
                found = true;
            }
            else
            {
                lowest = Mathf.Min(lowest, min.y);
            }
        }

        return found ? -lowest : 0.97f;
    }

    private PropType FindPropType(string prefabName)
    {
        if (propTypes == null || propTypes.propTypesData == null || string.IsNullOrEmpty(prefabName))
            return null;

        foreach (PropType def in propTypes.propTypesData)
        {
            if (def == null)
                continue;

            if (string.Equals(def.type, prefabName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(def.prefab, prefabName, StringComparison.OrdinalIgnoreCase))
            {
                return def;
            }
        }

        return null;
    }

    private string DescribePoiContent()
    {
        if (!draftHasLocation)
            return "POIs: sin entrada.";

        int pois = draftLocation != null && draftLocation.pois != null ? draftLocation.pois.Length : 0;
        int props = draftLocation != null && draftLocation.props != null ? draftLocation.props.Length : 0;
        int sites = draftLocation != null && draftLocation.sites != null ? draftLocation.sites.Length : 0;
        int herds = draftLocation != null && draftLocation.creatures != null ? draftLocation.creatures.Length : 0;

        string text = "POIs: " + pois + ", props: " + previewPropsInside
            + "/" + (previewPropsInside + previewPropsOutside)
            + " (" + props + " aislados, " + sites + " asentamientos), criaturas: "
            + previewCreaturesInside
            + "/" + (previewCreaturesInside + previewCreaturesOutside)
            + " (" + herds + " rebaños).";

        if (previewPropsOutside + previewCreaturesOutside > 0)
            text += " Fuera de la rejilla: " + (previewPropsOutside + previewCreaturesOutside)
                + ". Usa 'Ajustar rejilla a POIs, props y asentamientos'.";

        if (previewPropsRelaxed > 0)
            text += " Props con separación reducida: " + previewPropsRelaxed
                + " (no caben con ese radio/separación).";

        return text;
    }


    private static void ConfigureStreamer(TerrainStreamer streamer, StarSystem system)
    {
        streamer.seed = system.seed != 0 ? system.seed : 1138;
        streamer.planetName = system.Planet;
        streamer.terrainTextureType = GetTerrainTextureKeywords(system);
        streamer.maxTerrainHeight = 250f;
        streamer.tileRadiusInKilometres = 3f;
        streamer.outerTileRadiusInKilometres = 8f;
        streamer.plainsAmp = 25f;
        streamer.desertAmp = 90f;
        streamer.hillsAmp = 120f;
        streamer.mountAmp = 220f;

        CliffSettings cliffs = system.cliffs;
        if (cliffs != null && cliffs.enabled)
        {
            streamer.verticesPerSide = cliffs.verticesPerSide > 0 ? cliffs.verticesPerSide : 48;
            streamer.outerVerticesPerSide = cliffs.outerVerticesPerSide > 0 ? cliffs.outerVerticesPerSide : 32;
            streamer.tileRadiusInKilometres = cliffs.highDetailRadiusKm > 0f ? cliffs.highDetailRadiusKm : 5f;
            streamer.cliffsEnabled = true;
            streamer.cliffMesaHeight = cliffs.mesaHeight > 0f ? cliffs.mesaHeight : 60f;
            streamer.cliffMesaRoughness = cliffs.mesaRoughness > 0f ? cliffs.mesaRoughness : 25f;
            streamer.cliffEdgeWidth = cliffs.edgeWidth > 0f ? cliffs.edgeWidth : 0.02f;
            streamer.cliffCanyonDepth = cliffs.canyonDepth > 0f ? cliffs.canyonDepth : 55f;
            streamer.cliffCanyonWidth = cliffs.canyonWidth > 0f ? cliffs.canyonWidth : 0.06f;
            streamer.cliffStrength = cliffs.strength > 0f ? cliffs.strength : 1f;
            streamer.cliffCoverage = cliffs.coverage > 0f ? cliffs.coverage : 1f;
            streamer.cliffRegionFrequency = cliffs.regionFrequency > 0f ? cliffs.regionFrequency : 0.00012f;
            streamer.cliffRegionFeather = cliffs.regionFeather > 0f ? cliffs.regionFeather : 0.06f;
            streamer.cliffMaskThreshold = cliffs.maskThreshold > 0f ? cliffs.maskThreshold : 0.45f;
            streamer.cliffMaskFrequency = cliffs.maskFrequency > 0f ? cliffs.maskFrequency : 0.00045f;
            streamer.cliffSlopeStart = cliffs.slopeStart > 0f ? cliffs.slopeStart : 0.72f;
            streamer.cliffSlopeSoftness = cliffs.slopeSoftness > 0f ? cliffs.slopeSoftness : 0.15f;
            streamer.cliffTextureType = string.IsNullOrEmpty(cliffs.textureType)
                ? "cliff"
                : cliffs.textureType;
        }
        else
        {
            streamer.cliffsEnabled = false;
        }

        streamer.detailTextureType = system.detailTexture;
        streamer.overlayConfig = system.overlays;
        streamer.vegetationConfig = system.vegetation;
        streamer.vegetationRadiusInKilometres = 8f;
    }

    private static void CopyGeneratorSettings(TerrainStreamer streamer, TerrainGenerator generator)
    {
        generator.plainsAmp = streamer.plainsAmp;
        generator.desertAmp = streamer.desertAmp;
        generator.hillsAmp = streamer.hillsAmp;
        generator.mountAmp = streamer.mountAmp;

        generator.cliffsEnabled = streamer.cliffsEnabled;
        generator.cliffMesaHeight = streamer.cliffMesaHeight;
        generator.cliffMesaRoughness = streamer.cliffMesaRoughness;
        generator.cliffEdgeWidth = streamer.cliffEdgeWidth;
        generator.cliffCanyonDepth = streamer.cliffCanyonDepth;
        generator.cliffCanyonWidth = streamer.cliffCanyonWidth;
        generator.cliffStrength = streamer.cliffStrength;
        generator.cliffCoverage = streamer.cliffCoverage;
        generator.cliffRegionFrequency = streamer.cliffRegionFrequency;
        generator.cliffRegionFeather = streamer.cliffRegionFeather;
        generator.cliffMaskThreshold = streamer.cliffMaskThreshold;
        generator.cliffMaskFrequency = streamer.cliffMaskFrequency;
    }

    private static string GetTerrainTextureKeywords(StarSystem system)
    {
        if (system != null && !string.IsNullOrEmpty(system.terrainTexture))
            return system.terrainTexture;

        string planetType = system != null ? system.planetType : null;
        if (!string.IsNullOrEmpty(planetType))
        {
            TextAsset asset = Resources.Load<TextAsset>(OGGetAddress.files + "PlanetTypes");
            if (asset != null)
            {
                PlanetTypes planetTypes = JsonUtility.FromJson<PlanetTypes>(asset.text);
                if (planetTypes != null && planetTypes.planetTypesData != null)
                {
                    foreach (PlanetType def in planetTypes.planetTypesData)
                    {
                        if (def != null
                            && string.Equals(def.planetType, planetType, StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrEmpty(def.terrainTexture))
                        {
                            return def.terrainTexture;
                        }
                    }
                }
            }
        }

        return "sandy";
    }

    private void FramePreview()
    {
        if (previewRoot == null)
            return;
        Bounds bounds = new Bounds(previewRoot.transform.position, Vector3.zero);
        bool haveBounds = false;

        foreach (MeshRenderer renderer in previewRoot.GetComponentsInChildren<MeshRenderer>())
        {
            if (!haveBounds)
            {
                bounds = renderer.bounds;
                haveBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (!haveBounds)
            return;

        Vector3 focus = bounds.center;
        float distance = Mathf.Max(10f, bounds.extents.magnitude * 1.35f);
        bool framedPoiContent = false;

        // Props and herds are small compared to a 1-2 km patch, so frame their
        // actual positions instead of the whole terrain when they are shown.
        if (previewHasPoiContent && previewGenerator != null)
        {
            Vector3 contentFocus;
            float contentRadius;

            if (TryGetPoiContentFocus(out contentFocus, out contentRadius))
            {
                focus = contentFocus;
                distance = Mathf.Clamp(contentRadius * 2.4f, 90f, 1600f);
                framedPoiContent = true;
            }
        }

        // Bushes are 1-3 m tall and disappear at the distance needed to frame a
        // whole 1-2 km preview. When vegetation is enabled, frame the centre
        // tile up close so the instanced bushes can actually be inspected.
        if (!framedPoiContent && showVegetation && previewVegetationViewer != null)
        {
            focus = new Vector3(
                previewVegetationViewer.position.x,
                bounds.center.y,
                previewVegetationViewer.position.z);
            distance = 220f;
        }

        SceneView view = SceneView.lastActiveSceneView;
        if (view != null)
        {
            view.LookAt(focus, Quaternion.Euler(35f, 45f, 0f), distance);
            view.Repaint();
        }

        SceneView.RepaintAll();
    }

    private bool TryGetPoiContentFocus(out Vector3 focus, out float radius)
    {
        focus = Vector3.zero;
        radius = 0f;

        if (draftLocation == null || previewGenerator == null)
            return false;

        NormalizeLocationArrays(draftLocation);

        int startTileX = centerTileX - Mathf.FloorToInt(gridSize / 2f);
        int startTileZ = centerTileZ - Mathf.FloorToInt(gridSize / 2f);
        float gridMinX = startTileX * PreviewTileSize;
        float gridMinZ = startTileZ * PreviewTileSize;
        float gridMaxX = gridMinX + gridSize * PreviewTileSize;
        float gridMaxZ = gridMinZ + gridSize * PreviewTileSize;

        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minZ = float.PositiveInfinity;
        float maxZ = float.NegativeInfinity;

        if (showPoiMarkers)
        {
            foreach (PlanetPOIEntry poi in draftLocation.pois)
            {
                if (poi == null || !IsInsideGrid(poi.x, poi.z, gridMinX, gridMaxX, gridMinZ, gridMaxZ))
                    continue;
                Accumulate(poi.x - poi.radius, poi.x + poi.radius,
                    poi.z - poi.radius, poi.z + poi.radius,
                    ref minX, ref maxX, ref minZ, ref maxZ);
            }
        }

        if (showProps)
        {
            foreach (PlanetPOIProp prop in draftLocation.props)
            {
                if (prop == null || !IsInsideGrid(prop.x, prop.z, gridMinX, gridMaxX, gridMinZ, gridMaxZ))
                    continue;
                float extent = Mathf.Max(prop.radius, 30f);
                Accumulate(prop.x - extent, prop.x + extent,
                    prop.z - extent, prop.z + extent,
                    ref minX, ref maxX, ref minZ, ref maxZ);
            }

            foreach (PlanetPOISite site in draftLocation.sites)
            {
                if (site == null || !IsInsideGrid(site.x, site.z,
                        gridMinX, gridMaxX, gridMinZ, gridMaxZ))
                {
                    continue;
                }

                float extent = Mathf.Max(site.radius, 30f);
                Accumulate(site.x - extent, site.x + extent,
                    site.z - extent, site.z + extent,
                    ref minX, ref maxX, ref minZ, ref maxZ);
            }
        }

        if (showCreatures)
        {
            foreach (PlanetPOICreature creature in draftLocation.creatures)
            {
                if (creature == null || !IsInsideGrid(creature.x, creature.z,
                        gridMinX, gridMaxX, gridMinZ, gridMaxZ))
                    continue;
                float extent = Mathf.Max(creature.wanderRadius, 40f);
                Accumulate(creature.x - extent, creature.x + extent,
                    creature.z - extent, creature.z + extent,
                    ref minX, ref maxX, ref minZ, ref maxZ);
            }
        }

        if (float.IsPositiveInfinity(minX))
            return false;

        float centerX = (minX + maxX) * 0.5f;
        float centerZ = (minZ + maxZ) * 0.5f;
        focus = new Vector3(centerX, SamplePreviewGround(previewGenerator, centerX, centerZ), centerZ);
        radius = Mathf.Max(maxX - minX, maxZ - minZ) * 0.5f;
        return true;
    }

    private static bool IsInsideGrid(float x, float z,
        float gridMinX, float gridMaxX, float gridMinZ, float gridMaxZ)
    {
        return x >= gridMinX && x <= gridMaxX && z >= gridMinZ && z <= gridMaxZ;
    }

    // POI/radius helpers drawn in the scene view, independent of the spawned
    // prefabs, so placement can be tuned even when a prefab fails to load.
    private void HandleSceneGui(SceneView view)
    {
        if (previewRoot == null || previewGenerator == null)
            return;

        if (showCalculatedRoute)
            DrawCalculatedRouteGizmos();

        if (!showPoiMarkers || draftLocation == null || !draftHasLocation)
            return;

        NormalizeLocationArrays(draftLocation);
        Color previousColor = Handles.color;

        foreach (PlanetPOIEntry poi in draftLocation.pois)
        {
            if (poi == null)
                continue;

            Vector3 position = new Vector3(
                poi.x,
                SamplePreviewGround(previewGenerator, poi.x, poi.z),
                poi.z);
            Handles.color = new Color(1f, 0.72f, 0.25f, 0.95f);
            Handles.DrawWireDisc(position, Vector3.up, Mathf.Max(poi.radius, 1f));
            Handles.DrawLine(position, position + Vector3.up * 750f);
            Handles.Label(position + Vector3.up * 770f,
                string.IsNullOrEmpty(poi.name) ? "POI" : poi.name);
        }

        foreach (PlanetPOIProp prop in draftLocation.props)
        {
            if (prop == null || prop.count <= 1 || prop.radius <= 0f)
                continue;

            string type = PlanetContentUtility.GetPropType(prop);
            Vector3 position = new Vector3(
                prop.x,
                SamplePreviewGround(previewGenerator, prop.x, prop.z),
                prop.z);
            Handles.color = new Color(0.35f, 0.8f, 1f, 0.9f);
            Handles.DrawWireDisc(position, Vector3.up, prop.radius);
            Handles.Label(position + Vector3.up * 40f, type + " x" + prop.count);
        }

        foreach (PlanetPOISite site in draftLocation.sites)
        {
            if (site == null)
                continue;

            Vector3 position = new Vector3(
                site.x,
                SamplePreviewGround(previewGenerator, site.x, site.z),
                site.z);
            Handles.color = new Color(0.9f, 0.55f, 1f, 0.9f);
            Handles.DrawWireDisc(position, Vector3.up, Mathf.Max(site.radius, 1f));
            Handles.Label(position + Vector3.up * 55f,
                (string.IsNullOrEmpty(site.name) ? "Asentamiento" : site.name)
                + " [" + PlanetContentUtility.NormalizeLayout(site.layout) + "]");
        }

        foreach (PlanetPOICreature creature in draftLocation.creatures)
        {
            if (creature == null)
                continue;

            Vector3 position = new Vector3(
                creature.x,
                SamplePreviewGround(previewGenerator, creature.x, creature.z),
                creature.z);
            Handles.color = new Color(0.45f, 1f, 0.55f, 0.85f);
            Handles.DrawWireDisc(position, Vector3.up, Mathf.Max(creature.herdRadius, 1f));
            Handles.color = new Color(0.45f, 1f, 0.55f, 0.35f);
            Handles.DrawWireDisc(position, Vector3.up, Mathf.Max(creature.wanderRadius, 1f));
            Handles.Label(position + Vector3.up * 30f, creature.prefab);
        }

        Handles.color = previousColor;
    }

    private void DrawCalculatedRouteGizmos()
    {
        TerrainStreamer streamer = previewRoot.GetComponent<TerrainStreamer>();
        if (streamer == null)
            return;

        Array overlays = GetField<Array>(streamer, "overlays");
        if (overlays == null)
            return;

        Color previousColor = Handles.color;

        foreach (object overlay in overlays)
        {
            if (overlay == null)
                continue;

            Type type = overlay.GetType();
            FieldInfo activeField = type.GetField("active", InstanceFlags);
            if (activeField == null || !(bool)activeField.GetValue(overlay))
                continue;

            Array segments = GetField<Array>(overlay, "segments");
            int count = GetField<int>(overlay, "segmentCount");
            if (segments == null || count <= 0)
                continue;

            string status = GetField<string>(overlay, "routeStatus");
            bool trimmed = !string.IsNullOrEmpty(status)
                && status.IndexOf("recort", StringComparison.OrdinalIgnoreCase) >= 0;

            Handles.color = trimmed
                ? new Color(1f, 0.45f, 0.2f, 0.95f)
                : new Color(0.25f, 1f, 0.55f, 0.9f);
            Vector3 labelPosition = Vector3.zero;

            for (int i = 0; i < count && i < segments.Length; i++)
            {
                Vector4 segment = (Vector4)segments.GetValue(i);
                Vector3 start = new Vector3(
                    segment.x,
                    SamplePreviewGround(previewGenerator, segment.x, segment.y) + 4f,
                    segment.y);
                Vector3 end = new Vector3(
                    segment.z,
                    SamplePreviewGround(previewGenerator, segment.z, segment.w) + 4f,
                    segment.w);

                Handles.DrawLine(start, end);

                if (i == 0)
                    labelPosition = (start + end) * 0.5f;
            }

            if (!string.IsNullOrEmpty(status))
                Handles.Label(labelPosition + Vector3.up * 22f, status);
        }

        Handles.color = previousColor;
    }

    private static string DescribeOverlays(TerrainStreamer streamer)
    {
        Array overlays = GetField<Array>(streamer, "overlays");
        if (overlays == null)
            return "Overlays: 0.";

        int active = 0;
        int segments = 0;
        string routeStatus = "";

        foreach (object overlay in overlays)
        {
            if (overlay == null)
                continue;

            Type type = overlay.GetType();
            FieldInfo activeField = type.GetField("active", InstanceFlags);
            if (activeField == null || !(bool)activeField.GetValue(overlay))
                continue;

            active++;
            FieldInfo vectorField = type.GetField("pE", InstanceFlags);
            if (vectorField != null)
                segments += Mathf.RoundToInt(((Vector4)vectorField.GetValue(overlay)).z);

            FieldInfo statusField = type.GetField("routeStatus", InstanceFlags);
            if (statusField != null)
            {
                string status = statusField.GetValue(overlay) as string;
                if (!string.IsNullOrEmpty(status))
                {
                    routeStatus = string.IsNullOrEmpty(routeStatus)
                        ? status
                        : routeStatus + " | " + status;
                }
            }
        }

        string result = "Overlays activos: " + active
            + " (" + segments + " segmentos de ruta).";

        return string.IsNullOrEmpty(routeStatus)
            ? result
            : result + " " + routeStatus;
    }

    private static string DescribeVegetation(VegetationPrototype[] prototypes, int tiles)
    {
        if (prototypes == null || prototypes.Length == 0)
            return "Vegetación: 0.";

        int instances = 0;
        foreach (VegetationPrototype prototype in prototypes)
        {
            if (prototype != null && prototype.settings != null)
                instances += Mathf.Max(0, prototype.settings.countPerTile) * tiles;
        }

        return "Vegetación: " + prototypes.Length + " tipo(s), ~" + instances + " instancias.";
    }

    private void CleanupPreview()
    {
        if (previewRoot != null)
        {
            TerrainStreamer streamer = previewRoot.GetComponent<TerrainStreamer>();
            if (streamer != null)
            {
                Dictionary<Vector2Int, GameObject> loaded =
                    GetField<Dictionary<Vector2Int, GameObject>>(streamer, "loadedTiles");
                if (loaded != null)
                    loaded.Clear();
                SetField(streamer, "sharedOuterMaterial", null);
            }

            DestroyImmediate(previewRoot);
            previewRoot = null;
            previewVegetationViewer = null;
        }

        previewGenerator = null;
        previewHasPoiContent = false;
        previewPropsInside = 0;
        previewPropsOutside = 0;
        previewPropsRelaxed = 0;
        previewCreaturesInside = 0;
        previewCreaturesOutside = 0;

        foreach (Mesh mesh in generatedMeshes)
        {
            if (mesh != null)
                DestroyImmediate(mesh);
        }
        generatedMeshes.Clear();

        foreach (Material material in generatedMaterials)
        {
            if (material != null)
                DestroyImmediate(material);
        }
        generatedMaterials.Clear();
    }

    [Serializable]
    private class OverlayArray
    {
        public TerrainOverlay[] items;
    }

    [Serializable]
    private class VegetationArray
    {
        public VegetationSettings[] items;
    }

    private void SyncDraftIfNeeded(bool force = false)
    {
        if (!force
            && string.Equals(draftPlanet, selectedPlanet, StringComparison.OrdinalIgnoreCase)
            && draftOverlays != null)
        {
            return;
        }

        StarSystem system = FindSelectedSystem();
        draftPlanet = selectedPlanet ?? "";

        if (system == null)
        {
            draftPlanetType = "";
            draftSeed = 1138;
            draftTerrainTexture = "";
            draftDetailTexture = "";
            draftHasCliffs = false;
            draftCliffsFromJson = false;
            draftCliffs = new CliffSettings();
            draftOverlays = new TerrainOverlay[0];
            draftVegetation = new VegetationSettings[0];
        }
        else
        {
            draftPlanetType = system.planetType ?? "";
            draftSeed = system.seed;
            draftTerrainTexture = system.terrainTexture ?? "";
            draftDetailTexture = system.detailTexture ?? "";
            draftHasCliffs = system.cliffs != null;
            draftCliffsFromJson = draftHasCliffs;
            draftCliffs = draftHasCliffs ? CloneCliffs(system.cliffs) : new CliffSettings();
            draftOverlays = CloneOverlays(system.overlays);
            draftVegetation = CloneVegetation(system.vegetation);
        }

        if (overlayFoldouts == null || overlayFoldouts.Length < 3)
            overlayFoldouts = new bool[3];

        SyncPoiDraftIfNeeded(force);
    }

    private void SyncPoiDraftIfNeeded(bool force = false)
    {
        if (!force
            && draftLocation != null
            && string.Equals(draftLocationPlanet, selectedPlanet, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        draftLocationPlanet = selectedPlanet ?? "";
        PlanetPOILocation location = FindSelectedLocation();

        if (location == null)
        {
            draftHasLocation = false;
            draftLocation = new PlanetPOILocation
            {
                planet = draftLocationPlanet,
                pois = new PlanetPOIEntry[0],
                props = new PlanetPOIProp[0],
                sites = new PlanetPOISite[0],
                creatures = new PlanetPOICreature[0]
            };
        }
        else
        {
            draftHasLocation = true;
            draftLocation = CloneLocation(location);
            draftLocation.planet = draftLocationPlanet;
        }

        NormalizeLocationArrays(draftLocation);
    }

    private static PlanetPOILocation CloneLocation(PlanetPOILocation source)
    {
        if (source == null)
            return new PlanetPOILocation();

        PlanetPOILocation copy = JsonUtility.FromJson<PlanetPOILocation>(JsonUtility.ToJson(source));
        return copy ?? new PlanetPOILocation();
    }

    private StarSystem BuildDraftSystem()
    {
        if (string.IsNullOrEmpty(selectedPlanet))
            return null;

        return new StarSystem
        {
            Planet = selectedPlanet,
            planetType = draftPlanetType,
            seed = draftSeed,
            terrainTexture = draftTerrainTexture,
            detailTexture = draftDetailTexture,
            cliffs = draftHasCliffs ? CloneCliffs(draftCliffs) : null,
            overlays = CloneOverlays(draftOverlays),
            vegetation = CloneVegetation(draftVegetation)
        };
    }

    private static CliffSettings CloneCliffs(CliffSettings source)
    {
        if (source == null)
            return new CliffSettings();

        CliffSettings copy = JsonUtility.FromJson<CliffSettings>(JsonUtility.ToJson(source));
        return copy ?? new CliffSettings();
    }

    private static TerrainOverlay[] CloneOverlays(TerrainOverlay[] source)
    {
        if (source == null)
            return new TerrainOverlay[0];

        OverlayArray wrapper = JsonUtility.FromJson<OverlayArray>(
            JsonUtility.ToJson(new OverlayArray { items = source }));

        return wrapper != null && wrapper.items != null ? wrapper.items : new TerrainOverlay[0];
    }

    private static VegetationSettings[] CloneVegetation(VegetationSettings[] source)
    {
        if (source == null)
            return new VegetationSettings[0];

        VegetationArray wrapper = JsonUtility.FromJson<VegetationArray>(
            JsonUtility.ToJson(new VegetationArray { items = source }));

        return wrapper != null && wrapper.items != null
            ? wrapper.items
            : new VegetationSettings[0];
    }

    private void RemoveOverlayAt(int index)
    {
        if (draftOverlays == null || index < 0 || index >= draftOverlays.Length)
            return;

        List<TerrainOverlay> list = new List<TerrainOverlay>(draftOverlays);
        list.RemoveAt(index);
        draftOverlays = list.ToArray();

        if (overlayFoldouts == null || index >= overlayFoldouts.Length)
            return;

        for (int i = index; i < overlayFoldouts.Length - 1; i++)
            overlayFoldouts[i] = overlayFoldouts[i + 1];
        overlayFoldouts[overlayFoldouts.Length - 1] = false;
    }

    private static int ModeToIndex(string mode)
    {
        int index = Array.FindIndex(OverlayModes,
            candidate => string.Equals(candidate, mode, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? 0 : index;
    }

    private void ApplyConfigToJson()
    {
        SyncDraftIfNeeded();

        if (string.IsNullOrEmpty(selectedPlanet))
        {
            SetStatus("Selecciona un planeta válido.", MessageType.Warning);
            return;
        }

        try
        {
            string path = GetStarSystemsPath();
            if (!File.Exists(path))
            {
                SetStatus("No se encontró " + path, MessageType.Error);
                return;
            }

            string json = File.ReadAllText(path);
            int objectStart;
            int objectEnd;
            if (!TryFindPlanetObject(json, selectedPlanet, out objectStart, out objectEnd))
            {
                SetStatus("No se encontró el objeto del planeta '" + selectedPlanet + "' en el JSON.", MessageType.Error);
                return;
            }

            string planetObject = json.Substring(objectStart, objectEnd - objectStart + 1);
            string nl = json.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";

            planetObject = ReplaceJsonField(planetObject, "seed",
                draftSeed.ToString(CultureInfo.InvariantCulture),
                GetFieldIndent(planetObject, "seed", nl), nl);

            planetObject = ReplaceJsonField(planetObject, "planetType",
                JsonString(draftPlanetType),
                GetFieldIndent(planetObject, "planetType", nl), nl);

            planetObject = ReplaceJsonField(planetObject, "terrainTexture",
                JsonString(draftTerrainTexture),
                GetFieldIndent(planetObject, "terrainTexture", nl), nl);

            planetObject = ReplaceJsonField(planetObject, "detailTexture",
                JsonString(draftDetailTexture),
                GetFieldIndent(planetObject, "detailTexture", nl), nl);

            if ((draftHasCliffs || draftCliffsFromJson) && draftCliffs != null)
            {
                string cliffIndent = GetFieldIndent(planetObject, "cliffs", nl);
                planetObject = ReplaceJsonField(planetObject, "cliffs",
                    SerializeCliffs(draftCliffs, cliffIndent, nl), cliffIndent, nl);
            }

            string overlayIndent = GetFieldIndent(planetObject, "overlays", nl);
            planetObject = ReplaceJsonField(planetObject, "overlays",
                SerializeOverlays(draftOverlays, overlayIndent, nl), overlayIndent, nl);

            string vegetationIndent = GetFieldIndent(planetObject, "vegetation", nl);
            planetObject = ReplaceJsonField(planetObject, "vegetation",
                SerializeVegetation(draftVegetation, vegetationIndent, nl), vegetationIndent, nl);

            string updated = json.Substring(0, objectStart)
                + planetObject
                + json.Substring(objectEnd + 1);

            File.WriteAllText(path, updated);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            // Reload StarSystems.json without discarding unsaved POI edits.
            PlanetPOILocation savedLocation = draftLocation;
            bool savedHasLocation = draftHasLocation;
            string savedLocationPlanet = draftLocationPlanet;

            LoadData();

            draftLocation = savedLocation;
            draftHasLocation = savedHasLocation;
            draftLocationPlanet = savedLocationPlanet;

            CancelLiveRefresh();

            if (regenerateAfterApply)
                GeneratePreview(null);

            SetStatus("Configuración de " + selectedPlanet + " guardada en StarSystems.json.", MessageType.Info);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            SetStatus("No se pudo guardar el JSON: " + exception.Message, MessageType.Error);
        }
    }

    private static string GetStarSystemsPath()
    {
        TextAsset asset = Resources.Load<TextAsset>(OGGetAddress.files + "StarSystems");
        string path = asset != null ? AssetDatabase.GetAssetPath(asset) : "";
        return string.IsNullOrEmpty(path) ? "Assets/Resources/files/StarSystems.json" : path;
    }

    private static string GetPlanetPoisPath()
    {
        TextAsset asset = Resources.Load<TextAsset>(OGGetAddress.files + "PlanetPOIs");
        string path = asset != null ? AssetDatabase.GetAssetPath(asset) : "";
        return string.IsNullOrEmpty(path) ? "Assets/Resources/files/PlanetPOIs.json" : path;
    }

    private void ApplyPoiConfigToJson()
    {
        SyncPoiDraftIfNeeded();

        if (!draftHasLocation || draftLocation == null || string.IsNullOrEmpty(selectedPlanet))
        {
            SetStatus("Este planeta no tiene entrada de POIs activa.", MessageType.Warning);
            return;
        }

        try
        {
            string path = GetPlanetPoisPath();
            if (!File.Exists(path))
            {
                SetStatus("No se encontró " + path, MessageType.Error);
                return;
            }

            string json = File.ReadAllText(path);
            string nl = json.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
            NormalizeLocationArrays(draftLocation);
            draftLocation.planet = selectedPlanet;

            int objectStart;
            int objectEnd;
            string updated;

            if (TryFindObjectByFieldValue(json, "planet", selectedPlanet,
                    out objectStart, out objectEnd))
            {
                string indent = GetLineIndent(json, objectStart);
                updated = json.Substring(0, objectStart)
                    + SerializePoiLocation(draftLocation, indent, nl)
                    + json.Substring(objectEnd + 1);
            }
            else
            {
                updated = InsertPoiLocation(json, draftLocation, nl);
            }

            File.WriteAllText(path, updated);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            LoadPoiData();
            SyncPoiDraftIfNeeded(true);
            lastPreviewSignature = ComputePreviewSignature();
            CancelLiveRefresh();

            if (regenerateAfterApply)
                GeneratePreview(null);

            SetStatus("POIs, props, asentamientos y criaturas de " + selectedPlanet
                + " guardados en PlanetPOIs.json.", MessageType.Info);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            SetStatus("No se pudo guardar PlanetPOIs.json: " + exception.Message, MessageType.Error);
        }
    }

    private static string InsertPoiLocation(string json, PlanetPOILocation location, string nl)
    {
        int valueStart;
        int valueEnd;

        if (!TryFindTopLevelFieldValue(json, "planetPOIsData", out valueStart, out valueEnd)
            || valueEnd <= valueStart
            || json[valueStart] != '[')
        {
            throw new InvalidOperationException(
                "El JSON no tiene un array planetPOIsData válido.");
        }

        int closeIndex = valueEnd - 1;
        int insertAt = closeIndex;

        while (insertAt > valueStart && char.IsWhiteSpace(json[insertAt - 1]))
            insertAt--;

        bool empty = insertAt <= valueStart + 1;
        string fieldIndent = GetFieldIndent(json, "planetPOIsData", nl);
        string itemIndent;
        string closeIndent;

        if (empty)
        {
            itemIndent = fieldIndent + "  ";
            closeIndent = fieldIndent;
        }
        else
        {
            int firstEntry = json.IndexOf('{', valueStart + 1);
            itemIndent = firstEntry >= 0 && firstEntry < closeIndex
                ? GetLineIndent(json, firstEntry)
                : fieldIndent + "  ";
            closeIndent = GetLineIndent(json, closeIndex);
        }

        string indent = itemIndent;
        string entry = SerializePoiLocation(location, indent, nl);

        string insert = empty
            ? nl + indent + entry + nl + closeIndent
            : "," + nl + indent + entry;

        return json.Substring(0, insertAt) + insert + json.Substring(insertAt);
    }

    private static string SerializePoiLocation(PlanetPOILocation location, string indent, string nl)
    {
        StringBuilder builder = new StringBuilder();
        string fieldIndent = indent + "  ";

        builder.Append('{').Append(nl);
        AppendField(builder, fieldIndent, "planet", JsonString(location.planet), true, nl);
        AppendField(builder, fieldIndent, "entryX", Num(location.entryX), true, nl);
        AppendField(builder, fieldIndent, "entryZ", Num(location.entryZ), true, nl);
        AppendField(builder, fieldIndent, "pois",
            SerializePois(location.pois, fieldIndent, nl), true, nl);
        AppendField(builder, fieldIndent, "props",
            SerializePoiProps(location.props, fieldIndent, nl), true, nl);
        AppendField(builder, fieldIndent, "sites",
            SerializePoiSites(location.sites, fieldIndent, nl), true, nl);
        AppendField(builder, fieldIndent, "creatures",
            SerializePoiCreatures(location.creatures, fieldIndent, nl), false, nl);
        builder.Append(indent).Append('}');

        return builder.ToString();
    }

    private static string SerializePois(PlanetPOIEntry[] items, string indent, string nl)
    {
        if (items == null || items.Length == 0)
            return "[]";

        StringBuilder builder = new StringBuilder();
        string itemIndent = indent + "  ";
        string fieldIndent = itemIndent + "  ";

        builder.Append('[').Append(nl);

        for (int i = 0; i < items.Length; i++)
        {
            PlanetPOIEntry poi = items[i];
            if (poi == null)
                continue;

            builder.Append(itemIndent).Append('{').Append(nl);
            AppendField(builder, fieldIndent, "name", JsonString(poi.name), true, nl);
            if (!string.IsNullOrEmpty(poi.id))
                AppendField(builder, fieldIndent, "id", JsonString(poi.id), true, nl);
            AppendField(builder, fieldIndent, "x", Num(poi.x), true, nl);
            AppendField(builder, fieldIndent, "z", Num(poi.z), true, nl);
            AppendField(builder, fieldIndent, "radius", Num(poi.radius), false, nl);
            builder.Append(itemIndent).Append('}');
            if (i < items.Length - 1)
                builder.Append(',');
            builder.Append(nl);
        }

        builder.Append(indent).Append(']');
        return builder.ToString();
    }

    private static string SerializePoiProps(PlanetPOIProp[] items, string indent, string nl)
    {
        if (items == null || items.Length == 0)
            return "[]";

        StringBuilder builder = new StringBuilder();
        string itemIndent = indent + "  ";
        string fieldIndent = itemIndent + "  ";

        builder.Append('[').Append(nl);

        for (int i = 0; i < items.Length; i++)
        {
            PlanetPOIProp prop = items[i];
            if (prop == null)
                continue;

            string type = PlanetContentUtility.GetPropType(prop);
            bool swarm = prop.count > 1 || prop.radius > 0f || prop.randomYaw
                || prop.scaleVar > 0f || prop.seed != 1;

            builder.Append(itemIndent).Append('{').Append(nl);

            if (!string.IsNullOrEmpty(prop.id))
                AppendField(builder, fieldIndent, "id", JsonString(prop.id), true, nl);

            if (!string.IsNullOrEmpty(type))
                AppendField(builder, fieldIndent, "type", JsonString(type), true, nl);

            if (!string.IsNullOrEmpty(prop.site))
                AppendField(builder, fieldIndent, "site", JsonString(prop.site), true, nl);

            AppendField(builder, fieldIndent, "x", Num(prop.x), true, nl);
            AppendField(builder, fieldIndent, "z", Num(prop.z), true, nl);
            AppendField(builder, fieldIndent, "rotY", Num(prop.rotY), true, nl);
            AppendField(builder, fieldIndent, "scale", Num(prop.scale), swarm, nl);

            if (swarm)
            {
                AppendField(builder, fieldIndent, "count",
                    Mathf.Max(prop.count, 1).ToString(CultureInfo.InvariantCulture), true, nl);
                AppendField(builder, fieldIndent, "radius", Num(prop.radius), true, nl);
                AppendField(builder, fieldIndent, "seed",
                    prop.seed.ToString(CultureInfo.InvariantCulture), true, nl);
                AppendField(builder, fieldIndent, "randomYaw", Bool(prop.randomYaw), true, nl);
                AppendField(builder, fieldIndent, "scaleVar", Num(prop.scaleVar), true, nl);
                AppendField(builder, fieldIndent, "minDist", Num(prop.minDist), false, nl);
            }

            builder.Append(itemIndent).Append('}');
            if (i < items.Length - 1)
                builder.Append(',');
            builder.Append(nl);
        }

        builder.Append(indent).Append(']');
        return builder.ToString();
    }

    private static string SerializePoiSites(PlanetPOISite[] items, string indent, string nl)
    {
        if (items == null || items.Length == 0)
            return "[]";

        StringBuilder builder = new StringBuilder();
        string itemIndent = indent + "  ";
        string fieldIndent = itemIndent + "  ";

        builder.Append('[').Append(nl);

        for (int i = 0; i < items.Length; i++)
        {
            PlanetPOISite site = items[i];
            if (site == null)
                continue;

            builder.Append(itemIndent).Append('{').Append(nl);
            AppendField(builder, fieldIndent, "id", JsonString(site.id ?? ""), true, nl);
            AppendField(builder, fieldIndent, "name", JsonString(site.name ?? ""), true, nl);
            AppendField(builder, fieldIndent, "poi", JsonString(site.poi ?? ""), true, nl);
            AppendField(builder, fieldIndent, "kind",
                JsonString(string.IsNullOrEmpty(site.kind) ? "town" : site.kind), true, nl);
            AppendField(builder, fieldIndent, "layout",
                JsonString(PlanetContentUtility.NormalizeLayout(site.layout)), true, nl);
            AppendField(builder, fieldIndent, "x", Num(site.x), true, nl);
            AppendField(builder, fieldIndent, "z", Num(site.z), true, nl);
            AppendField(builder, fieldIndent, "radius", Num(site.radius), true, nl);
            AppendField(builder, fieldIndent, "rotation", Num(site.rotation), true, nl);
            AppendField(builder, fieldIndent, "seed",
                site.seed.ToString(CultureInfo.InvariantCulture), true, nl);
            AppendField(builder, fieldIndent, "spacing", Num(site.spacing), true, nl);
            AppendField(builder, fieldIndent, "rules",
                SerializePoiSiteRules(site.rules, fieldIndent, nl), false, nl);
            builder.Append(itemIndent).Append('}');
            if (i < items.Length - 1)
                builder.Append(',');
            builder.Append(nl);
        }

        builder.Append(indent).Append(']');
        return builder.ToString();
    }

    private static string SerializePoiSiteRules(PlanetPOISiteRule[] items,
        string indent, string nl)
    {
        if (items == null || items.Length == 0)
            return "[]";

        StringBuilder builder = new StringBuilder();
        string itemIndent = indent + "  ";
        string fieldIndent = itemIndent + "  ";

        builder.Append('[').Append(nl);

        for (int i = 0; i < items.Length; i++)
        {
            PlanetPOISiteRule rule = items[i];
            if (rule == null)
                continue;

            builder.Append(itemIndent).Append('{').Append(nl);
            AppendField(builder, fieldIndent, "type", JsonString(rule.type ?? ""), true, nl);
            AppendField(builder, fieldIndent, "count",
                Mathf.Max(rule.count, 0).ToString(CultureInfo.InvariantCulture), true, nl);
            AppendField(builder, fieldIndent, "weight", Num(rule.weight), true, nl);
            AppendField(builder, fieldIndent, "scale", Num(rule.scale), true, nl);
            AppendField(builder, fieldIndent, "scaleVar", Num(rule.scaleVar), true, nl);
            AppendField(builder, fieldIndent, "randomYaw", Bool(rule.randomYaw), true, nl);
            AppendField(builder, fieldIndent, "mode", JsonString(rule.mode ?? ""), false, nl);
            builder.Append(itemIndent).Append('}');
            if (i < items.Length - 1)
                builder.Append(',');
            builder.Append(nl);
        }

        builder.Append(indent).Append(']');
        return builder.ToString();
    }

    private static string SerializePoiCreatures(PlanetPOICreature[] items, string indent, string nl)
    {
        if (items == null || items.Length == 0)
            return "[]";

        StringBuilder builder = new StringBuilder();
        string itemIndent = indent + "  ";
        string fieldIndent = itemIndent + "  ";

        builder.Append('[').Append(nl);

        for (int i = 0; i < items.Length; i++)
        {
            PlanetPOICreature creature = items[i];
            if (creature == null)
                continue;

            builder.Append(itemIndent).Append('{').Append(nl);
            AppendField(builder, fieldIndent, "prefab", JsonString(creature.prefab), true, nl);
            AppendField(builder, fieldIndent, "x", Num(creature.x), true, nl);
            AppendField(builder, fieldIndent, "z", Num(creature.z), true, nl);
            if (!string.IsNullOrEmpty(creature.site))
                AppendField(builder, fieldIndent, "site", JsonString(creature.site), true, nl);
            AppendField(builder, fieldIndent, "herdMin",
                creature.herdMin.ToString(CultureInfo.InvariantCulture), true, nl);
            AppendField(builder, fieldIndent, "herdMax",
                creature.herdMax.ToString(CultureInfo.InvariantCulture), true, nl);
            AppendField(builder, fieldIndent, "herdRadius", Num(creature.herdRadius), true, nl);
            AppendField(builder, fieldIndent, "wanderRadius", Num(creature.wanderRadius), true, nl);
            AppendField(builder, fieldIndent, "speed", Num(creature.speed), true, nl);
            AppendField(builder, fieldIndent, "seed",
                creature.seed.ToString(CultureInfo.InvariantCulture), false, nl);
            builder.Append(itemIndent).Append('}');
            if (i < items.Length - 1)
                builder.Append(',');
            builder.Append(nl);
        }

        builder.Append(indent).Append(']');
        return builder.ToString();
    }

    private static bool TryFindPlanetObject(string json, string planetName,
        out int objectStart, out int objectEnd)
    {
        return TryFindObjectByFieldValue(json, "Planet", planetName, out objectStart, out objectEnd);
    }

    private static bool TryFindObjectByFieldValue(string json, string fieldName, string fieldValue,
        out int objectStart, out int objectEnd)
    {
        objectStart = -1;
        objectEnd = -1;

        List<int> objectStack = new List<int>();
        int i = 0;

        while (i < json.Length)
        {
            char c = json[i];

            if (c == '"')
            {
                string token;
                int end;
                if (!ReadJsonString(json, i, out token, out end))
                {
                    i++;
                    continue;
                }

                if (string.Equals(token, fieldName, StringComparison.Ordinal))
                {
                    int j = end + 1;
                    while (j < json.Length && char.IsWhiteSpace(json[j]))
                        j++;

                    if (j < json.Length && json[j] == ':')
                    {
                        j++;
                        while (j < json.Length && char.IsWhiteSpace(json[j]))
                            j++;

                        string value;
                        int valueEnd;
                        if (j < json.Length && json[j] == '"'
                            && ReadJsonString(json, j, out value, out valueEnd)
                            && string.Equals(value, fieldValue, StringComparison.Ordinal)
                            && objectStack.Count > 0)
                        {
                            objectStart = objectStack[objectStack.Count - 1];
                            objectEnd = FindMatchingBrace(json, objectStart);
                            return objectEnd > objectStart;
                        }
                    }
                }

                i = end + 1;
                continue;
            }

            if (c == '{')
                objectStack.Add(i);
            else if (c == '}' && objectStack.Count > 0)
                objectStack.RemoveAt(objectStack.Count - 1);

            i++;
        }

        return false;
    }

    private static int FindMatchingBrace(string text, int start)
    {
        int depth = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];

            if (inString)
            {
                if (escaped)
                    escaped = false;
                else if (c == '\\')
                    escaped = true;
                else if (c == '"')
                    inString = false;
                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return -1;
    }

    private static bool ReadJsonString(string text, int quoteIndex,
        out string value, out int endIndex)
    {
        value = "";
        endIndex = quoteIndex;

        if (quoteIndex < 0 || quoteIndex >= text.Length || text[quoteIndex] != '"')
            return false;

        StringBuilder builder = new StringBuilder();
        int i = quoteIndex + 1;

        while (i < text.Length)
        {
            char c = text[i];

            if (c == '"')
            {
                value = builder.ToString();
                endIndex = i;
                return true;
            }

            if (c == '\\')
            {
                i++;
                if (i >= text.Length)
                    break;

                char escape = text[i];
                switch (escape)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (i + 4 < text.Length)
                        {
                            string hex = text.Substring(i + 1, 4);
                            int code;
                            if (int.TryParse(hex, NumberStyles.HexNumber,
                                    CultureInfo.InvariantCulture, out code))
                            {
                                builder.Append((char)code);
                                i += 4;
                            }
                            else
                            {
                                builder.Append(escape);
                            }
                        }
                        else
                        {
                            builder.Append(escape);
                        }
                        break;
                    default: builder.Append(escape); break;
                }

                i++;
                continue;
            }

            builder.Append(c);
            i++;
        }

        return false;
    }

    private static bool TryFindTopLevelFieldValue(string planetObject, string fieldName,
        out int valueStart, out int valueEnd)
    {
        valueStart = -1;
        valueEnd = -1;

        int depth = 0;
        int i = 0;

        while (i < planetObject.Length)
        {
            char c = planetObject[i];

            if (c == '"')
            {
                string token;
                int end;
                if (!ReadJsonString(planetObject, i, out token, out end))
                {
                    i++;
                    continue;
                }

                if (depth == 1 && string.Equals(token, fieldName, StringComparison.Ordinal))
                {
                    int j = end + 1;
                    while (j < planetObject.Length && char.IsWhiteSpace(planetObject[j]))
                        j++;

                    if (j < planetObject.Length && planetObject[j] == ':')
                    {
                        j++;
                        while (j < planetObject.Length && char.IsWhiteSpace(planetObject[j]))
                            j++;

                        valueStart = j;
                        valueEnd = FindValueEnd(planetObject, j);
                        return valueEnd > valueStart;
                    }
                }

                i = end + 1;
                continue;
            }

            if (c == '{')
                depth++;
            else if (c == '}')
                depth--;

            i++;
        }

        return false;
    }

    private static int FindValueEnd(string text, int start)
    {
        if (start >= text.Length)
            return -1;

        char first = text[start];
        int i = start;

        if (first == '"')
        {
            string value;
            int end;
            return ReadJsonString(text, start, out value, out end) ? end + 1 : -1;
        }

        if (first == '{' || first == '[')
        {
            int depth = 0;
            bool inString = false;
            bool escaped = false;

            for (; i < text.Length; i++)
            {
                char c = text[i];

                if (inString)
                {
                    if (escaped)
                        escaped = false;
                    else if (c == '\\')
                        escaped = true;
                    else if (c == '"')
                        inString = false;
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '{' || c == '[')
                {
                    depth++;
                }
                else if (c == '}' || c == ']')
                {
                    depth--;
                    if (depth == 0)
                        return i + 1;
                }
            }

            return -1;
        }

        while (i < text.Length && text[i] != ',' && text[i] != '}' && text[i] != ']')
            i++;

        while (i > start && char.IsWhiteSpace(text[i - 1]))
            i--;

        return i;
    }

    private static string GetLineIndent(string text, int index)
    {
        int start = index;
        while (start > 0 && text[start - 1] != '\n' && text[start - 1] != '\r')
            start--;

        int end = start;
        while (end < text.Length && (text[end] == ' ' || text[end] == '\t'))
            end++;

        return text.Substring(start, end - start);
    }

    private static string DetectFieldIndent(string planetObject)
    {
        int start = 0;
        while (start < planetObject.Length && planetObject[start] != '\n' && planetObject[start] != '\r')
            start++;

        if (start < planetObject.Length)
        {
            if (planetObject[start] == '\r' && start + 1 < planetObject.Length
                && planetObject[start + 1] == '\n')
            {
                start += 2;
            }
            else
            {
                start++;
            }
        }

        int end = start;
        while (end < planetObject.Length && (planetObject[end] == ' ' || planetObject[end] == '\t'))
            end++;

        return end > start ? planetObject.Substring(start, end - start) : "    ";
    }

    private static string GetFieldIndent(string planetObject, string fieldName, string nl)
    {
        int valueStart;
        int valueEnd;
        return TryFindTopLevelFieldValue(planetObject, fieldName, out valueStart, out valueEnd)
            ? GetLineIndent(planetObject, valueStart)
            : DetectFieldIndent(planetObject);
    }

    private static string ReplaceJsonField(string planetObject, string fieldName,
        string valueJson, string indent, string nl)
    {
        int valueStart;
        int valueEnd;

        if (TryFindTopLevelFieldValue(planetObject, fieldName, out valueStart, out valueEnd))
        {
            return planetObject.Substring(0, valueStart)
                + valueJson
                + planetObject.Substring(valueEnd);
        }

        int close = planetObject.LastIndexOf('}');
        if (close < 0)
            return planetObject;

        int insertAt = close;
        while (insertAt > 0 && char.IsWhiteSpace(planetObject[insertAt - 1]))
            insertAt--;

        string comma = insertAt > 1
            && planetObject[insertAt - 1] != '{'
            && planetObject[insertAt - 1] != ','
            ? ","
            : "";

        string addition = comma + nl + indent + "\"" + fieldName + "\": " + valueJson;
        return planetObject.Substring(0, insertAt)
            + addition
            + planetObject.Substring(insertAt);
    }

    private static string JsonString(string value)
    {
        if (value == null)
            return "\"\"";

        StringBuilder builder = new StringBuilder("\"");
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (c < 32)
                        builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        builder.Append(c);
                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    private static string Num(float value)
    {
        string text = value.ToString("R", CultureInfo.InvariantCulture);
        if (text.IndexOf('.') < 0 && text.IndexOf('e') < 0 && text.IndexOf('E') < 0)
            text += ".0";
        return text;
    }

    private static string Bool(bool value)
    {
        return value ? "true" : "false";
    }

    private static void AppendField(StringBuilder builder, string indent,
        string name, string value, bool comma, string nl)
    {
        builder.Append(indent).Append('"').Append(name).Append("\": ").Append(value);
        if (comma)
            builder.Append(',');
        builder.Append(nl);
    }

    private static string SerializeCliffs(CliffSettings cliffs, string indent, string nl)
    {
        StringBuilder builder = new StringBuilder();
        string fieldIndent = indent + "  ";

        builder.Append('{').Append(nl);
        AppendField(builder, fieldIndent, "enabled", Bool(cliffs.enabled), true, nl);
        AppendField(builder, fieldIndent, "mesaHeight", Num(cliffs.mesaHeight), true, nl);
        AppendField(builder, fieldIndent, "mesaRoughness", Num(cliffs.mesaRoughness), true, nl);
        AppendField(builder, fieldIndent, "edgeWidth", Num(cliffs.edgeWidth), true, nl);
        AppendField(builder, fieldIndent, "canyonDepth", Num(cliffs.canyonDepth), true, nl);
        AppendField(builder, fieldIndent, "canyonWidth", Num(cliffs.canyonWidth), true, nl);
        AppendField(builder, fieldIndent, "strength", Num(cliffs.strength), true, nl);
        AppendField(builder, fieldIndent, "coverage", Num(cliffs.coverage), true, nl);
        AppendField(builder, fieldIndent, "regionFrequency", Num(cliffs.regionFrequency), true, nl);
        AppendField(builder, fieldIndent, "regionFeather", Num(cliffs.regionFeather), true, nl);
        AppendField(builder, fieldIndent, "maskThreshold", Num(cliffs.maskThreshold), true, nl);
        AppendField(builder, fieldIndent, "maskFrequency", Num(cliffs.maskFrequency), true, nl);
        AppendField(builder, fieldIndent, "slopeStart", Num(cliffs.slopeStart), true, nl);
        AppendField(builder, fieldIndent, "slopeSoftness", Num(cliffs.slopeSoftness), true, nl);
        AppendField(builder, fieldIndent, "textureType", JsonString(cliffs.textureType), true, nl);
        AppendField(builder, fieldIndent, "verticesPerSide",
            cliffs.verticesPerSide.ToString(CultureInfo.InvariantCulture), true, nl);
        AppendField(builder, fieldIndent, "outerVerticesPerSide",
            cliffs.outerVerticesPerSide.ToString(CultureInfo.InvariantCulture), true, nl);
        AppendField(builder, fieldIndent, "highDetailRadiusKm", Num(cliffs.highDetailRadiusKm), false, nl);
        builder.Append(indent).Append('}');

        return builder.ToString();
    }

    private static string SerializeOverlays(TerrainOverlay[] overlays, string indent, string nl)
    {
        if (overlays == null || overlays.Length == 0)
            return "[]";

        StringBuilder builder = new StringBuilder();
        string itemIndent = indent + "  ";
        string fieldIndent = itemIndent + "  ";

        builder.Append('[').Append(nl);

        for (int i = 0; i < overlays.Length; i++)
        {
            TerrainOverlay overlay = overlays[i];
            if (overlay == null)
                continue;

            builder.Append(itemIndent).Append('{').Append(nl);
            AppendField(builder, fieldIndent, "enabled", Bool(overlay.enabled), true, nl);
            AppendField(builder, fieldIndent, "label", JsonString(overlay.label), true, nl);
            AppendField(builder, fieldIndent, "texture", JsonString(overlay.texture), true, nl);
            AppendField(builder, fieldIndent, "mode", JsonString(overlay.mode), true, nl);
            AppendField(builder, fieldIndent, "tiling", Num(overlay.tiling), true, nl);
            AppendField(builder, fieldIndent, "strength", Num(overlay.strength), true, nl);
            AppendField(builder, fieldIndent, "fadeDistance", Num(overlay.fadeDistance), true, nl);
            AppendField(builder, fieldIndent, "normalStrength", Num(overlay.normalStrength), true, nl);
            AppendField(builder, fieldIndent, "aoStrength", Num(overlay.aoStrength), true, nl);
            AppendField(builder, fieldIndent, "width", Num(overlay.width), true, nl);
            AppendField(builder, fieldIndent, "feather", Num(overlay.feather), true, nl);
            AppendField(builder, fieldIndent, "wanderAmplitude", Num(overlay.wanderAmplitude), true, nl);
            AppendField(builder, fieldIndent, "wanderFrequency", Num(overlay.wanderFrequency), true, nl);
            AppendField(builder, fieldIndent, "curvePreset", JsonString(overlay.curvePreset), true, nl);
            AppendField(builder, fieldIndent, "curviness", Num(overlay.curviness), true, nl);
            AppendField(builder, fieldIndent, "curvinessSet", Bool(overlay.curvinessSet), true, nl);
            AppendField(builder, fieldIndent, "maxDetour", Num(overlay.maxDetour), true, nl);
            AppendField(builder, fieldIndent, "wallClearance", Num(overlay.wallClearance), true, nl);
            AppendField(builder, fieldIndent, "angle", Num(overlay.angle), true, nl);
            AppendField(builder, fieldIndent, "offset", Num(overlay.offset), true, nl);
            AppendField(builder, fieldIndent, "noiseScale", Num(overlay.noiseScale), true, nl);
            AppendField(builder, fieldIndent, "threshold", Num(overlay.threshold), true, nl);
            AppendField(builder, fieldIndent, "patchFeather", Num(overlay.patchFeather), true, nl);
            AppendField(builder, fieldIndent, "seedOffset", Num(overlay.seedOffset), true, nl);
            AppendField(builder, fieldIndent, "connectPois", Bool(overlay.connectPois), true, nl);
            AppendField(builder, fieldIndent, "footstepClip", JsonString(overlay.footstepClip), false, nl);
            builder.Append(itemIndent).Append('}');
            if (i < overlays.Length - 1)
                builder.Append(',');
            builder.Append(nl);
        }

        builder.Append(indent).Append(']');
        return builder.ToString();
    }

    private static string SerializeVegetation(VegetationSettings[] entries, string indent, string nl)
    {
        if (entries == null || entries.Length == 0)
            return "[]";

        StringBuilder builder = new StringBuilder();
        string itemIndent = indent + "  ";
        string fieldIndent = itemIndent + "  ";

        builder.Append('[').Append(nl);

        for (int i = 0; i < entries.Length; i++)
        {
            VegetationSettings entry = entries[i];
            if (entry == null)
                continue;

            builder.Append(itemIndent).Append('{').Append(nl);
            AppendField(builder, fieldIndent, "enabled", Bool(entry.enabled), true, nl);
            AppendField(builder, fieldIndent, "label", JsonString(entry.label), true, nl);
            AppendField(builder, fieldIndent, "folder", JsonString(entry.folder), true, nl);
            AppendField(builder, fieldIndent, "prefab", JsonString(entry.prefab), true, nl);
            AppendField(builder, fieldIndent, "material", JsonString(entry.material), true, nl);
            AppendField(builder, fieldIndent, "countPerTile",
                entry.countPerTile.ToString(CultureInfo.InvariantCulture), true, nl);
            AppendField(builder, fieldIndent, "minScale", Num(entry.minScale), true, nl);
            AppendField(builder, fieldIndent, "maxScale", Num(entry.maxScale), true, nl);
            AppendField(builder, fieldIndent, "maxSlope", Num(entry.maxSlope), true, nl);
            AppendField(builder, fieldIndent, "maxDistance", Num(entry.maxDistance), true, nl);
            AppendField(builder, fieldIndent, "placementAttempts",
                entry.placementAttempts.ToString(CultureInfo.InvariantCulture), true, nl);
            AppendField(builder, fieldIndent, "windStrength", Num(entry.windStrength), true, nl);
            AppendField(builder, fieldIndent, "windSpeed", Num(entry.windSpeed), true, nl);
            AppendField(builder, fieldIndent, "windFrequency", Num(entry.windFrequency), true, nl);
            AppendField(builder, fieldIndent, "seedOffset",
                entry.seedOffset.ToString(CultureInfo.InvariantCulture), false, nl);
            builder.Append(itemIndent).Append('}');
            if (i < entries.Length - 1)
                builder.Append(',');
            builder.Append(nl);
        }

        builder.Append(indent).Append(']');
        return builder.ToString();
    }

    private string ComputePreviewSignature()
    {
        StarSystem draft = BuildDraftSystem();
        StringBuilder builder = new StringBuilder();

        builder.Append(draft == null ? "<no-planet>" : JsonUtility.ToJson(draft));
        builder.Append("|poi=").Append(draftHasLocation ? JsonUtility.ToJson(draftLocation) : "<none>");
        builder.Append("|grid=").Append(gridSize);
        builder.Append("|centerX=").Append(centerTileX);
        builder.Append("|centerZ=").Append(centerTileZ);
        builder.Append("|overlayToggle=").Append(showOverlays ? 1 : 0);
        builder.Append("|detailToggle=").Append(showDetail ? 1 : 0);
        builder.Append("|vegetationToggle=").Append(showVegetation ? 1 : 0);
        builder.Append("|propsToggle=").Append(showProps ? 1 : 0);
        builder.Append("|creaturesToggle=").Append(showCreatures ? 1 : 0);
        builder.Append("|markersToggle=").Append(showPoiMarkers ? 1 : 0);
        builder.Append("|poseToggle=").Append(poseCreatures ? 1 : 0);

        return builder.ToString();
    }

    private void TrackLivePreviewChanges()
    {
        string signature = ComputePreviewSignature();
        if (string.Equals(signature, lastPreviewSignature, StringComparison.Ordinal))
            return;

        lastPreviewSignature = signature;
        ScheduleLiveRefresh();
    }

    private void ScheduleLiveRefresh()
    {
        if (!liveUpdate || !dataLoaded || FindSelectedSystem() == null)
            return;

        liveRefreshPending = true;
        liveRefreshTime = EditorApplication.timeSinceStartup + LiveRefreshDelaySeconds;
    }

    private void CancelLiveRefresh()
    {
        liveRefreshPending = false;
    }

    private void HandleLiveRefresh()
    {
        if (!liveRefreshPending)
            return;

        if (!liveUpdate || EditorApplication.timeSinceStartup < liveRefreshTime)
            return;

        CancelLiveRefresh();

        if (!dataLoaded || FindSelectedSystem() == null)
            return;

        try
        {
            GeneratePreview(null);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            SetStatus("No se pudo actualizar la vista previa: " + exception.Message, MessageType.Error);
        }
    }

    private void SetStatus(string message, MessageType type)
    {
        statusMessage = message;
        statusType = type;
        Repaint();
    }

    private static string SanitizeName(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "Planet";

        char[] invalid = System.IO.Path.GetInvalidFileNameChars();
        string result = value;
        foreach (char character in invalid)
            result = result.Replace(character, '_');
        return result.Replace(' ', '_');
    }

    private static T GetField<T>(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, InstanceFlags);
        return field != null ? (T)field.GetValue(target) : default(T);
    }

    private static void SetField(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, InstanceFlags);
        if (field != null)
            field.SetValue(target, value);
    }

    private static object Invoke(object target, string name, params object[] arguments)
    {
        MethodInfo method = target.GetType().GetMethod(name, InstanceFlags);
        if (method == null)
            throw new MissingMethodException(target.GetType().Name, name);
        return method.Invoke(target, arguments);
    }

    private static T Invoke<T>(object target, string name, params object[] arguments)
    {
        return (T)Invoke(target, name, arguments);
    }
}
#endif
