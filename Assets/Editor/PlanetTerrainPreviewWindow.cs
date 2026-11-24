#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

    private static readonly BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [SerializeField] private string selectedPlanet = "";
    [SerializeField] private string searchText = "";
    [SerializeField] private int gridSize = 2;
    [SerializeField] private int centerTileX;
    [SerializeField] private int centerTileZ;
    [SerializeField] private bool showOverlays = true;
    [SerializeField] private bool showDetail = true;

    private StarSystems starSystems;
    private string[] allPlanets = new string[0];
    private string[] filteredPlanets = new string[0];
    private GameObject previewRoot;
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
    }

    private void OnDisable()
    {
        SessionState.SetString(SelectedPlanetKey, selectedPlanet ?? "");
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
        EditorGUILayout.LabelField("Planet Terrain Preview", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Genera un trozo pequeño del terreno usando los parámetros del planeta en StarSystems.json. " +
            "No entra en Play Mode.", MessageType.Info);

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
                selectedPlanet = filteredPlanets[newIndex];
        }
        else
        {
            EditorGUILayout.HelpBox("No hay planetas que coincidan con la búsqueda.", MessageType.Warning);
        }

        StarSystem system = FindSelectedSystem();
        if (dataLoaded && system != null)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Parámetros del planeta", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Seed", system.seed.ToString());
            EditorGUILayout.LabelField("Texturas", GetTerrainTextureKeywords(system));
            EditorGUILayout.LabelField("Acantilados", system.cliffs != null && system.cliffs.enabled ? "activados" : "desactivados");
            EditorGUILayout.LabelField("Detalle", string.IsNullOrEmpty(system.detailTexture) ? "ninguno" : system.detailTexture);
            EditorGUILayout.LabelField("Overlays", system.overlays == null ? "0" : system.overlays.Length.ToString());
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Vista previa", EditorStyles.boldLabel);
        gridSize = EditorGUILayout.IntSlider("Tiles por lado", gridSize, 1, 4);
        centerTileX = EditorGUILayout.IntField("Centro tile X", centerTileX);
        centerTileZ = EditorGUILayout.IntField("Centro tile Z", centerTileZ);
        showOverlays = EditorGUILayout.Toggle("Overlays (caminos/arena)", showOverlays);
        showDetail = EditorGUILayout.Toggle("Detalle cercano", showDetail);

        EditorGUILayout.Space(6f);
        EditorGUI.BeginDisabledGroup(!dataLoaded || system == null);
        if (GUILayout.Button("Generar vista previa", GUILayout.Height(30f)))
            GeneratePreview(system);
        EditorGUI.EndDisabledGroup();

        if (GUILayout.Button("Limpiar vista previa"))
            CleanupPreview();

        EditorGUILayout.Space(6f);
        EditorGUILayout.HelpBox(statusMessage, statusType);
    }

    private void GeneratePreview(StarSystem system)
    {
        // Re-read the JSON on every generation so edits to StarSystems.json
        // are visible without closing/reopening the window.
        TextAsset source = Resources.Load<TextAsset>(OGGetAddress.files + "StarSystems");
        if (source == null)
        {
            SetStatus("No se encontró Assets/Resources/files/StarSystems.json.", MessageType.Error);
            return;
        }

        StarSystems fresh = JsonUtility.FromJson<StarSystems>(source.text);
        if (fresh == null || fresh.starSystemsData == null)
        {
            SetStatus("StarSystems.json no se pudo parsear.", MessageType.Error);
            return;
        }

        starSystems = fresh;
        system = FindSelectedSystem();

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
                generatedTiles++;
            }
        }

        FramePreview();
        SetStatus(
            "Generadas " + generatedTiles + " tiles de " + system.Planet
            + " con " + verticesPerSide + " vértices/lado. " + DescribeOverlays(streamer),
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
