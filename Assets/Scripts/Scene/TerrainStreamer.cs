using System.Collections.Generic;
using UnityEngine;

public class TerrainStreamer : MonoBehaviour
{
    [Header("Generation")]
    [SerializeField] public int seed = 1138;

    [Header("Tile Settings")]
    [SerializeField] private float tileSize = 1000f;
    [SerializeField] private int verticesPerSide = 32;
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
    [SerializeField] private Texture2D cliffTexture;
    [SerializeField] public string terrainTextureType = "forest-mixed";
    [SerializeField] public string cliffTextureType = "cliff01";


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
        cliffTexture = LoadCliffTexture("objects/terrain/cliffs/" + cliffTextureType);

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

        tileRadiusInTiles = Mathf.CeilToInt(
            tileRadiusInKilometres * 1000f / tileSize
        );

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
        if (cliffTexture != null)
        {
            sharedOuterMaterial.SetTexture("_CliffMap", cliffTexture);
        }

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
            || lower.EndsWith("_normal") || lower.EndsWith("_occlusion");
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

    private Texture2D LoadCliffTexture(string texturePath)
    {
        Texture2D texture = Resources.Load<Texture2D>(texturePath);

        if (texture == null)
        {
            Debug.LogError($"TerrainTileStreamer: Cliff texture not found at Resources/{texturePath}");
            return null;
        }

        return texture;
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

        if (cliffTexture != null)
        {
            materialInstance.SetTexture("_CliffMap", cliffTexture);
        }

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