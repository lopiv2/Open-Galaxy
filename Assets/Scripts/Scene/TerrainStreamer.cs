using System.Collections.Generic;
using UnityEngine;

public class TerrainStreamer : MonoBehaviour
{
    [Header("Generation")]
    [SerializeField] public int seed = 1138;

    [Header("Tile Settings")]
    [SerializeField] private float tileSize = 1000f;
    [SerializeField] public int verticesPerSide = 32;
    [SerializeField] public float maxTerrainHeight = 1000f;
    [SerializeField] private AnimationCurve heightCurve = AnimationCurve.Linear(0, 0, 1, 1);

    [Tooltip("The ship is surrounded by this many kilometres of tiles.")]
    [SerializeField] public float tileRadiusInKilometres = 30f;

    [Tooltip("Cheap distant ring (low-res, no colliders) hiding the terrain edge.")]
    [SerializeField] public float outerTileRadiusInKilometres = 30f;
    [SerializeField] public int outerVerticesPerSide = 8;

    [Header("Materials")]
    [SerializeField] private Material terrainMaterial;
    [SerializeField] private Texture2D[] terrainTextures;
    // Parallel to terrainTextures: per-diffuse normal/AO siblings resolved
    // by name (foo_d + foo_n + foo_ao). Null entries = no map for that tile.
    private Texture2D[] terrainNormalMaps;
    private Texture2D[] terrainOcclusionMaps;
    private Texture2D[] cliffTextures;
    private Texture2D detailAlbedo;
    private Texture2D detailNormal;
    private Texture2D detailHeight;
    private Texture2D detailOcclusion;
    private bool detailReady;
    [SerializeField] public string terrainTextureType = "forest-mixed";
    [SerializeField] public string cliffTextureType = "cliff01";

    [Header("Cliffs (mesas / canyons)")]
    [Tooltip("Opt-in cliff layer. Off keeps the previous smooth terrain.")]
    public bool cliffsEnabled = false;
    [Tooltip("Slope (normal.y) where the cliff texture starts showing.")]
    public float cliffSlopeStart = 0.8f;
    public float cliffSlopeSoftness = 0.2f;
    public float cliffMesaHeight = 60f;
    public float cliffMesaRoughness = 25f;
    public float cliffEdgeWidth = 0.02f;
    public float cliffCanyonDepth = 55f;
    public float cliffCanyonWidth = 0.06f;
    public float cliffStrength = 1f;
    [Tooltip("0..1: fraction of the planet covered by deterministic cliff regions.")]
    public float cliffCoverage = 1f;
    public float cliffRegionFrequency = 0.00012f;
    public float cliffRegionFeather = 0.06f;
    public float cliffMaskThreshold = 0.45f;
    public float cliffMaskFrequency = 0.00045f;

    [Header("Close-up detail")]
    [Tooltip("Base name of <name>_d/_n/_h/_ao in the terrain textures folder.")]
    public string detailTextureType = "dune_detail";
    public float detailTiling = 0.05f;
    public float detailStrength = 0.6f;
    public float detailNormalStrength = 0.8f;
    public float detailHeightScale = 0.015f;
    public float detailAOStrength = 0.8f;
    public float detailFadeDistance = 350f;

    [Header("Vegetation")]
    [Tooltip("Per-planet vegetation entries from StarSystems.json (trees, bushes...).")]
    public VegetationSettings[] vegetationConfig;
    [Tooltip("Radius around the ship whose tiles receive vegetation. Smaller = cheaper.")]
    public float vegetationRadiusInKilometres = 1.5f;
    private VegetationPrototype[] vegetationPrototypes;
    private int vegetationTileRadius;

    [Header("Terrain Overlays")]
    [Tooltip("Per-planet overlay layers from StarSystems.json. Max three slots.")]
    public TerrainOverlay[] overlayConfig;
    [Tooltip("Planet name, used to connect path overlays to PlanetPOIs.json.")]
    public string planetName;
    private const int MaxOverlaySlots = 3;
    private const int MaxPathSegments = 16;

    private class OverlayRuntime
    {
        public bool active;
        public Texture2D albedo;
        public Texture2D normal;
        public Texture2D occlusion;
        public Vector4 pA;
        public Vector4 pB;
        public Vector4 pC;
        public Vector4 pD;
        public Vector4 pE;
        public readonly Vector4[] segments = new Vector4[MaxPathSegments];
        public int segmentCount;
    }

    // Precomputed road cost for the rectangle a leg may wander through.
    // Evaluating the terrain directly for every dynamic-programming edge is
    // far too expensive, so the detailed sampling happens once per cell.
    private class RouteCostField
    {
        private readonly float originX;
        private readonly float originZ;
        private readonly float cellSize;
        private readonly int width;
        private readonly int height;
        private readonly float[] costs;

        public RouteCostField(TerrainStreamer owner, Vector2 start, Vector2 end,
            float maxOffset, float requestedCellSize)
        {
            Vector2 direction = (end - start).normalized;
            Vector2 perpendicular = new Vector2(-direction.y, direction.x);
            Vector2 a = start + perpendicular * maxOffset;
            Vector2 b = start - perpendicular * maxOffset;
            Vector2 c = end + perpendicular * maxOffset;
            Vector2 d = end - perpendicular * maxOffset;

            float minX = Mathf.Min(Mathf.Min(a.x, b.x), Mathf.Min(c.x, d.x));
            float maxX = Mathf.Max(Mathf.Max(a.x, b.x), Mathf.Max(c.x, d.x));
            float minZ = Mathf.Min(Mathf.Min(a.y, b.y), Mathf.Min(c.y, d.y));
            float maxZ = Mathf.Max(Mathf.Max(a.y, b.y), Mathf.Max(c.y, d.y));

            float spanX = maxX - minX;
            float spanZ = maxZ - minZ;
            cellSize = Mathf.Max(requestedCellSize,
                Mathf.Max(spanX, spanZ) / 159f);

            const int margin = 3;
            width = Mathf.Clamp(Mathf.CeilToInt(spanX / cellSize) + margin * 2, 4, 160);
            height = Mathf.Clamp(Mathf.CeilToInt(spanZ / cellSize) + margin * 2, 4, 160);

            originX = (minX + maxX) * 0.5f - (width - 1) * cellSize * 0.5f;
            originZ = (minZ + maxZ) * 0.5f - (height - 1) * cellSize * 0.5f;
            costs = new float[width * height];

            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    costs[z * width + x] = owner.CliffRouteCost(
                        originX + x * cellSize,
                        originZ + z * cellSize);
                }
            }
        }

        public float Sample(float worldX, float worldZ)
        {
            float gx = Mathf.Clamp((worldX - originX) / cellSize, 0f, width - 1.001f);
            float gz = Mathf.Clamp((worldZ - originZ) / cellSize, 0f, height - 1.001f);
            int x0 = Mathf.FloorToInt(gx);
            int z0 = Mathf.FloorToInt(gz);
            float tx = gx - x0;
            float tz = gz - z0;
            int index = z0 * width + x0;

            return Mathf.Lerp(
                Mathf.Lerp(costs[index], costs[index + 1], tx),
                Mathf.Lerp(costs[index + width], costs[index + width + 1], tx),
                tz);
        }
    }

    private readonly OverlayRuntime[] overlays = new OverlayRuntime[MaxOverlaySlots];
    private bool overlaysReady;

    [Header("Ship")]
    [SerializeField] public Transform ship;

    [Header("Tile Parent")]
    [SerializeField] public Transform tileParent;


    [Header("Generation settings")]
    public float plainsAmp = 50f;
    public float desertAmp = 100f;
    public float hillsAmp = 200f;
    public float mountAmp = 400f;

    private readonly Dictionary<Vector2Int, GameObject> loadedTiles = new();
    private Material sharedOuterMaterial;

    private TerrainGenerator terrainGenerator;
    private Vector2Int currentShipTile;
    private int tileRadiusInTiles;
    private int outerTileRadiusInTiles;
    private bool initialized;

    private void Start()
    {
        terrainMaterial = LoadBaseMaterial("objects/terrain/material/terrain");
        terrainTextures = LoadTexturesFromResources("objects/terrain/textures/");
        ResolveMapSiblings(Resources.LoadAll<Texture2D>("objects/terrain/textures/"));
        cliffTextures = LoadCliffTextures("objects/terrain/cliffs/");
        LoadDetailTextures();

        if (ship == null)
        {
            Debug.LogError("TerrainTileStreamer: Ship is not assigned.");
            enabled = false;
            return;
        }

        if (terrainMaterial == null)
        {
            Debug.LogError("TerrainTileStreamer: Terrain material is not assigned.");
            enabled = false;
            return;
        }

        if (terrainTextures == null || terrainTextures.Length == 0)
        {
            Debug.LogError("TerrainTileStreamer: No terrain textures assigned.");
            enabled = false;
            return;
        }

        if (tileSize <= 0f)
        {
            Debug.LogError("TerrainTileStreamer: Tile size must be greater than zero.");
            enabled = false;
            return;
        }

        if (tileRadiusInKilometres <= 0f)
        {
            Debug.LogError(
                "TerrainTileStreamer: Tile radius must be greater than zero."
            );

            enabled = false;
            return;
        }

        if (tileParent == null)
        {
            GameObject parent = new GameObject("Generated Terrain Tiles");
            tileParent = parent.transform;
        }

        terrainGenerator = new TerrainGenerator(
            seed,
            tileSize,
            verticesPerSide,
            maxTerrainHeight,
            heightCurve
        );

        terrainGenerator.plainsAmp = plainsAmp;
        terrainGenerator.desertAmp = desertAmp;
        terrainGenerator.hillsAmp = hillsAmp;
        terrainGenerator.mountAmp = mountAmp;

        terrainGenerator.cliffsEnabled = cliffsEnabled;
        terrainGenerator.cliffMesaHeight = cliffMesaHeight;
        terrainGenerator.cliffMesaRoughness = cliffMesaRoughness;
        terrainGenerator.cliffEdgeWidth = cliffEdgeWidth;
        terrainGenerator.cliffCanyonDepth = cliffCanyonDepth;
        terrainGenerator.cliffCanyonWidth = cliffCanyonWidth;
        terrainGenerator.cliffStrength = cliffStrength;
        terrainGenerator.cliffCoverage = cliffCoverage;
        terrainGenerator.cliffRegionFrequency = cliffRegionFrequency;
        terrainGenerator.cliffRegionFeather = cliffRegionFeather;
        terrainGenerator.cliffMaskThreshold = cliffMaskThreshold;
        terrainGenerator.cliffMaskFrequency = cliffMaskFrequency;

        // Route overlays need the configured terrain generator, so they are
        // loaded only after its cliff fields are ready.
        LoadOverlays();

        tileRadiusInTiles = Mathf.CeilToInt(
            tileRadiusInKilometres * 1000f / tileSize
        );

        vegetationPrototypes = BuildVegetationPrototypes();
        vegetationTileRadius = Mathf.Max(0,
            Mathf.CeilToInt(vegetationRadiusInKilometres * 1000f / tileSize));

        outerTileRadiusInKilometres = Mathf.Max(outerTileRadiusInKilometres, tileRadiusInKilometres);
        outerTileRadiusInTiles = Mathf.CeilToInt(
            outerTileRadiusInKilometres * 1000f / tileSize
        );

        // One shared material for the whole distant ring (no per-tile cost).
        // Must define ALL ground slots: the shader blends tile borders with
        // the neighbor maps (default white) and missing slots show as streaks.
        sharedOuterMaterial = new Material(terrainMaterial);
        Texture2D outerGround = terrainTextures.Length > 0 ? terrainTextures[0] : null;
        if (outerGround != null)
        {
            sharedOuterMaterial.SetTexture("_GroundMap", outerGround);
            sharedOuterMaterial.SetTexture("_RightGroundMap", outerGround);
            sharedOuterMaterial.SetTexture("_FrontGroundMap", outerGround);
            sharedOuterMaterial.SetTexture("_GroundNormalMap", terrainNormalMaps.Length > 0 ? terrainNormalMaps[0] : null);
            sharedOuterMaterial.SetTexture("_GroundOcclusionMap", terrainOcclusionMaps.Length > 0 ? terrainOcclusionMaps[0] : null);
        }
        if (cliffTextures.Length > 0 && cliffTextures[0] != null)
        {
            sharedOuterMaterial.SetTexture("_CliffMap", cliffTextures[0]);
            sharedOuterMaterial.SetTexture("_CliffMapB", cliffTextures[0]);
        }
        sharedOuterMaterial.SetFloat("_CliffBlendAmount", 0f);
        sharedOuterMaterial.SetFloat("_CliffStart", cliffSlopeStart);
        sharedOuterMaterial.SetFloat("_CliffSoftness", Mathf.Max(0.001f, cliffSlopeSoftness));

        // Distant ring: no close-up dune detail, but overlays are world-space
        // masks and must stay visible when the ship is flying above the planet.
        sharedOuterMaterial.SetFloat("_DetailEnabled", 0f);
        ApplyOverlayMaps(sharedOuterMaterial);

        Vector3 shipLocalPosition = tileParent.InverseTransformPoint(ship.position);
        currentShipTile = LocalPositionToTileCoordinate(shipLocalPosition);

        UpdateLoadedTiles();

        initialized = true;
    }

    private void Update()
    {
        if (!initialized)
            return;

        if (ship != null)
        {
            Vector3 shipLocalPosition = tileParent.InverseTransformPoint(ship.position);
            Vector2Int shipTile = LocalPositionToTileCoordinate(shipLocalPosition);

            if (shipTile != currentShipTile)
            {
                currentShipTile = shipTile;
                UpdateLoadedTiles();
            }
        }
    }

    private Texture2D[] LoadTexturesFromResources(string folderPath)
    {
        Texture2D[] textures = Resources.LoadAll<Texture2D>(folderPath);

        if (textures == null || textures.Length == 0)
        {
            Debug.LogError($"TerrainTileStreamer: No textures found in Resources/{folderPath}");
            return new Texture2D[0];
        }

        // terrainTextureType accepts a comma/semicolon-separated keyword list:
        // a texture matches if its name contains ANY keyword ("sandy, dune").
        // A single keyword ("sandy") behaves exactly like before.
        if (string.IsNullOrWhiteSpace(terrainTextureType))
            return textures;

        string[] keywords = terrainTextureType.Split(new char[] { ',', ';' }, System.StringSplitOptions.RemoveEmptyEntries);
        List<string> cleaned = new List<string>(keywords.Length);

        foreach (string raw in keywords)
        {
            string kw = raw.Trim().ToLowerInvariant();

            if (string.IsNullOrEmpty(kw) == false)
            {
                cleaned.Add(kw);
            }
        }

        if (cleaned.Count == 0)
            return textures;

        List<Texture2D> filtered = new List<Texture2D>(textures.Length);

        foreach (Texture2D t in textures)
        {
            if (t == null || string.IsNullOrEmpty(t.name))
                continue;

            // Normal/AO siblings (foo_n, foo_ao) are never diffuse tiles:
            // they attach to their foo_d partner via ResolveMapSibling.
            if (IsMapSiblingName(t.name))
                continue;

            string lowerName = t.name.ToLowerInvariant();

            // Close-up detail maps are applied separately in world space;
            // dune_detail_d would otherwise match the "dune" keyword.
            if (lowerName.Contains("detail"))
                continue;

            foreach (string kw in cleaned)
            {
                if (lowerName.Contains(kw))
                {
                    filtered.Add(t);
                    break;
                }
            }
        }

        if (filtered.Count == 0)
        {
            Debug.LogWarning($"TerrainTileStreamer: No textures containing '{terrainTextureType}' found in Resources/{folderPath}");
            return new Texture2D[0];
        }

        return filtered.ToArray();
    }

    // foo_n / foo_ao / foo_normal / foo_occlusion are map siblings, not tiles.
    private static bool IsMapSiblingName(string textureName)
    {
        string lower = textureName.ToLowerInvariant();
        return lower.EndsWith("_n") || lower.EndsWith("_ao")
            || lower.EndsWith("_normal") || lower.EndsWith("_occlusion")
            || lower.EndsWith("_h") || lower.EndsWith("_height");
    }

    // Finds the normal ("n") or occlusion ("ao") partner of a diffuse map:
    // dunes_d -> dunes_n / dunes_ao, rock -> rock_n / rock_ao.
    private static Texture2D ResolveMapSibling(Texture2D[] all, string diffuseName, string kind)
    {
        string lower = diffuseName.ToLowerInvariant();
        string baseName = lower.EndsWith("_d") ? diffuseName.Substring(0, diffuseName.Length - 2) : diffuseName;
        string wanted = (baseName + "_" + kind).ToLowerInvariant();

        foreach (Texture2D t in all)
        {
            if (t != null && t.name.ToLowerInvariant() == wanted)
                return t;
        }

        return null;
    }

    // Builds the normal/AO arrays parallel to the filtered diffuse pool.
    private void ResolveMapSiblings(Texture2D[] all)
    {
        terrainNormalMaps = new Texture2D[terrainTextures.Length];
        terrainOcclusionMaps = new Texture2D[terrainTextures.Length];

        for (int i = 0; i < terrainTextures.Length; i++)
        {
            if (terrainTextures[i] == null)
                continue;

            terrainNormalMaps[i] = ResolveMapSibling(all, terrainTextures[i].name, "n");
            terrainOcclusionMaps[i] = ResolveMapSibling(all, terrainTextures[i].name, "ao");
        }
    }

    // Current-tile maps: diffuse + its normal/AO siblings. Missing maps fall
    // back to the shader defaults ("bump" neutral normal, "white" AO).
    private void SetTileGroundMaps(Material material, int index)
    {
        material.SetTexture("_GroundMap", terrainTextures[index]);
        material.SetTexture("_GroundNormalMap", index < terrainNormalMaps.Length ? terrainNormalMaps[index] : null);
        material.SetTexture("_GroundOcclusionMap", index < terrainOcclusionMaps.Length ? terrainOcclusionMaps[index] : null);
        ApplyDetailMaps(material);
        ApplyOverlayMaps(material);
    }

    private Material LoadBaseMaterial(string materialPath)
    {
        Material material = Resources.Load<Material>(materialPath);

        if (material == null)
        {
            Debug.LogError($"TerrainTileStreamer: Material not found at Resources/{materialPath}");
            return null;
        }

        return material;
    }

    private Texture2D[] LoadCliffTextures(string folderPath)
    {
        Texture2D[] textures = Resources.LoadAll<Texture2D>(folderPath);

        if (textures == null || textures.Length == 0)
        {
            Debug.LogError($"TerrainTileStreamer: No cliff textures found in Resources/{folderPath}");
            return new Texture2D[0];
        }

        // Same keyword filter as ground textures: "cliff" matches cliff01 and
        // cliff02, "cliff02" only that one. Empty = every cliff map.
        if (string.IsNullOrWhiteSpace(cliffTextureType))
            return textures;

        string[] keywords = cliffTextureType.Split(new char[] { ',', ';' }, System.StringSplitOptions.RemoveEmptyEntries);
        List<Texture2D> filtered = new List<Texture2D>();

        foreach (Texture2D texture in textures)
        {
            if (texture == null || string.IsNullOrEmpty(texture.name))
                continue;

            string lowerName = texture.name.ToLowerInvariant();

            foreach (string raw in keywords)
            {
                string keyword = raw.Trim().ToLowerInvariant();

                if (keyword.Length > 0 && lowerName.Contains(keyword))
                {
                    filtered.Add(texture);
                    break;
                }
            }
        }

        if (filtered.Count == 0)
        {
            Debug.LogWarning($"TerrainTileStreamer: No cliff textures containing '{cliffTextureType}'; using all cliff maps.");
            return textures;
        }

        return filtered.ToArray();
    }

    // Loads the four close-up maps named <detailTextureType>_d/_n/_h/_ao.
    // Any missing map is left null so the shader uses its neutral default.
    private void LoadDetailTextures()
    {
        detailAlbedo = null;
        detailNormal = null;
        detailHeight = null;
        detailOcclusion = null;
        detailReady = false;

        if (string.IsNullOrWhiteSpace(detailTextureType))
            return;

        string baseName = detailTextureType.Trim();
        string lower = baseName.ToLowerInvariant();
        if (lower.EndsWith("_d") || lower.EndsWith("_n")
            || lower.EndsWith("_h") || lower.EndsWith("_ao"))
        {
            baseName = baseName.Substring(0, baseName.Length - 2);
        }

        string folder = "objects/terrain/textures/";
        detailAlbedo = LoadDetailMap(folder + baseName + "_d", "albedo");
        detailNormal = LoadDetailMap(folder + baseName + "_n", "normal");
        detailHeight = LoadDetailMap(folder + baseName + "_h", "height");
        detailOcclusion = LoadDetailMap(folder + baseName + "_ao", "occlusion");

        if (detailAlbedo == null)
        {
            Debug.LogWarning("TerrainTileStreamer: No detail albedo found for '"
                + detailTextureType + "'; close-up detail disabled.");
            return;
        }

        detailReady = true;

        if (terrainMaterial != null)
            ApplyDetailMaps(terrainMaterial);
    }

    // Applies the close-up set to a material instance. Also called per tile so
    // the maps cannot be lost when the shared base material is reimported.
    private void ApplyDetailMaps(Material material)
    {
        if (detailReady == false || material == null)
            return;

        material.SetTexture("_DetailMap", detailAlbedo);
        material.SetTexture("_DetailNormalMap", detailNormal);
        material.SetTexture("_DetailHeightMap", detailHeight);
        material.SetTexture("_DetailOcclusionMap", detailOcclusion);
        material.SetFloat("_DetailEnabled", 1f);
        material.SetFloat("_DetailTiling", detailTiling);
        material.SetFloat("_DetailStrength", detailStrength);
        material.SetFloat("_DetailNormalStrength", detailNormalStrength);
        material.SetFloat("_DetailHeightScale", detailHeightScale);
        material.SetFloat("_DetailAOStrength", detailAOStrength);
        material.SetFloat("_DetailFadeDistance", Mathf.Max(1f, detailFadeDistance));
    }

    private static Texture2D LoadDetailMap(string resourcePath, string kind)
    {
        Texture2D map = Resources.Load<Texture2D>(resourcePath);

        if (map == null)
        {
            Debug.LogWarning("TerrainTileStreamer: Missing detail " + kind
                + " map at Resources/" + resourcePath);
        }

        return map;
    }

    // Loads up to three overlay sets: <texture>_d / _n / _ao. Every layer is
    // driven by a procedural mask, so no splatmap asset is required.
    private void LoadOverlays()
    {
        overlaysReady = false;

        for (int i = 0; i < overlays.Length; i++)
            overlays[i] = null;

        if (overlayConfig == null || overlayConfig.Length == 0)
        {
            ApplyOverlayMaps(terrainMaterial);
            return;
        }

        int slot = 0;

        foreach (TerrainOverlay config in overlayConfig)
        {
            if (slot >= MaxOverlaySlots || config == null || !config.enabled)
                continue;

            if (string.IsNullOrWhiteSpace(config.texture))
                continue;

            string baseName = NormalizeOverlayBaseName(config.texture);
            Texture2D albedo = Resources.Load<Texture2D>("objects/terrain/textures/" + baseName + "_d");

            if (albedo == null)
            {
                Debug.LogWarning("TerrainTileStreamer: Overlay albedo not found for '"
                    + config.texture + "' (expected " + baseName + "_d).");
                continue;
            }

            OverlayRuntime runtime = new OverlayRuntime
            {
                active = true,
                albedo = albedo,
                normal = Resources.Load<Texture2D>("objects/terrain/textures/" + baseName + "_n"),
                occlusion = Resources.Load<Texture2D>("objects/terrain/textures/" + baseName + "_ao")
            };

            int mode = OverlayModeToInt(config.mode);
            bool connectPois = config.connectPois || mode == 3;

            float tiling = config.tiling > 0f ? config.tiling : 0.05f;
            float angle = config.angle * Mathf.Deg2Rad;

            runtime.pA = new Vector4(
                tiling,
                Mathf.Clamp01(config.strength),
                config.fadeDistance,
                mode);

            runtime.pB = new Vector4(
                config.width > 0f ? config.width : 3f,
                config.feather > 0f ? config.feather : 4f,
                config.wanderAmplitude,
                config.wanderFrequency);

            runtime.pC = new Vector4(
                config.noiseScale > 0f ? config.noiseScale : 0.002f,
                Mathf.Clamp01(config.threshold),
                config.patchFeather > 0f ? config.patchFeather : 0.12f,
                config.seedOffset);

            runtime.pD = new Vector4(
                Mathf.Cos(angle),
                Mathf.Sin(angle),
                config.offset,
                config.seedOffset * 0.173f);

            runtime.pE = new Vector4(
                Mathf.Clamp01(config.normalStrength),
                Mathf.Clamp01(config.aoStrength),
                0f,
                0f);

            if (connectPois)
                LoadOverlayPoiSegments(runtime);
            else if (mode == 2)
                LoadOverlayWanderingSegments(runtime);

            runtime.pE.z = runtime.segmentCount;
            overlays[slot++] = runtime;
        }

        overlaysReady = true;

        if (terrainMaterial != null)
            ApplyOverlayMaps(terrainMaterial);
    }

    private static string NormalizeOverlayBaseName(string texture)
    {
        string baseName = texture.Trim();
        string lower = baseName.ToLowerInvariant();
        string[] suffixes = { "_disp", "_height", "_normal", "_occlusion", "_ao", "_d", "_n", "_h", "_r" };

        foreach (string suffix in suffixes)
        {
            if (lower.EndsWith(suffix))
            {
                baseName = baseName.Substring(0, baseName.Length - suffix.Length);
                break;
            }
        }

        return baseName;
    }

    private static int OverlayModeToInt(string mode)
    {
        string lower = (mode ?? string.Empty).Trim().ToLowerInvariant();

        switch (lower)
        {
            case "clifffoot":
            case "cliff-foot":
            case "cliff_foot":
            case "footsand":
                return 1;
            case "path":
            case "trail":
            case "wanderingpath":
                return 2;
            case "poipath":
            case "poi-path":
            case "poi_path":
            case "waypoints":
                return 3;
            default:
                return 0;
        }
    }

    // Sampling distance used to detect near-vertical mesa and canyon walls.
    private const float RouteWallSample = 70f;

    // Lower cost means better road terrain. Mesa tops and canyon floors are
    // usable; the steep edges between them are not. The sand apron is the
    // cheapest place to run a road along a cliff foot.
    private float CliffRouteCost(float worldX, float worldZ)
    {
        if (terrainGenerator == null)
            return 1f;

        Vector4 masks = terrainGenerator.SampleCliffRouteMasks(worldX, worldZ, 120f);
        float region = masks.y;
        float plateau = masks.z;
        float canyon = masks.w;
        float apron = masks.x;

        // Mesa and canyon walls are the steep parts of the real terrain, not
        // every wiggle of the noise masks. Measure the actual height slope
        // across a short span so roads may run on top or along a canyon floor
        // but never climb the vertical sides.
        float heightXPlus = terrainGenerator.GetHeightAtLocalPosition(
            Vector2Int.zero, new Vector3(worldX + RouteWallSample, 0f, worldZ));
        float heightXMinus = terrainGenerator.GetHeightAtLocalPosition(
            Vector2Int.zero, new Vector3(worldX - RouteWallSample, 0f, worldZ));
        float heightZPlus = terrainGenerator.GetHeightAtLocalPosition(
            Vector2Int.zero, new Vector3(worldX, 0f, worldZ + RouteWallSample));
        float heightZMinus = terrainGenerator.GetHeightAtLocalPosition(
            Vector2Int.zero, new Vector3(worldX, 0f, worldZ - RouteWallSample));

        float slope = Mathf.Max(
            Mathf.Abs(heightXPlus - heightXMinus),
            Mathf.Abs(heightZPlus - heightZMinus)) / (RouteWallSample * 2f);
        float wall = Mathf.InverseLerp(0.25f, 0.85f, slope) * region;
        float cost = 0.65f + region * 0.25f + plateau * 1.2f
            - apron * 0.6f - canyon * 0.8f + wall * 32f;

        return Mathf.Max(0.15f, cost);
    }

    // Integrated cost of a straight piece of road. A single expensive sample
    // (a wall crossing) is enough to reject the shortcut.
    private float SegmentRouteCost(RouteCostField field, Vector2 start, Vector2 end)
    {
        float distance = Vector2.Distance(start, end);

        if (distance < 1f)
            return 0f;

        int steps = Mathf.Clamp(Mathf.CeilToInt(distance / 70f), 3, 72);
        float total = 0f;
        float peak = 0f;

        for (int i = 0; i <= steps; i++)
        {
            Vector2 point = Vector2.Lerp(start, end, i / (float)steps);
            float cost = field.Sample(point.x, point.y);
            total += cost;

            if (cost > peak)
                peak = cost;
        }

        float average = total / (steps + 1);
        return (average + Mathf.Max(0f, peak - 4f) * 1.6f) * distance;
    }

    // Builds a curved route by choosing a lateral offset at each station along
    // the straight leg. Dynamic programming prices the actual ground between
    // stations, so the road bends around mesa walls and follows canyon floors
    // instead of jumping across them.
    private List<Vector2> BuildCliffAwareRoute(Vector2 start, Vector2 end, int maxSegments)
    {
        List<Vector2> route = new List<Vector2> { start, end };
        Vector2 delta = end - start;
        float distance = delta.magnitude;

        if (terrainGenerator == null || distance < 1f || maxSegments < 2)
            return route;

        Vector2 direction = delta / distance;
        Vector2 perpendicular = new Vector2(-direction.y, direction.x);

        int stationCount = Mathf.Clamp(Mathf.CeilToInt(distance / 700f) + 1, 5, 12);
        int sampleCount = 21;
        int zeroIndex = sampleCount / 2;
        float maxOffset = Mathf.Min(2400f, Mathf.Max(700f, distance * 0.9f));
        float[] offsets = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
            offsets[i] = Mathf.Lerp(-maxOffset, maxOffset, i / (float)(sampleCount - 1));

        RouteCostField field = new RouteCostField(
            this, start, end, maxOffset + RouteWallSample * 2f, 80f);

        Vector2[,] points = new Vector2[stationCount, sampleCount];
        float[,] pointCost = new float[stationCount, sampleCount];

        for (int station = 0; station < stationCount; station++)
        {
            Vector2 centre = start + delta * (station / (float)(stationCount - 1));

            for (int sample = 0; sample < sampleCount; sample++)
            {
                Vector2 point = centre + perpendicular * offsets[sample];
                points[station, sample] = point;
                pointCost[station, sample] = field.Sample(point.x, point.y)
                    + Mathf.Abs(offsets[sample]) * 0.0002f;
            }
        }

        float[,,] edgeCost = new float[stationCount - 1, sampleCount, sampleCount];

        for (int station = 1; station < stationCount; station++)
        {
            for (int previous = 0; previous < sampleCount; previous++)
            {
                for (int sample = 0; sample < sampleCount; sample++)
                {
                    edgeCost[station - 1, previous, sample] =
                        SegmentRouteCost(field, points[station - 1, previous], points[station, sample]);
                }
            }
        }

        float[,] cost = new float[stationCount, sampleCount];
        int[,] from = new int[stationCount, sampleCount];

        for (int sample = 0; sample < sampleCount; sample++)
            cost[0, sample] = sample == zeroIndex ? pointCost[0, sample] : float.MaxValue;

        for (int station = 1; station < stationCount; station++)
        {
            for (int sample = 0; sample < sampleCount; sample++)
            {
                float bestPrevious = float.MaxValue;
                int bestIndex = zeroIndex;

                for (int previous = 0; previous < sampleCount; previous++)
                {
                    if (cost[station - 1, previous] >= float.MaxValue * 0.5f)
                        continue;

                    float candidate = cost[station - 1, previous]
                        + edgeCost[station - 1, previous, sample];

                    if (station > 1)
                    {
                        int before = from[station - 1, previous];
                        float lastBend = offsets[previous] - offsets[before];
                        float nextBend = offsets[sample] - offsets[previous];
                        candidate += Mathf.Abs(nextBend - lastBend) * 0.9f;
                    }

                    if (candidate < bestPrevious)
                    {
                        bestPrevious = candidate;
                        bestIndex = previous;
                    }
                }

                cost[station, sample] = bestPrevious + pointCost[station, sample];
                from[station, sample] = bestIndex;
            }
        }

        // The road must finish exactly at the target waypoint.
        for (int sample = 0; sample < sampleCount; sample++)
        {
            if (sample != zeroIndex)
                cost[stationCount - 1, sample] = float.MaxValue;
        }

        int[] chosen = new int[stationCount];
        chosen[stationCount - 1] = zeroIndex;

        for (int station = stationCount - 1; station > 0; station--)
            chosen[station - 1] = from[station, chosen[station]];

        route.Clear();

        for (int station = 0; station < stationCount; station++)
            route.Add(points[station, chosen[station]]);

        route[0] = start;
        route[route.Count - 1] = end;

        // Keep only the bends that actually avoid cliffs, within the segment
        // budget available to this leg.
        while (route.Count - 1 > maxSegments)
        {
            float bestPenalty = float.MaxValue;
            int bestIndex = -1;

            for (int i = 1; i < route.Count - 1; i++)
            {
                float penalty = SegmentRouteCost(field, route[i - 1], route[i + 1])
                    - SegmentRouteCost(field, route[i - 1], route[i])
                    - SegmentRouteCost(field, route[i], route[i + 1]);

                if (penalty < bestPenalty)
                {
                    bestPenalty = penalty;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
                break;

            route.RemoveAt(bestIndex);
        }

        return route;
    }

    private void AddRouteSegment(OverlayRuntime runtime, Vector2 start, Vector2 end)
    {
        if (runtime.segmentCount >= MaxPathSegments)
            return;

        if ((end - start).sqrMagnitude < 1f)
            return;

        runtime.segments[runtime.segmentCount++] = new Vector4(
            start.x, start.y, end.x, end.y);
    }

    private void AddRouteSegments(OverlayRuntime runtime, List<Vector2> route)
    {
        for (int i = 0; i < route.Count - 1 && runtime.segmentCount < MaxPathSegments; i++)
            AddRouteSegment(runtime, route[i], route[i + 1]);
    }

    private void LoadOverlayPoiSegments(OverlayRuntime runtime)
    {
        runtime.segmentCount = 0;

        if (string.IsNullOrWhiteSpace(planetName))
            return;

        TextAsset asset = Resources.Load<TextAsset>(OGGetAddress.files + "PlanetPOIs");

        if (asset == null)
            return;

        PlanetPOIs definitions = JsonUtility.FromJson<PlanetPOIs>(asset.text);

        if (definitions == null || definitions.planetPOIsData == null)
            return;

        PlanetPOILocation location = null;

        foreach (PlanetPOILocation candidate in definitions.planetPOIsData)
        {
            if (candidate != null && string.Equals(candidate.planet, planetName,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                location = candidate;
                break;
            }
        }

        if (location == null)
            return;

        List<Vector2> points = new List<Vector2>
        {
            new Vector2(location.entryX, location.entryZ)
        };

        if (location.pois != null)
        {
            foreach (PlanetPOIEntry poi in location.pois)
            {
                if (poi != null)
                    points.Add(new Vector2(poi.x, poi.z));
            }
        }

        if (points.Count < 2)
            return;

        int legCount = points.Count - 1;
        float[] distances = new float[legCount];
        float totalDistance = 0f;

        for (int i = 0; i < legCount; i++)
        {
            distances[i] = Vector2.Distance(points[i], points[i + 1]);
            totalDistance += distances[i];
        }

        if (totalDistance < 1f)
            return;

        int allocated = 0;
        int[] budgets = new int[legCount];

        for (int i = 0; i < legCount - 1; i++)
        {
            budgets[i] = Mathf.Clamp(
                Mathf.RoundToInt(MaxPathSegments * distances[i] / totalDistance),
                2, MaxPathSegments - 2 * (legCount - 1 - i));
            allocated += budgets[i];
        }

        budgets[legCount - 1] = MaxPathSegments - allocated;

        for (int i = 0; i < legCount; i++)
        {
            List<Vector2> route = BuildCliffAwareRoute(
                points[i], points[i + 1], Mathf.Max(2, budgets[i]));
            AddRouteSegments(runtime, route);
        }
    }

    // Free trails use the same cliff-aware router, but their endpoints are a
    // long deterministic line through the planet instead of POI waypoints.
    private void LoadOverlayWanderingSegments(OverlayRuntime runtime)
    {
        runtime.segmentCount = 0;

        if (terrainGenerator == null)
            return;

        float angle = Mathf.Atan2(runtime.pD.y, runtime.pD.x);
        Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        Vector2 perpendicular = new Vector2(-direction.y, direction.x);
        const float routeLength = 8000f;

        Vector2 start = direction * (-routeLength * 0.5f)
            + perpendicular * runtime.pD.z;
        Vector2 end = direction * (routeLength * 0.5f)
            + perpendicular * runtime.pD.z;

        List<Vector2> route = BuildCliffAwareRoute(start, end, MaxPathSegments);
        AddRouteSegments(runtime, route);
    }

    private void ApplyOverlayMaps(Material material)
    {
        if (material == null)
            return;

        if (!overlaysReady)
        {
            material.SetFloat("_OverlayEnabled", 0f);
            return;
        }

        material.SetFloat("_OverlayEnabled", 1f);

        for (int i = 0; i < MaxOverlaySlots; i++)
        {
            string suffix = i.ToString();
            OverlayRuntime runtime = overlays[i];

            if (runtime == null || !runtime.active)
            {
                // Material.SetFloat cannot address a single vector component;
                // write the whole vector and clear only the relevant channel.
                Vector4 inactiveA = material.GetVector("_Overlay" + suffix + "A");
                inactiveA.y = 0f;
                material.SetVector("_Overlay" + suffix + "A", inactiveA);

                Vector4 inactiveE = material.GetVector("_Overlay" + suffix + "E");
                inactiveE.z = 0f;
                material.SetVector("_Overlay" + suffix + "E", inactiveE);
                continue;
            }

            material.SetTexture("_Overlay" + suffix + "Map", runtime.albedo);
            material.SetTexture("_Overlay" + suffix + "NormalMap", runtime.normal);
            material.SetTexture("_Overlay" + suffix + "AOMap", runtime.occlusion);
            material.SetVector("_Overlay" + suffix + "A", runtime.pA);
            material.SetVector("_Overlay" + suffix + "B", runtime.pB);
            material.SetVector("_Overlay" + suffix + "C", runtime.pC);
            material.SetVector("_Overlay" + suffix + "D", runtime.pD);
            material.SetVector("_Overlay" + suffix + "E", runtime.pE);
            material.SetVectorArray("_Overlay" + suffix + "Segments", runtime.segments);
        }
    }

    private void UpdateLoadedTiles()
    {
        HashSet<Vector2Int> requiredTiles = new();

        for (int z = -outerTileRadiusInTiles; z <= outerTileRadiusInTiles; z++)
        {
            for (int x = -outerTileRadiusInTiles; x <= outerTileRadiusInTiles; x++)
            {
                Vector2Int tileCoordinate = new Vector2Int(
                    currentShipTile.x + x,
                    currentShipTile.y + z
                );

                requiredTiles.Add(tileCoordinate);

                if (!loadedTiles.ContainsKey(tileCoordinate))
                {
                    bool isOuter = Mathf.Abs(x) > tileRadiusInTiles || Mathf.Abs(z) > tileRadiusInTiles;
                    LoadTile(tileCoordinate, isOuter);
                }
            }
        }

        List<Vector2Int> tilesToUnload = new();

        foreach (Vector2Int loadedCoordinate in loadedTiles.Keys)
        {
            if (!requiredTiles.Contains(loadedCoordinate))
            {
                tilesToUnload.Add(loadedCoordinate);
            }
        }

        foreach (Vector2Int coordinate in tilesToUnload)
        {
            UnloadTile(coordinate);
        }

        RefreshVegetationForLoadedTiles();
    }

    private VegetationPrototype[] BuildVegetationPrototypes()
    {
        if (vegetationConfig == null || vegetationConfig.Length == 0)
        {
            return null;
        }

        List<VegetationPrototype> prototypes = new List<VegetationPrototype>();

        foreach (VegetationSettings settings in vegetationConfig)
        {
            if (settings == null || settings.enabled == false || string.IsNullOrEmpty(settings.prefab))
            {
                continue;
            }

            string folder = string.IsNullOrEmpty(settings.folder) ? "objects/trees/" : settings.folder;
            GameObject prefab = Resources.Load<GameObject>(folder + settings.prefab);

            if (prefab == null)
            {
                Debug.LogWarning($"TerrainStreamer: vegetation prefab not found at Resources/{folder}{settings.prefab}");
                continue;
            }

            MeshFilter filter = prefab.GetComponentInChildren<MeshFilter>(true);
            MeshRenderer renderer = prefab.GetComponentInChildren<MeshRenderer>(true);

            if (filter == null || filter.sharedMesh == null)
            {
                Debug.LogWarning($"TerrainStreamer: vegetation prefab '{settings.prefab}' has no mesh.");
                continue;
            }

            Material source = renderer != null ? renderer.sharedMaterial : null;

            if (string.IsNullOrEmpty(settings.material) == false)
            {
                Material overrideMaterial = Resources.Load<Material>(folder + settings.material);
                if (overrideMaterial != null)
                {
                    source = overrideMaterial;
                }
                else
                {
                    Debug.LogWarning($"TerrainStreamer: vegetation material not found at Resources/{folder}{settings.material}");
                }
            }

            if (source == null)
            {
                Debug.LogWarning($"TerrainStreamer: vegetation '{settings.prefab}' has no material.");
                continue;
            }

            // Runtime instance: wind values can be tuned per planet without
            // writing the values back into the shared material asset.
            Material runtimeMaterial = new Material(source);
            runtimeMaterial.enableInstancing = true;

            if (runtimeMaterial.HasProperty("_WindStrength"))
                runtimeMaterial.SetFloat("_WindStrength", settings.windStrength);
            if (runtimeMaterial.HasProperty("_WindSpeed"))
                runtimeMaterial.SetFloat("_WindSpeed", settings.windSpeed);
            if (runtimeMaterial.HasProperty("_WindFrequency"))
                runtimeMaterial.SetFloat("_WindFrequency", settings.windFrequency);
            if (runtimeMaterial.HasProperty("_WindHeight"))
                runtimeMaterial.SetFloat("_WindHeight", Mathf.Max(0.001f, filter.sharedMesh.bounds.size.y));

            prototypes.Add(new VegetationPrototype
            {
                mesh = filter.sharedMesh,
                material = runtimeMaterial,
                settings = settings
            });
        }

        return prototypes.ToArray();
    }

    // A tile loaded far away gets its vegetation when it later enters the
    // vegetation radius (tiles are kept loaded out to the terrain radius).
    private void RefreshVegetationForLoadedTiles()
    {
        if (vegetationPrototypes == null || vegetationPrototypes.Length == 0)
        {
            return;
        }

        foreach (KeyValuePair<Vector2Int, GameObject> loaded in loadedTiles)
        {
            GameObject tile = loaded.Value;
            if (tile == null)
            {
                continue;
            }

            if (tile.GetComponents<TileVegetation>().Length >= vegetationPrototypes.Length)
            {
                continue;
            }

            Vector2Int delta = loaded.Key - currentShipTile;
            if (Mathf.Abs(delta.x) > vegetationTileRadius || Mathf.Abs(delta.y) > vegetationTileRadius)
            {
                continue;
            }

            for (int i = 0; i < vegetationPrototypes.Length; i++)
            {
                TileVegetation.Attach(tile, loaded.Key, tileSize, terrainGenerator, vegetationPrototypes[i], ship);
            }
        }
    }

    private void LoadTile(Vector2Int coordinate, bool isOuter = false)
    {
        Mesh mesh = isOuter
            ? terrainGenerator.GenerateMesh(coordinate, outerVerticesPerSide)
            : terrainGenerator.GenerateMesh(coordinate);

        GameObject tileObject = new GameObject($"Tile_{coordinate.x}_{coordinate.y}");
        tileObject.transform.SetParent(tileParent);

        // Position the tile based on its coordinate
        Vector3 tileLocalPosition = new Vector3(
            coordinate.x * tileSize,
            0f,
            coordinate.y * tileSize
        );
        tileObject.transform.localPosition = tileLocalPosition;
        tileObject.transform.localRotation = Quaternion.identity;

        MeshFilter meshFilter = tileObject.AddComponent<MeshFilter>();
        meshFilter.mesh = mesh;

        MeshRenderer meshRenderer = tileObject.AddComponent<MeshRenderer>();

        if (isOuter)
        {
            // Distant ring: shared material, no collider, no per-tile cost.
            meshRenderer.sharedMaterial = sharedOuterMaterial;
            loadedTiles.Add(coordinate, tileObject);
            return;
        }

        // Create a unique material instance
        Material materialInstance = new Material(terrainMaterial);

        // Use deterministic selection for the final texture
        int finalIndex = GetFinalTextureIndex(coordinate);
        SetTileGroundMaps(materialInstance, finalIndex);

        // Set neighbor textures for blending (uses same deterministic selection)
        SetNeighborTextures(materialInstance, coordinate);

        SetTileCliffMaps(materialInstance, coordinate);

        meshRenderer.material = materialInstance;

        // Add collider for physics
        MeshCollider collider = tileObject.AddComponent<MeshCollider>();
        collider.convex = false;

        loadedTiles.Add(coordinate, tileObject);
    }

    // Kept for single-hash lookups if you need them elsewhere
    private int GetDeterministicTextureIndex(Vector2Int coordinate)
    {
        int hash = CoordinateHash(seed, coordinate.x, coordinate.y);
        return PositiveModulo(hash, terrainTextures.Length);
    }

    // Deterministic selection among multiple hashed candidates so neighbors and tiles always agree
    private int GetFinalTextureIndex(Vector2Int coordinate)
    {
        int idxA = PositiveModulo(CoordinateHash(seed, coordinate.x, coordinate.y), terrainTextures.Length);
        int idxB = PositiveModulo(CoordinateHash(seed + 100, coordinate.x, coordinate.y), terrainTextures.Length);
        int idxC = PositiveModulo(CoordinateHash(seed + 200, coordinate.x, coordinate.y), terrainTextures.Length);

        // Deterministically choose one of the three variants with another hash
        int selector = PositiveModulo(CoordinateHash(seed + 300, coordinate.x, coordinate.y), 3);
        return new int[] { idxA, idxB, idxC }[selector];
    }

    private void SetNeighborTextures(Material material, Vector2Int tileCoordinate)
    {
        // Keep whatever offsets you decided are correct for your world/UVs.
        // These should use the same selection logic as the tile itself so they match.
        Vector2Int right = tileCoordinate + new Vector2Int(1, 0); // use your chosen offset convention
        Vector2Int front = tileCoordinate + new Vector2Int(0, 1);

        int rightIndex = GetFinalTextureIndex(right);
        int frontIndex = GetFinalTextureIndex(front);

        material.SetTexture("_RightGroundMap", terrainTextures[rightIndex]);
        material.SetTexture("_FrontGroundMap", terrainTextures[frontIndex]);
    }

    // Cliff texture pair + slope threshold per tile. Two maps are blended in
    // the shader with world-space noise, so a cliff crossing a tile border
    // does not switch texture abruptly.
    private void SetTileCliffMaps(Material material, Vector2Int coordinate)
    {
        material.SetFloat("_CliffStart", cliffSlopeStart);
        material.SetFloat("_CliffSoftness", Mathf.Max(0.001f, cliffSlopeSoftness));

        if (cliffTextures == null || cliffTextures.Length == 0)
            return;

        int indexA = PositiveModulo(
            CoordinateHash(seed + 400, coordinate.x, coordinate.y),
            cliffTextures.Length);
        Texture2D cliffA = cliffTextures[indexA];
        Texture2D cliffB = cliffA;
        float blend = 0f;

        if (cliffTextures.Length > 1)
        {
            // Offset in [1, n-1] so B is never the same map as A.
            int offset = 1 + PositiveModulo(
                CoordinateHash(seed + 500, coordinate.x, coordinate.y),
                cliffTextures.Length - 1);
            int indexB = PositiveModulo(indexA + offset, cliffTextures.Length);

            cliffB = cliffTextures[indexB];
            blend = 1f;
        }

        material.SetTexture("_CliffMap", cliffA);
        material.SetTexture("_CliffMapB", cliffB);
        material.SetFloat("_CliffBlendAmount", blend);
    }

    private void UnloadTile(Vector2Int coordinate)
    {
        if (!loadedTiles.TryGetValue(coordinate, out GameObject tile))
            return;

        if (tile != null)
        {
            Destroy(tile);
        }

        loadedTiles.Remove(coordinate);
    }

    private Vector2Int LocalPositionToTileCoordinate(Vector3 localPosition)
    {
        int tileX = Mathf.FloorToInt(localPosition.x / tileSize);
        int tileZ = Mathf.FloorToInt(localPosition.z / tileSize);

        return new Vector2Int(tileX, tileZ);
    }

    private static int CoordinateHash(int seed, int x, int y)
    {
        unchecked
        {
            int hash = seed;

            hash = hash * 31 + x;
            hash = hash * 31 + y;

            hash ^= hash << 13;
            hash ^= hash >> 17;
            hash ^= hash << 5;

            return hash;
        }
    }

    private static int PositiveModulo(int value, int modulus)
    {
        int result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private void OnDestroy()
    {
        foreach (GameObject tile in loadedTiles.Values)
        {
            if (tile != null)
            {
                Destroy(tile);
            }
        }

        loadedTiles.Clear();

        if (sharedOuterMaterial != null)
        {
            Destroy(sharedOuterMaterial);
            sharedOuterMaterial = null;
        }
    }
}