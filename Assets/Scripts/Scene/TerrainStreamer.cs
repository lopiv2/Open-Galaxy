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
    // Editor-only override: the terrain preview tool feeds its unsaved POI
    // draft here so "poiPath" overlays follow edited waypoints live. Not
    // serialized into scenes; the game always reads PlanetPOIs.json.
    [System.NonSerialized] public PlanetPOILocation poiOverride;
    [Header("Footsteps")]
    [Tooltip("Per-planet footstep surfaces from StarSystems.json (texture keyword -> audioclip).")]
    public FootstepSurface[] footstepConfig;
    [Header("Landing dust")]
    [Tooltip("Per-planet landing dust tuning from StarSystems.json (tint / amount / audio).")]
    public LandingDustSettings landingDust;
    private const int MaxOverlaySlots = 3;
    private const int MaxPathSegments = 24;

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
        public string footstepClip;
        public float routeCurviness;
        public float routeMaxDetour;
        public float routeWallClearance;
        public int routeSeed;
        public string routeStatus = "";
    }

    // Precomputed road cost for the rectangle a leg may wander through.
    // Evaluating the terrain directly for every A* edge is far too expensive,
    // so the detailed sampling happens once per cell. Steep walls are marked
    // blocked; mesa tops and canyon floors stay usable, and the intermediate
    // slope bands are treated as ramps.
    private class RouteCostField
    {
        private readonly TerrainStreamer owner;
        private readonly float originX;
        private readonly float originZ;
        private readonly float cellSize;
        private readonly int width;
        private readonly int height;
        private readonly float[] costs;
        private readonly bool[] blocked;
        private readonly bool[] ramps;

        public int Width => width;
        public int Height => height;
        public float CellSize => cellSize;

        public RouteCostField(TerrainStreamer owner, Vector2 start, Vector2 end,
            float maxOffset, float requestedCellSize, float clearance)
        {
            this.owner = owner;
            Vector2 direction = (end - start).normalized;
            Vector2 perpendicular = new Vector2(-direction.y, direction.x);
            // Leave room before/after the leg so a wall that continues past a
            // waypoint can still be walked around instead of crossed.
            Vector2 alongMargin = direction * Mathf.Max(0f, maxOffset) * 0.5f;
            Vector2 a = start + perpendicular * maxOffset - alongMargin;
            Vector2 b = start - perpendicular * maxOffset - alongMargin;
            Vector2 c = end + perpendicular * maxOffset + alongMargin;
            Vector2 d = end - perpendicular * maxOffset + alongMargin;

            float minX = Mathf.Min(Mathf.Min(a.x, b.x), Mathf.Min(c.x, d.x));
            float maxX = Mathf.Max(Mathf.Max(a.x, b.x), Mathf.Max(c.x, d.x));
            float minZ = Mathf.Min(Mathf.Min(a.y, b.y), Mathf.Min(c.y, d.y));
            float maxZ = Mathf.Max(Mathf.Max(a.y, b.y), Mathf.Max(c.y, d.y));

            float spanX = maxX - minX;
            float spanZ = maxZ - minZ;
            cellSize = Mathf.Max(requestedCellSize,
                Mathf.Max(spanX, spanZ) / 239f);

            const int margin = 3;
            width = Mathf.Clamp(Mathf.CeilToInt(spanX / cellSize) + margin * 2, 4, 240);
            height = Mathf.Clamp(Mathf.CeilToInt(spanZ / cellSize) + margin * 2, 4, 240);

            originX = (minX + maxX) * 0.5f - (width - 1) * cellSize * 0.5f;
            originZ = (minZ + maxZ) * 0.5f - (height - 1) * cellSize * 0.5f;
            costs = new float[width * height];
            ramps = new bool[width * height];
            bool[] walls = new bool[width * height];

            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    RouteTerrainSample sample = owner.SampleRouteTerrain(
                        originX + (x + 0.5f) * cellSize,
                        originZ + (z + 0.5f) * cellSize);
                    int index = z * width + x;
                    costs[index] = sample.cost;
                    ramps[index] = sample.ramp;
                    walls[index] = sample.blocked;
                }
            }

            int clearanceCells = Mathf.Clamp(
                Mathf.CeilToInt(Mathf.Max(0f, clearance) / cellSize), 0, 8);

            if (clearanceCells <= 0)
            {
                blocked = walls;
            }
            else
            {
                blocked = new bool[walls.Length];

                for (int z = 0; z < height; z++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int index = z * width + x;
                        if (!walls[index])
                            continue;

                        for (int dz = -clearanceCells; dz <= clearanceCells; dz++)
                        {
                            for (int dx = -clearanceCells; dx <= clearanceCells; dx++)
                            {
                                int nx = x + dx;
                                int nz = z + dz;
                                if (nx < 0 || nz < 0 || nx >= width || nz >= height)
                                    continue;

                                int neighbour = nz * width + nx;
                                // Do not let clearance swallow a ramp: a narrow
                                // ramp is the only legal way down a mesa face.
                                if (!ramps[neighbour])
                                    blocked[neighbour] = true;
                            }
                        }
                    }
                }

                // The wall cells themselves always stay blocked.
                for (int i = 0; i < blocked.Length; i++)
                {
                    if (walls[i])
                        blocked[i] = true;
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

        public bool IsBlockedCell(int x, int z)
        {
            return blocked[z * width + x];
        }

        public float CellCost(int x, int z)
        {
            return costs[z * width + x];
        }

        public Vector2 CellCenter(int x, int z)
        {
            return new Vector2(
                originX + (x + 0.5f) * cellSize,
                originZ + (z + 0.5f) * cellSize);
        }

        public void WorldToCell(Vector2 world, out int x, out int z)
        {
            x = Mathf.Clamp(
                Mathf.FloorToInt((world.x - originX) / cellSize), 0, width - 1);
            z = Mathf.Clamp(
                Mathf.FloorToInt((world.y - originZ) / cellSize), 0, height - 1);
        }

        // Fast grid query for A* and path simplification.
        public bool IsBlockedWorld(float worldX, float worldZ)
        {
            WorldToCell(new Vector2(worldX, worldZ), out int x, out int z);
            return IsBlockedCell(x, z);
        }

        // Exact query used only by the final validation pass. Sampling the
        // full terrain for every A* edge made the preview take minutes.
        public bool IsBlockedWorldExact(float worldX, float worldZ)
        {
            if (owner != null && owner.SampleRouteTerrain(worldX, worldZ).blocked)
                return true;

            WorldToCell(new Vector2(worldX, worldZ), out int x, out int z);
            return IsBlockedCell(x, z);
        }
    }

    private struct RouteTerrainSample
    {
        public float cost;
        public bool blocked;
        public bool ramp;
    }

    private struct RouteHeapEntry
    {
        public int node;
        public float priority;
    }

    private class RouteMinHeap
    {
        private readonly List<RouteHeapEntry> items = new List<RouteHeapEntry>();

        public int Count => items.Count;

        public void Push(int node, float priority)
        {
            items.Add(new RouteHeapEntry { node = node, priority = priority });
            int index = items.Count - 1;

            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (items[parent].priority <= items[index].priority)
                    break;

                RouteHeapEntry swap = items[parent];
                items[parent] = items[index];
                items[index] = swap;
                index = parent;
            }
        }

        public int Pop(out float priority)
        {
            RouteHeapEntry root = items[0];
            int last = items.Count - 1;
            items[0] = items[last];
            items.RemoveAt(last);
            priority = root.priority;

            int index = 0;

            while (true)
            {
                int left = index * 2 + 1;
                int right = left + 1;
                int smallest = index;

                if (left < items.Count && items[left].priority < items[smallest].priority)
                    smallest = left;
                if (right < items.Count && items[right].priority < items[smallest].priority)
                    smallest = right;
                if (smallest == index)
                    break;

                RouteHeapEntry swap = items[index];
                items[index] = items[smallest];
                items[smallest] = swap;
                index = smallest;
            }

            return root.node;
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
                occlusion = Resources.Load<Texture2D>("objects/terrain/textures/" + baseName + "_ao"),
                footstepClip = config.footstepClip
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

            runtime.routeCurviness = ResolveCurviness(config);
            runtime.routeMaxDetour = ResolveMaxDetour(config);
            float routeWidth = config.width > 0f ? config.width : 3f;
            float routeFeather = config.feather > 0f ? config.feather : 4f;
            float extraClearance = config.wallClearance > 0f
                ? config.wallClearance
                : 6f;
            // Keep the whole painted strip, not only its centreline, off walls.
            runtime.routeWallClearance = extraClearance
                + (routeWidth + routeFeather) * 0.5f;
            runtime.routeSeed = Mathf.RoundToInt(config.seedOffset * 1000f);

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

    private static string NormalizeCurvePreset(string preset)
    {
        string lower = (preset ?? string.Empty).Trim().ToLowerInvariant();

        switch (lower)
        {
            case "straight":
            case "recto":
                return "straight";
            case "winding":
            case "serpenteante":
            case "curvo":
                return "winding";
            default:
                return "balanced";
        }
    }

    private static float PresetCurviness(string preset)
    {
        switch (NormalizeCurvePreset(preset))
        {
            case "straight":
                return 0.05f;
            case "winding":
                return 0.80f;
            default:
                return 0.35f;
        }
    }

    private static float ResolveCurviness(TerrainOverlay config)
    {
        // Old JSONs have none of these fields. Treat 0 (without the explicit
        // flag) as "unset" so they still get the preset curve instead of
        // silently becoming perfectly straight.
        if (config.curvinessSet && config.curviness >= 0f)
            return Mathf.Clamp01(config.curviness);

        if (config.curviness > 0f)
            return Mathf.Clamp01(config.curviness);

        return PresetCurviness(config.curvePreset);
    }

    private static float ResolveMaxDetour(TerrainOverlay config)
    {
        if (config.maxDetour > 0f)
            return Mathf.Max(200f, config.maxDetour);

        switch (NormalizeCurvePreset(config.curvePreset))
        {
            case "straight":
                return 2000f;
            case "winding":
                return 3600f;
            default:
                return 2600f;
        }
    }

    // Footstep surface query for the on-foot character. Priority:
    // 1) path/poiPath overlay with a footstepClip when the foot is on/near
    //    its segments, 2) base tile texture keyword match in footstepConfig,
    // 3) default/empty-texture fallback entry. Supports planets with several
    // ground textures: each tile resolves its own _GroundMap.
    public bool TryGetFootstep(Vector3 worldPosition, out string clipName, out float volume, out float pitchMin, out float pitchMax)
    {
        clipName = null;
        volume = 0.6f;
        pitchMin = 0.92f;
        pitchMax = 1.08f;

        // Base ground texture under the foot (per-tile material instance).
        string groundTexture = GetTileGroundTextureName(worldPosition);

        // Overlay paths first: they win when the foot is inside their width.
        string overlayClip = GetOverlayFootstepClip(worldPosition);
        if (string.IsNullOrEmpty(overlayClip) == false)
        {
            clipName = overlayClip;
            ApplyFootstepTuning(clipName, ref volume, ref pitchMin, ref pitchMax);
            return true;
        }

        if (footstepConfig == null || footstepConfig.Length == 0)
        {
            return false;
        }

        string groundLower = (groundTexture ?? string.Empty).ToLowerInvariant();

        // Exact/keyword matches first (in JSON order).
        foreach (FootstepSurface surface in footstepConfig)
        {
            if (surface == null || string.IsNullOrEmpty(surface.clip))
            {
                continue;
            }

            string key = (surface.texture ?? string.Empty).Trim().ToLowerInvariant();
            if (key.Length == 0 || key == "default" || key == "any" || key == "*")
            {
                continue;
            }

            if (groundLower.Length > 0 && groundLower.Contains(key))
            {
                clipName = surface.clip;
                volume = surface.volume > 0f ? surface.volume : volume;
                pitchMin = surface.pitchMin;
                pitchMax = surface.pitchMax;
                return true;
            }
        }

        // Fallback entry (empty/default texture) or first entry.
        foreach (FootstepSurface surface in footstepConfig)
        {
            if (surface == null || string.IsNullOrEmpty(surface.clip))
            {
                continue;
            }

            string key = (surface.texture ?? string.Empty).Trim().ToLowerInvariant();
            if (key.Length == 0 || key == "default" || key == "any" || key == "*")
            {
                clipName = surface.clip;
                volume = surface.volume > 0f ? surface.volume : volume;
                pitchMin = surface.pitchMin;
                pitchMax = surface.pitchMax;
                return true;
            }
        }

        return false;
    }

    private string GetTileGroundTextureName(Vector3 worldPosition)
    {
        Vector3 origin = new Vector3(worldPosition.x, worldPosition.y + 400f, worldPosition.z);
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, origin.y + 2000f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        string textureName = null;

        foreach (RaycastHit hit in hits)
        {
            if (hit.transform == null || hit.transform.name.StartsWith("Tile_") == false)
            {
                continue;
            }

            if (hit.point.y <= best)
            {
                continue;
            }

            best = hit.point.y;
            MeshRenderer renderer = hit.transform.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.sharedMaterial != null)
            {
                Texture ground = renderer.sharedMaterial.GetTexture("_GroundMap");
                if (ground != null)
                {
                    textureName = ground.name;
                }
            }
        }

        return textureName;
    }

    private string GetOverlayFootstepClip(Vector3 worldPosition)
    {
        if (overlays == null || tileParent == null)
        {
            return null;
        }

        Vector3 local = tileParent.InverseTransformPoint(worldPosition);
        Vector2 p = new Vector2(local.x, local.z);

        for (int i = 0; i < overlays.Length; i++)
        {
            OverlayRuntime runtime = overlays[i];
            if (runtime == null || runtime.active == false || string.IsNullOrEmpty(runtime.footstepClip))
            {
                continue;
            }

            int mode = (int)runtime.pA.w;
            if (mode != 2 && mode != 3)
            {
                continue;
            }

            float halfWidth = Mathf.Max(0.5f, runtime.pB.x * 0.5f + runtime.pB.y * 0.5f);
            float halfWidthSqr = halfWidth * halfWidth;

            for (int s = 0; s < runtime.segmentCount; s++)
            {
                Vector4 seg = runtime.segments[s];
                Vector2 a = new Vector2(seg.x, seg.y);
                Vector2 b = new Vector2(seg.z, seg.w);
                if (DistancePointToSegmentSqr(p, a, b) <= halfWidthSqr)
                {
                    return runtime.footstepClip;
                }
            }
        }

        return null;
    }

    private static float DistancePointToSegmentSqr(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float denom = ab.sqrMagnitude;
        if (denom < 0.0001f)
        {
            return (p - a).sqrMagnitude;
        }

        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / denom);
        return (p - (a + ab * t)).sqrMagnitude;
    }

    private void ApplyFootstepTuning(string clipName, ref float volume, ref float pitchMin, ref float pitchMax)
    {
        if (footstepConfig == null)
        {
            return;
        }

        foreach (FootstepSurface surface in footstepConfig)
        {
            if (surface != null && string.Equals(surface.clip, clipName, System.StringComparison.OrdinalIgnoreCase))
            {
                volume = surface.volume > 0f ? surface.volume : volume;
                pitchMin = surface.pitchMin;
                pitchMax = surface.pitchMax;
                return;
            }
        }
    }

    // Sampling distance used to detect near-vertical mesa and canyon walls.
    // 30 m catches the actual cliff face; 70 m averaged a 30 m wall down into
    // a harmless ramp, which is why roads used to cross it.
    private const float RouteWallSample = 30f;
    // Slopes below this are flat enough for a road. The band up to the wall
    // threshold is treated as a ramp; anything steeper is a wall. The wall
    // threshold is deliberately above normal mountain slopes so routes are
    // not cut off by every steep dune.
    private const float RouteRampSlopeStart = 0.15f;
    private const float RouteWallSlopeStart = 0.45f;
    // A* cell size. 45 m keeps the road away from narrow walls while staying
    // cheap enough to evaluate at load time.
    private const float RouteCellSize = 45f;
    private const float RouteMinCost = 0.15f;

    // Lower cost means better road terrain. Mesa tops and canyon floors are
    // usable; steep walls are blocked. The intermediate slope band is a ramp
    // and stays passable at a higher cost, so a road can climb down to a
    // canyon floor without crossing a vertical face.
    private RouteTerrainSample SampleRouteTerrain(float worldX, float worldZ)
    {
        RouteTerrainSample sample = new RouteTerrainSample { cost = 1f };

        if (terrainGenerator == null)
            return sample;

        Vector4 masks = terrainGenerator.SampleCliffRouteMasks(worldX, worldZ, 120f);
        float region = masks.y;
        float plateau = masks.z;
        float canyon = masks.w;
        float apron = masks.x;

        // Measure the actual height slope across a short span. This is more
        // reliable than the masks alone, which also cover broad, flat tops.
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

        // Only the actual mesa/canyon layer counts as a cliff. The base
        // mountains can be steep too, but they are not vertical walls and
        // must not fragment every cross-country route.
        bool cliffFeature = region > 0.05f
            && (plateau > 0.02f || canyon > 0.02f);

        sample.blocked = cliffFeature && slope >= RouteWallSlopeStart;
        sample.ramp = !sample.blocked && cliffFeature
            && slope >= RouteRampSlopeStart;

        float cost = 0.70f + region * 0.20f + plateau * 0.15f
            - apron * 0.55f - canyon * 0.20f;

        if (sample.ramp)
        {
            float rampT = Mathf.InverseLerp(
                RouteRampSlopeStart, RouteWallSlopeStart, slope);
            cost += Mathf.Lerp(2.2f, 7.5f, rampT);
        }

        if (sample.blocked)
            cost = 40f;

        sample.cost = Mathf.Max(RouteMinCost, cost);
        return sample;
    }

    // A straight piece of road is safe when no interior sample lands in a
    // blocked (wall or clearance) cell. Endpoints are allowed: a POI can sit
    // right at the edge of a mesa, and the road itself still must not cut the
    // wall between its waypoints.
    private static bool IsRouteSegmentSafe(
        RouteCostField field, Vector2 start, Vector2 end, bool exact = false)
    {
        float distance = Vector2.Distance(start, end);

        if (distance < 1f)
            return true;

        int steps = Mathf.Clamp(
            Mathf.CeilToInt(distance / Mathf.Max(4f, field.CellSize * 0.35f)),
            4, 240);

        for (int i = 1; i < steps; i++)
        {
            float t = i / (float)steps;

            if (t < 0.02f || t > 0.98f)
                continue;

            Vector2 point = Vector2.Lerp(start, end, t);
            bool blocked = exact
                ? field.IsBlockedWorldExact(point.x, point.y)
                : field.IsBlockedWorld(point.x, point.y);

            if (blocked)
                return false;
        }

        return true;
    }

    private static Vector2 FindLastSafePoint(
        RouteCostField field, Vector2 start, Vector2 end)
    {
        float distance = Vector2.Distance(start, end);

        if (distance < 1f)
            return start;

        int steps = Mathf.Clamp(
            Mathf.CeilToInt(distance / Mathf.Max(4f, field.CellSize * 0.35f)),
            4, 240);

        Vector2 safe = start;

        for (int i = 1; i < steps; i++)
        {
            Vector2 point = Vector2.Lerp(start, end, i / (float)steps);

            if (field.IsBlockedWorldExact(point.x, point.y))
                break;

            safe = point;
        }

        return safe;
    }

    // Greedy line-of-sight simplification: keep the farthest point that can
    // still be reached without touching a wall. This removes A*'s grid
    // staircase without ever cutting a corner through a cliff.
    private static List<Vector2> SimplifyRoute(
        List<Vector2> points, int maxSegments, RouteCostField field)
    {
        if (points == null || points.Count <= 2)
            return points;

        List<Vector2> simplified = new List<Vector2> { points[0] };
        int index = 0;

        while (index < points.Count - 1)
        {
            int farthest = index + 1;

            for (int candidate = points.Count - 1; candidate > index + 1; candidate--)
            {
                if (!IsRouteSegmentSafe(field, points[index], points[candidate]))
                    continue;

                farthest = candidate;
                break;
            }

            simplified.Add(points[farthest]);
            index = farthest;
        }

        // Rarely, a very twisted safe route needs more segments than the
        // shader budget. Remove the cheapest safe bend until it fits.
        while (simplified.Count - 1 > maxSegments)
        {
            float bestPenalty = float.MaxValue;
            int bestIndex = -1;

            for (int i = 1; i < simplified.Count - 1; i++)
            {
                if (!IsRouteSegmentSafe(field, simplified[i - 1], simplified[i + 1]))
                    continue;

                float before = Vector2.Distance(simplified[i - 1], simplified[i])
                    + Vector2.Distance(simplified[i], simplified[i + 1]);
                float after = Vector2.Distance(simplified[i - 1], simplified[i + 1]);
                float penalty = after - before;

                if (penalty < bestPenalty)
                {
                    bestPenalty = penalty;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
                break;

            simplified.RemoveAt(bestIndex);
        }

        return simplified;
    }

    // Evenly re-samples a safe polyline. A straight leg simplifies to just
    // two points, and without interior points ApplyCurviness would have
    // nowhere to push a bend. Sampling along the existing safe polyline keeps
    // the result inside the already validated corridor.
    private static List<Vector2> ResampleRoute(
        List<Vector2> route, int pointCount)
    {
        if (route == null || route.Count < 2 || pointCount < 3)
            return route;

        if (route.Count >= pointCount)
            return route;

        float[] cumulative = new float[route.Count];

        for (int i = 1; i < route.Count; i++)
            cumulative[i] = cumulative[i - 1] + Vector2.Distance(route[i - 1], route[i]);

        float totalDistance = cumulative[cumulative.Length - 1];

        if (totalDistance < 1f)
            return route;

        List<Vector2> result = new List<Vector2>(pointCount) { route[0] };

        for (int i = 1; i < pointCount - 1; i++)
        {
            float target = totalDistance * i / (pointCount - 1);
            int segment = 0;

            while (segment < route.Count - 2 && cumulative[segment + 1] < target)
                segment++;

            float segmentLength = cumulative[segment + 1] - cumulative[segment];
            float t = segmentLength > 0.001f
                ? Mathf.Clamp01((target - cumulative[segment]) / segmentLength)
                : 0f;
            result.Add(Vector2.Lerp(route[segment], route[segment + 1], t));
        }

        result.Add(route[route.Count - 1]);
        return result;
    }

    // Adds a controlled serpentine to a route that is already known to avoid
    // walls. Each candidate offset is validated against the same blocked
    // field, so a curve is only kept when there is room for it.
    private static List<Vector2> ApplyCurviness(
        List<Vector2> route, float curviness, float maxDetour,
        RouteCostField field, int routeSeed)
    {
        if (route == null || route.Count < 3 || curviness <= 0.001f)
            return route;

        float totalDistance = 0f;
        float[] cumulative = new float[route.Count];

        for (int i = 1; i < route.Count; i++)
        {
            totalDistance += Vector2.Distance(route[i - 1], route[i]);
            cumulative[i] = totalDistance;
        }

        if (totalDistance < 1f)
            return route;

        float amplitude = Mathf.Min(
            Mathf.Max(0f, maxDetour) * 0.18f, 220f) * Mathf.Clamp01(curviness);
        // Short legs should not get a huge sideways loop just because the
        // preset's amplitude is large.
        amplitude = Mathf.Min(amplitude, totalDistance * 0.12f);

        if (amplitude < 0.5f)
            return route;

        int maxWaves = Mathf.Max(1, (route.Count - 1) / 4);
        int waves = Mathf.Clamp(
            Mathf.RoundToInt(totalDistance / 1100f), 1, maxWaves);
        float phaseOffset = Mathf.Abs(routeSeed % 360) * Mathf.Deg2Rad;
        List<Vector2> curved = new List<Vector2>(route);

        for (int i = 1; i < route.Count - 1; i++)
        {
            Vector2 previous = curved[i - 1];
            Vector2 next = curved[i + 1];
            Vector2 tangent = next - previous;

            if (tangent.sqrMagnitude < 0.01f)
                continue;

            tangent.Normalize();
            Vector2 normal = new Vector2(-tangent.y, tangent.x);
            float u = cumulative[i] / totalDistance;
            float taper = Mathf.Sin(u * Mathf.PI);
            float wave = Mathf.Sin(u * Mathf.PI * 2f * waves + phaseOffset);
            float offset = wave * amplitude * taper;

            if (Mathf.Abs(offset) < 0.5f)
                continue;

            Vector2 candidate = route[i] + normal * offset;

            if (IsRouteSegmentSafe(field, previous, candidate)
                && IsRouteSegmentSafe(field, candidate, next))
            {
                curved[i] = candidate;
                continue;
            }

            candidate = route[i] + normal * offset * 0.5f;

            if (IsRouteSegmentSafe(field, previous, candidate)
                && IsRouteSegmentSafe(field, candidate, next))
            {
                curved[i] = candidate;
            }
        }

        return curved;
    }

    // Last line of defence: if even the chosen polyline touches a wall, cut
    // it at the last safe sample instead of painting across the cliff.
    private static List<Vector2> TrimBlockedRoute(
        List<Vector2> route, RouteCostField field, out bool trimmed)
    {
        trimmed = false;

        if (route == null || route.Count < 2)
            return route;

        for (int i = 0; i < route.Count - 1; i++)
        {
            if (IsRouteSegmentSafe(field, route[i], route[i + 1], true))
                continue;

            Vector2 safe = FindLastSafePoint(field, route[i], route[i + 1]);
            route.RemoveRange(i + 1, route.Count - (i + 1));

            if (Vector2.Distance(route[i], safe) > 1f)
                route.Add(safe);

            trimmed = true;
            return route;
        }

        return route;
    }

    private static string AppendRouteStatus(string current, string message)
    {
        return string.IsNullOrEmpty(current) ? message : current + " | " + message;
    }

    // Builds a route on a cost grid. Walls are hard-blocked; mesa tops and
    // canyon floors are legal; ramp cells connect the two. If the target is
    // unreachable the route stops at the closest safe cell and reports it.
    private List<Vector2> BuildCliffAwareRoute(
        Vector2 start, Vector2 end, int maxSegments,
        float curviness, float maxDetour, float wallClearance,
        int routeSeed, out bool reachedTarget, out string warning)
    {
        reachedTarget = true;
        warning = null;

        List<Vector2> route = new List<Vector2> { start, end };
        float distance = Vector2.Distance(start, end);

        if (terrainGenerator == null || distance < 1f || maxSegments < 2)
            return route;

        float detour = Mathf.Max(200f, maxDetour);
        float clearance = Mathf.Max(0f, wallClearance);

        RouteCostField field = new RouteCostField(
            this, start, end, detour, RouteCellSize, clearance);

        field.WorldToCell(start, out int startX, out int startZ);
        field.WorldToCell(end, out int endX, out int endZ);

        int count = field.Width * field.Height;
        float[] gScore = new float[count];
        float[] bestF = new float[count];
        int[] cameFrom = new int[count];
        bool[] closed = new bool[count];

        for (int i = 0; i < count; i++)
        {
            gScore[i] = float.MaxValue;
            bestF[i] = float.MaxValue;
            cameFrom[i] = -1;
        }

        int startNode = startZ * field.Width + startX;
        int endNode = endZ * field.Width + endX;
        bool targetBlocked = field.IsBlockedCell(endX, endZ);

        gScore[startNode] = 0f;
        float startHeuristic = Vector2.Distance(
            field.CellCenter(startX, startZ), end) * RouteMinCost;
        bestF[startNode] = startHeuristic;

        RouteMinHeap open = new RouteMinHeap();
        open.Push(startNode, startHeuristic);

        bool foundTarget = false;
        int bestNode = startNode;
        float bestDistance = Vector2.Distance(
            field.CellCenter(startX, startZ), end);

        int[] neighbourX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        int[] neighbourZ = { 0, 0, 1, -1, 1, -1, 1, -1 };

        while (open.Count > 0)
        {
            int current = open.Pop(out float priority);

            if (closed[current] || priority > bestF[current] + 0.01f)
                continue;

            closed[current] = true;

            int currentX = current % field.Width;
            int currentZ = current / field.Width;
            Vector2 currentCenter = field.CellCenter(currentX, currentZ);
            float distanceToEnd = Vector2.Distance(currentCenter, end);

            if (distanceToEnd < bestDistance - 0.01f)
            {
                bestDistance = distanceToEnd;
                bestNode = current;
            }

            if (!targetBlocked && current == endNode)
            {
                foundTarget = true;
                break;
            }

            for (int n = 0; n < 8; n++)
            {
                int nextX = currentX + neighbourX[n];
                int nextZ = currentZ + neighbourZ[n];

                if (nextX < 0 || nextZ < 0
                    || nextX >= field.Width || nextZ >= field.Height)
                {
                    continue;
                }

                bool isStart = nextX == startX && nextZ == startZ;

                if (field.IsBlockedCell(nextX, nextZ) && !isStart)
                    continue;

                bool diagonal = n >= 4;

                // Never squeeze diagonally between two blocked cells.
                if (diagonal
                    && (field.IsBlockedCell(currentX + neighbourX[n], currentZ)
                        || field.IsBlockedCell(currentX, currentZ + neighbourZ[n])))
                {
                    continue;
                }

                int nextNode = nextZ * field.Width + nextX;

                if (closed[nextNode])
                    continue;

                float step = diagonal
                    ? field.CellSize * 1.41421356f
                    : field.CellSize;
                float edgeCost = (field.CellCost(currentX, currentZ)
                    + field.CellCost(nextX, nextZ)) * 0.5f * step;

                int previous = cameFrom[current];

                if (previous >= 0)
                {
                    int previousX = previous % field.Width;
                    int previousZ = previous / field.Width;
                    float previousXDir = currentX - previousX;
                    float previousZDir = currentZ - previousZ;
                    float nextXDir = neighbourX[n];
                    float nextZDir = neighbourZ[n];
                    float previousLength = Mathf.Sqrt(
                        previousXDir * previousXDir + previousZDir * previousZDir);

                    if (previousLength > 0.001f)
                    {
                        float nextLength = diagonal ? 1.41421356f : 1f;
                        float dot = (previousXDir * nextXDir + previousZDir * nextZDir)
                            / (previousLength * nextLength);
                        float turn = Mathf.Clamp01(1f - dot);
                        float turnWeight = Mathf.Lerp(
                            0.65f, 0.12f, Mathf.Clamp01(curviness));
                        edgeCost += turn * turnWeight * step;
                    }
                }

                float tentative = gScore[current] + edgeCost;

                if (tentative >= gScore[nextNode])
                    continue;

                gScore[nextNode] = tentative;
                cameFrom[nextNode] = current;
                float heuristic = Vector2.Distance(
                    field.CellCenter(nextX, nextZ), end) * RouteMinCost;
                bestF[nextNode] = tentative + heuristic;
                open.Push(nextNode, bestF[nextNode]);
            }
        }

        int lastNode = foundTarget ? endNode : bestNode;

        if (!foundTarget)
        {
            reachedTarget = false;
            warning = "Sin ruta segura; recortada a "
                + Mathf.RoundToInt(bestDistance) + " m del destino.";
        }

        List<Vector2> raw = new List<Vector2>();
        int node = lastNode;

        while (node >= 0)
        {
            int x = node % field.Width;
            int z = node / field.Width;
            raw.Add(field.CellCenter(x, z));

            if (node == startNode)
                break;

            node = cameFrom[node];
        }

        raw.Reverse();

        if (raw.Count == 0)
            raw.Add(start);

        raw[0] = start;

        if (foundTarget)
        {
            raw[raw.Count - 1] = end;
        }
        else if (raw.Count == 1)
        {
            raw.Add(raw[0]);
        }

        List<Vector2> simplified = SimplifyRoute(raw, maxSegments, field);
        int desiredPoints = curviness > 0.01f && maxSegments >= 3
            ? Mathf.Clamp(
                Mathf.RoundToInt(distance / 350f) + 1,
                3, maxSegments + 1)
            : simplified.Count;
        List<Vector2> dense = ResampleRoute(simplified, desiredPoints);
        List<Vector2> curved = ApplyCurviness(
            dense, curviness, detour, field, routeSeed);
        List<Vector2> trimmed = TrimBlockedRoute(curved, field, out bool wasTrimmed);

        if (wasTrimmed)
        {
            reachedTarget = false;
            warning = AppendRouteStatus(
                warning, "Ruta recortada al encontrar una pared.");
        }

        if (trimmed == null || trimmed.Count == 0)
            trimmed = new List<Vector2> { start };

        return trimmed;
    }

    // If the normal corridor is blocked, widen the search before giving up.
    // This keeps the default parameters small while still letting the router
    // walk around a cliff band that is wider than maxDetour.
    private List<Vector2> BuildRouteWithFallback(
        Vector2 start, Vector2 end, int maxSegments,
        float curviness, float maxDetour, float wallClearance,
        int routeSeed, out bool reachedTarget, out string warning)
    {
        List<Vector2> bestRoute = null;
        string bestWarning = null;
        float bestRemaining = float.MaxValue;
        float baseDetour = Mathf.Max(400f, maxDetour);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            float detour = Mathf.Min(12000f, baseDetour * Mathf.Pow(2f, attempt));
            bool reached;
            string attemptWarning;
            List<Vector2> route = BuildCliffAwareRoute(
                start, end, maxSegments, curviness, detour,
                wallClearance, routeSeed + attempt * 977,
                out reached, out attemptWarning);

            if (reached)
            {
                reachedTarget = true;
                warning = null;
                return route;
            }

            float remaining = route != null && route.Count > 0
                ? Vector2.Distance(route[route.Count - 1], end)
                : float.MaxValue;

            if (bestRoute == null || remaining < bestRemaining)
            {
                bestRoute = route;
                bestRemaining = remaining;
                bestWarning = attemptWarning;
            }
        }

        reachedTarget = false;
        warning = bestWarning;
        return bestRoute ?? new List<Vector2> { start };
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
        runtime.routeStatus = "";

        PlanetPOILocation location = poiOverride;

        if (location == null)
        {
            if (string.IsNullOrWhiteSpace(planetName))
                return;

            TextAsset asset = Resources.Load<TextAsset>(OGGetAddress.files + "PlanetPOIs");

            if (asset == null)
                return;

            PlanetPOIs definitions = JsonUtility.FromJson<PlanetPOIs>(asset.text);

            if (definitions == null || definitions.planetPOIsData == null)
                return;

            foreach (PlanetPOILocation candidate in definitions.planetPOIsData)
            {
                if (candidate != null && string.Equals(candidate.planet, planetName,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    location = candidate;
                    break;
                }
            }
        }

        if (location == null)
            return;

        List<Vector2> points = new List<Vector2>
        {
            new Vector2(location.entryX, location.entryZ)
        };
        List<string> names = new List<string> { "entrada" };

        if (location.pois != null)
        {
            foreach (PlanetPOIEntry poi in location.pois)
            {
                if (poi == null)
                    continue;

                points.Add(new Vector2(poi.x, poi.z));
                names.Add(string.IsNullOrEmpty(poi.name)
                    ? "POI " + (points.Count - 1)
                    : poi.name);
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

        int minimum = Mathf.Clamp(
            MaxPathSegments / Mathf.Max(1, legCount), 1, 4);
        int remaining = MaxPathSegments;
        int[] budgets = new int[legCount];

        for (int i = 0; i < legCount; i++)
        {
            int legsAfter = legCount - 1 - i;

            if (i == legCount - 1)
            {
                budgets[i] = Mathf.Max(1, remaining);
                remaining = 0;
                continue;
            }

            int maxForLeg = Mathf.Max(
                minimum, remaining - minimum * legsAfter);
            int budget = Mathf.Clamp(
                Mathf.RoundToInt(MaxPathSegments * distances[i] / totalDistance),
                minimum, maxForLeg);

            budgets[i] = Mathf.Max(1, budget);
            remaining = Mathf.Max(0, remaining - budgets[i]);
        }

        for (int i = 0; i < legCount; i++)
        {
            bool reachedTarget;
            string warning;
            List<Vector2> route = BuildRouteWithFallback(
                points[i], points[i + 1], Mathf.Max(2, budgets[i]),
                runtime.routeCurviness, runtime.routeMaxDetour,
                runtime.routeWallClearance, runtime.routeSeed + i,
                out reachedTarget, out warning);

            if (!reachedTarget)
            {
                string target = i + 1 < names.Count ? names[i + 1] : "destino";
                runtime.routeStatus = AppendRouteStatus(
                    runtime.routeStatus, "Tramo a " + target + ": " + warning);
                Debug.LogWarning("[TerrainStreamer] Tramo a " + target + ": " + warning);
            }

            if (route.Count - 1 > budgets[i])
            {
                runtime.routeStatus = AppendRouteStatus(
                    runtime.routeStatus,
                    "Tramo a " + names[i + 1] + " necesita más segmentos.");
            }

            AddRouteSegments(runtime, route);
        }
    }

    // Free trails use the same cliff-aware router, but their endpoints are a
    // long deterministic line through the planet instead of POI waypoints.
    private void LoadOverlayWanderingSegments(OverlayRuntime runtime)
    {
        runtime.segmentCount = 0;
        runtime.routeStatus = "";

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

        bool reachedTarget;
        string warning;
        List<Vector2> route = BuildRouteWithFallback(
            start, end, MaxPathSegments,
            runtime.routeCurviness, runtime.routeMaxDetour,
            runtime.routeWallClearance, runtime.routeSeed,
            out reachedTarget, out warning);

        if (!reachedTarget)
        {
            runtime.routeStatus = warning ?? "Ruta recortada.";
            Debug.LogWarning("[TerrainStreamer] " + runtime.routeStatus);
        }

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

        // Describe the ground so landing dust (and anything else that hits
        // the tile) knows whether it is sand, rock, snow...
        TerrainSurface surface = tileObject.AddComponent<TerrainSurface>();
        Texture2D groundTexture = finalIndex < terrainTextures.Length
            ? terrainTextures[finalIndex] : null;
        surface.ApplyTexture(groundTexture != null ? groundTexture.name : null, landingDust);

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