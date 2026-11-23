using UnityEngine;

/// <summary>
/// Generates a single terrain tile mesh using FastNoiseLite with varied, natural terrain.
/// Uses a biome blending system similar to the proven terrain manager.
/// </summary>
public class TerrainGenerator
{
    private readonly int seed;
    private readonly float tileSize;
    private readonly int verticesPerSide;
    private readonly float maxHeight;
    private readonly AnimationCurve heightCurve;
    private readonly float tileNoiseScale;

    private readonly FastNoiseLite mountainNoise;
    private readonly FastNoiseLite hillNoise;
    private readonly FastNoiseLite desertNoise;
    private readonly FastNoiseLite plainsNoise;
    private readonly FastNoiseLite biomeNoise;
    private readonly FastNoiseLite cliffMaskNoise;
    private readonly FastNoiseLite cliffCanyonNoise;

    // Amplitude multipliers for each biome - these need to be MUCH larger
    public float plainsAmp = 50f;
    public float desertAmp = 100f;
    public float hillsAmp = 200f;
    public float mountAmp = 400f;

    // Cliff / mesa layer. Disabled by default so existing planets keep the
    // exact same terrain. Everything is world-space, so tiles stay seamless.
    public bool cliffsEnabled = false;
    public float cliffMesaHeight = 60f;
    public float cliffMesaRoughness = 25f;
    public float cliffEdgeWidth = 0.02f;
    public float cliffCanyonDepth = 55f;
    public float cliffCanyonWidth = 0.06f;
    public float cliffStrength = 1f;
    // 0..1: how much of the planet gets mesas/canyons. 1 = everywhere.
    public float cliffCoverage = 1f;
    public float cliffMaskThreshold = 0.45f;
    public float cliffMaskFrequency = 0.00045f;

    private float appliedMaskFrequency = -1f;

    // Biome percentages
    private float plainsPercentage = 0.25f;
    private float desertPercentage = 0.25f;
    private float hillsPercentage = 0.25f;

    public TerrainGenerator(
        int seed,
        float tileSize,
        int verticesPerSide,
        float maxHeight,
        AnimationCurve heightCurve,
        float tileNoiseScale = 0.2f
    )
    {
        this.seed = seed;
        this.tileSize = tileSize;
        this.verticesPerSide = verticesPerSide;
        this.maxHeight = maxHeight;
        this.heightCurve = heightCurve ?? AnimationCurve.Linear(0, 0, 1, 1);
        this.tileNoiseScale = tileNoiseScale;

        mountainNoise = new FastNoiseLite(seed);
        hillNoise = new FastNoiseLite(seed + 1);
        desertNoise = new FastNoiseLite(seed + 2);
        plainsNoise = new FastNoiseLite(seed + 3);
        biomeNoise = new FastNoiseLite(seed + 4);
        cliffMaskNoise = new FastNoiseLite(seed + 5);
        cliffCanyonNoise = new FastNoiseLite(seed + 6);

        InitializeNoiseGenerators();
    }

    private void InitializeNoiseGenerators()
    {
        // Mountain peaks: sharp, craggy features
        mountainNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        mountainNoise.SetFrequency(0.002f);
        mountainNoise.SetFractalType(FastNoiseLite.FractalType.Ridged);
        mountainNoise.SetFractalOctaves(5);
        mountainNoise.SetFractalLacunarity(2.2f);
        mountainNoise.SetFractalGain(0.6f);

        // Rolling hills: medium scale undulation
        hillNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        hillNoise.SetFrequency(0.005f);
        hillNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        hillNoise.SetFractalOctaves(3);
        hillNoise.SetFractalLacunarity(2.0f);
        hillNoise.SetFractalGain(0.3f);

        // Desert: cellular noise for rocky appearance
        desertNoise.SetNoiseType(FastNoiseLite.NoiseType.Cellular);
        desertNoise.SetFrequency(0.01f);

        // Plains: large rolling terrain
        plainsNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        plainsNoise.SetFrequency(0.0005f);
        plainsNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        plainsNoise.SetFractalOctaves(3);
        plainsNoise.SetFractalLacunarity(2.0f);
        plainsNoise.SetFractalGain(0.3f);

        // Biome selector: determines which terrain type appears where
        biomeNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        biomeNoise.SetFrequency(0.0005f);
        biomeNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        biomeNoise.SetFractalOctaves(2);
        biomeNoise.SetFractalLacunarity(2.0f);
        biomeNoise.SetFractalGain(0.5f);

        // Cliff mask: low-frequency patches that decide where mesas appear.
        // Frequency is configurable per planet; applied on first sample.
        cliffMaskNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        cliffMaskNoise.SetFrequency(cliffMaskFrequency);
        cliffMaskNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        cliffMaskNoise.SetFractalOctaves(3);
        cliffMaskNoise.SetFractalLacunarity(2.0f);
        cliffMaskNoise.SetFractalGain(0.5f);

        // Canyon carve: ridged noise digs a network of gorges between mesas.
        cliffCanyonNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        cliffCanyonNoise.SetFrequency(0.0012f);
        cliffCanyonNoise.SetFractalType(FastNoiseLite.FractalType.Ridged);
        cliffCanyonNoise.SetFractalOctaves(4);
        cliffCanyonNoise.SetFractalLacunarity(2.1f);
        cliffCanyonNoise.SetFractalGain(0.55f);
    }

    // Sharp plateau mask: raising the ground where this is 1 carves a
    // near-vertical mesa wall regardless of the base terrain slope.
    private float SampleCliffPlateau(float worldX, float worldZ)
    {
        float frequency = Mathf.Max(0.00001f, cliffMaskFrequency);

        if (Mathf.Approximately(frequency, appliedMaskFrequency) == false)
        {
            cliffMaskNoise.SetFrequency(frequency);
            appliedMaskFrequency = frequency;
        }

        // Coverage 0 pushes the threshold above the noise range (no mesas),
        // coverage 1 uses the configured threshold everywhere on the planet.
        float coverage = Mathf.Clamp01(cliffCoverage);
        float threshold = Mathf.Lerp(1f, Mathf.Clamp(cliffMaskThreshold, 0f, 0.98f), coverage);
        float field = (cliffMaskNoise.GetNoise(worldX, worldZ) + 1f) * 0.5f;
        float edge = Mathf.Clamp(cliffEdgeWidth, 0.002f, 0.25f);

        return Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(threshold, threshold + edge, field));
    }

    // Raises mesas and cuts canyon gorges. Works on top of the base biome
    // height; coverage/strength come from the per-planet JSON settings.
    private float ApplyCliffs(float height, float worldX, float worldZ)
    {
        if (cliffsEnabled == false)
            return height;

        float coverage = Mathf.Clamp01(cliffCoverage);
        float strength = Mathf.Clamp01(cliffStrength);

        if (coverage <= 0.0001f || strength <= 0.0001f)
            return height;

        float plateau = SampleCliffPlateau(worldX, worldZ);

        // Rough top so mesas are not perfectly flat cardboard.
        float topNoise = (hillNoise.GetNoise(worldX * tileNoiseScale, worldZ * tileNoiseScale) + 1f) * 0.5f;
        float mesaTop = Mathf.Max(0f, cliffMesaHeight) + topNoise * Mathf.Max(0f, cliffMesaRoughness);

        // Ridged lines become canyon gorges cut through the mesas.
        float ridge = cliffCanyonNoise.GetNoise(worldX, worldZ);
        float canyonWidth = Mathf.Clamp(cliffCanyonWidth, 0.002f, 0.5f);
        float canyon = Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(0.35f, 0.35f + canyonWidth, ridge)) * coverage;

        float carved = height
            + plateau * mesaTop
            - canyon * Mathf.Max(0f, cliffCanyonDepth);

        return Mathf.Lerp(height, carved, strength);
    }

    private float SampleTerrainHeight(float worldX, float worldZ)
    {
        // Get biome control value (0 to 1)
        float biomeControl = biomeNoise.GetNoise(worldX * tileNoiseScale * 0.2f, worldZ * tileNoiseScale * 0.2f);
        biomeControl = (biomeControl + 1f) / 2f;

        // Sample each biome's height - noise is [-1, 1], so (noise + 1f) gives [0, 2], multiply by amplitude
        float hPlains = (plainsNoise.GetNoise(worldX * tileNoiseScale, worldZ * tileNoiseScale) + 1f) * plainsAmp;
        float hDesert = (desertNoise.GetNoise(worldX * tileNoiseScale, worldZ * tileNoiseScale) + 1f) * desertAmp;
        float hHills = (hillNoise.GetNoise(worldX * tileNoiseScale, worldZ * tileNoiseScale) + 1f) * hillsAmp;
        float hMount = (mountainNoise.GetNoise(worldX * tileNoiseScale, worldZ * tileNoiseScale) + 1f) * mountAmp;

        // Calculate biome boundaries
        float valueA = plainsPercentage;
        float valueB = plainsPercentage + desertPercentage + hillsPercentage;

        if (valueA > 0.5f) valueA = 0.49f;
        if (valueB > 1f) valueB = 0.98f;

        float height = 0f;

        // Blend between biomes
        if (biomeControl < valueA)
        {
            float blendFactor = Mathf.InverseLerp(0.0f, valueA, biomeControl);
            height = Mathf.Lerp(hPlains, hDesert, blendFactor);
        }
        else if (biomeControl < valueB)
        {
            float blendFactor = Mathf.InverseLerp(valueA, valueB, biomeControl);
            height = Mathf.Lerp(hDesert, hHills, blendFactor);
        }
        else
        {
            float blendFactor = Mathf.InverseLerp(valueB, 1.0f, biomeControl);
            height = Mathf.Lerp(hHills, hMount, blendFactor);
        }

        return ApplyCliffs(height, worldX, worldZ);
    }

    public Mesh GenerateMesh(Vector2Int tileCoordinate)
    {
        return GenerateMesh(tileCoordinate, verticesPerSide);
    }

    public Mesh GenerateMesh(Vector2Int tileCoordinate, int vertsPerSide)
    {
        vertsPerSide = Mathf.Max(2, vertsPerSide);
        Mesh mesh = new Mesh();
        mesh.name = $"Terrain_Tile_{tileCoordinate.x}_{tileCoordinate.y}";

        Vector3[] vertices = new Vector3[vertsPerSide * vertsPerSide];
        Vector2[] uvs = new Vector2[vertsPerSide * vertsPerSide];
        int[] triangles = new int[(vertsPerSide - 1) * (vertsPerSide - 1) * 6];

        float step = tileSize / (vertsPerSide - 1);
        float tileWorldX = tileCoordinate.x * tileSize;
        float tileWorldZ = tileCoordinate.y * tileSize;

        int vertexIndex = 0;

        for (int z = 0; z < vertsPerSide; z++)
        {
            for (int x = 0; x < vertsPerSide; x++)
            {
                float worldX = tileWorldX + x * step;
                float worldZ = tileWorldZ + z * step;

                float height = SampleTerrainHeight(worldX, worldZ);

                vertices[vertexIndex] = new Vector3(
                    x * step,
                    height,
                    z * step
                );

                uvs[vertexIndex] = new Vector2(
                    x / (float)(vertsPerSide - 1),
                    z / (float)(vertsPerSide - 1)
                );

                vertexIndex++;
            }
        }

        int triangleIndex = 0;

        for (int z = 0; z < vertsPerSide - 1; z++)
        {
            for (int x = 0; x < vertsPerSide - 1; x++)
            {
                int topLeft = z * vertsPerSide + x;
                int topRight = topLeft + 1;
                int bottomLeft = topLeft + vertsPerSide;
                int bottomRight = bottomLeft + 1;

                triangles[triangleIndex++] = topLeft;
                triangles[triangleIndex++] = bottomLeft;
                triangles[triangleIndex++] = topRight;

                triangles[triangleIndex++] = topRight;
                triangles[triangleIndex++] = bottomLeft;
                triangles[triangleIndex++] = bottomRight;
            }
        }

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();

        return mesh;
    }

    public float GetHeightAtLocalPosition(Vector2Int tileCoordinate, Vector3 localPosition)
    {
        float worldX = tileCoordinate.x * tileSize + localPosition.x;
        float worldZ = tileCoordinate.y * tileSize + localPosition.z;

        return SampleTerrainHeight(worldX, worldZ);
    }
}