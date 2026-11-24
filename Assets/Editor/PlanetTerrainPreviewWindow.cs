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

    [SerializeField] private bool configFoldout = true;
    [SerializeField] private bool generalFoldout = true;
    [SerializeField] private bool cliffsFoldout;
    [SerializeField] private bool overlaysFoldout = true;
    [SerializeField] private bool[] overlayFoldouts = new bool[3];

    private static readonly string[] OverlayModes = { "patch", "cliffFoot", "path", "poiPath" };

    private StarSystems starSystems;
    private bool liveRefreshCallbackRegistered;
    private string[] allPlanets = new string[0];
    private string[] filteredPlanets = new string[0];
    private GameObject previewRoot;
    private Transform previewVegetationViewer;
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
    }

    private void OnDisable()
    {
        SessionState.SetString(SelectedPlanetKey, selectedPlanet ?? "");

        if (liveRefreshCallbackRegistered)
        {
            EditorApplication.update -= HandleLiveRefresh;
            liveRefreshCallbackRegistered = false;
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
        SyncDraftIfNeeded(true);
        lastPreviewSignature = ComputePreviewSignature();
        SetStatus("StarSystems.json cargado. " + allPlanets.Length + " planetas disponibles.", MessageType.Info);
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
            "'Aplicar a JSON' sobrescribe la configuración en StarSystems.json.",
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
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(4f);
            EditorGUI.BeginDisabledGroup(!hasPlanet);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Restablecer desde JSON"))
            {
                SyncDraftIfNeeded(true);
                SetStatus("Parámetros restablecidos desde StarSystems.json.", MessageType.Info);
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
        gridSize = EditorGUILayout.IntSlider("Tiles por lado", gridSize, 1, 4);
        centerTileX = EditorGUILayout.IntField("Centro tile X", centerTileX);
        centerTileZ = EditorGUILayout.IntField("Centro tile Z", centerTileZ);
        showOverlays = EditorGUILayout.Toggle("Overlays (caminos/arena)", showOverlays);
        showDetail = EditorGUILayout.Toggle("Detalle cercano", showDetail);
        showVegetation = EditorGUILayout.Toggle("Vegetación (bloque vegetation)", showVegetation);

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
            overlay.fadeDistance = EditorGUILayout.FloatField("Fade distance", overlay.fadeDistance);
            overlay.normalStrength = EditorGUILayout.FloatField("Normal strength", overlay.normalStrength);
            overlay.aoStrength = EditorGUILayout.FloatField("AO strength", overlay.aoStrength);
            overlay.width = EditorGUILayout.FloatField("Width", overlay.width);
            overlay.feather = EditorGUILayout.FloatField("Feather", overlay.feather);
            overlay.wanderAmplitude = EditorGUILayout.FloatField("Wander amplitude", overlay.wanderAmplitude);
            overlay.wanderFrequency = EditorGUILayout.FloatField("Wander frequency", overlay.wanderFrequency);
            overlay.angle = EditorGUILayout.FloatField("Angle", overlay.angle);
            overlay.offset = EditorGUILayout.FloatField("Offset", overlay.offset);
            overlay.noiseScale = EditorGUILayout.FloatField("Noise scale", overlay.noiseScale);
            overlay.threshold = EditorGUILayout.FloatField("Threshold", overlay.threshold);
            overlay.patchFeather = EditorGUILayout.FloatField("Patch feather", overlay.patchFeather);
            overlay.seedOffset = EditorGUILayout.FloatField("Seed offset", overlay.seedOffset);
            overlay.connectPois = EditorGUILayout.Toggle("Connect POIs", overlay.connectPois);
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

        FramePreview();
        SetStatus(
            "Generadas " + generatedTiles + " tiles de " + system.Planet
            + " con " + verticesPerSide + " vértices/lado. " + DescribeOverlays(streamer)
            + " " + DescribeVegetation(vegetationPrototypes, generatedTiles),
            MessageType.Info);
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

        SceneView view = SceneView.lastActiveSceneView;
        if (view != null)
        {
            view.LookAt(bounds.center, Quaternion.Euler(35f, 45f, 0f),
                Mathf.Max(10f, bounds.extents.magnitude * 1.35f));
            view.Repaint();
        }

        SceneView.RepaintAll();
    }

    private static string DescribeOverlays(TerrainStreamer streamer)
    {
        Array overlays = GetField<Array>(streamer, "overlays");
        if (overlays == null)
            return "Overlays: 0.";

        int active = 0;
        int segments = 0;

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
        }

        return "Overlays activos: " + active + " (" + segments + " segmentos de ruta).";
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

            string updated = json.Substring(0, objectStart)
                + planetObject
                + json.Substring(objectEnd + 1);

            File.WriteAllText(path, updated);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            LoadData();
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

    private static bool TryFindPlanetObject(string json, string planetName,
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

                if (token == "Planet")
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
                            && string.Equals(value, planetName, StringComparison.Ordinal)
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
            AppendField(builder, fieldIndent, "angle", Num(overlay.angle), true, nl);
            AppendField(builder, fieldIndent, "offset", Num(overlay.offset), true, nl);
            AppendField(builder, fieldIndent, "noiseScale", Num(overlay.noiseScale), true, nl);
            AppendField(builder, fieldIndent, "threshold", Num(overlay.threshold), true, nl);
            AppendField(builder, fieldIndent, "patchFeather", Num(overlay.patchFeather), true, nl);
            AppendField(builder, fieldIndent, "seedOffset", Num(overlay.seedOffset), true, nl);
            AppendField(builder, fieldIndent, "connectPois", Bool(overlay.connectPois), false, nl);
            builder.Append(itemIndent).Append('}');
            if (i < overlays.Length - 1)
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
        builder.Append("|grid=").Append(gridSize);
        builder.Append("|centerX=").Append(centerTileX);
        builder.Append("|centerZ=").Append(centerTileZ);
        builder.Append("|overlayToggle=").Append(showOverlays ? 1 : 0);
        builder.Append("|detailToggle=").Append(showDetail ? 1 : 0);
        builder.Append("|vegetationToggle=").Append(showVegetation ? 1 : 0);

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
