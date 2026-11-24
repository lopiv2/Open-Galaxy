using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Per-tile vegetation drawn with GPU instancing.
//
// One component is attached to each close terrain tile, so bushes/trees stream
// in and out with the terrain instead of living in a global forest that would
// float when tiles unload. Placements are deterministic per tile coordinate
// (same layout every time the tile is rebuilt) and heights come straight from
// TerrainGenerator, so no physics raycasts or collider baking are required.
// ExecuteAlways + OnRenderObject make the same component visible both in play
// mode and in the editor Terrain Preview window.
[ExecuteAlways]
public class TileVegetation : MonoBehaviour
{
    private const int MaxInstancesPerBatch = 1023;

    private Mesh mesh;
    private Material material;
    private Matrix4x4[] localMatrices;
    private Matrix4x4[][] worldBatches;
    private int[] batchCounts;
    private Bounds localBounds;
    private Transform viewer;
    private float maxDistanceSqr;
    private Vector3 lastWorldPosition;
    private bool initialized;

    public static void Attach(GameObject tile, Vector2Int coordinate, float tileSize,
        TerrainGenerator generator, VegetationPrototype prototype, Transform viewer)
    {
        if (tile == null || generator == null || prototype == null || prototype.mesh == null)
        {
            return;
        }

        TileVegetation vegetation = tile.AddComponent<TileVegetation>();
        vegetation.hideFlags = tile.hideFlags;
        vegetation.Initialize(coordinate, tileSize, generator, prototype, viewer);
    }

    private void Initialize(Vector2Int coordinate, float tileSize, TerrainGenerator generator,
        VegetationPrototype prototype, Transform viewer)
    {
        mesh = prototype.mesh;
        material = prototype.material;
        this.viewer = viewer;

        VegetationSettings settings = prototype.settings;
        float distance = Mathf.Max(1f, settings.maxDistance);
        maxDistanceSqr = distance * distance;

        int requested = Mathf.Max(0, settings.countPerTile);
        int maxAttempts = Mathf.Clamp(settings.placementAttempts, 1, 16);
        float maxSlope = settings.maxSlope;

        System.Random random = new System.Random(
            settings.seedOffset
            ^ coordinate.x * 73856093
            ^ coordinate.y * 19349663);

        List<Matrix4x4> placements = new List<Matrix4x4>(requested);

        for (int i = 0; i < requested; i++)
        {
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                float x = (float)random.NextDouble() * tileSize;
                float z = (float)random.NextDouble() * tileSize;
                Vector3 sample = new Vector3(x, 0f, z);
                float height = generator.GetHeightAtLocalPosition(coordinate, sample);

                if (maxSlope > 0f && maxSlope < 90f)
                {
                    const float step = 2f;
                    float left = generator.GetHeightAtLocalPosition(
                        coordinate, sample + new Vector3(-step, 0f, 0f));
                    float right = generator.GetHeightAtLocalPosition(
                        coordinate, sample + new Vector3(step, 0f, 0f));
                    float back = generator.GetHeightAtLocalPosition(
                        coordinate, sample + new Vector3(0f, 0f, -step));
                    float front = generator.GetHeightAtLocalPosition(
                        coordinate, sample + new Vector3(0f, 0f, step));

                    Vector3 normal = new Vector3(left - right, step * 2f, back - front).normalized;
                    if (Vector3.Angle(normal, Vector3.up) > maxSlope)
                    {
                        continue;
                    }
                }

                float scale = Mathf.Lerp(settings.minScale, settings.maxScale, (float)random.NextDouble());
                float yaw = (float)random.NextDouble() * 360f;

                placements.Add(Matrix4x4.TRS(
                    new Vector3(x, height, z),
                    Quaternion.Euler(0f, yaw, 0f),
                    Vector3.one * scale));
                break;
            }
        }

        localMatrices = placements.ToArray();

        int batchCount = Mathf.Max(1,
            Mathf.CeilToInt(localMatrices.Length / (float)MaxInstancesPerBatch));
        worldBatches = new Matrix4x4[batchCount][];
        batchCounts = new int[batchCount];

        for (int batch = 0; batch < batchCount; batch++)
        {
            int offset = batch * MaxInstancesPerBatch;
            int count = Mathf.Clamp(localMatrices.Length - offset, 0, MaxInstancesPerBatch);
            batchCounts[batch] = count;
            worldBatches[batch] = new Matrix4x4[count];
        }

        localBounds = new Bounds(
            new Vector3(tileSize * 0.5f, 0f, tileSize * 0.5f),
            new Vector3(tileSize, 2000f, tileSize));

        RebuildWorldMatrices();
        initialized = true;
    }

    private void RebuildWorldMatrices()
    {
        Matrix4x4 tileToWorld = transform.localToWorldMatrix;
        int index = 0;

        for (int batch = 0; batch < worldBatches.Length; batch++)
        {
            Matrix4x4[] matrices = worldBatches[batch];
            for (int i = 0; i < matrices.Length; i++)
            {
                matrices[i] = tileToWorld * localMatrices[index++];
            }
        }

        lastWorldPosition = transform.position;
    }

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
    }

    // Play mode: one draw per frame.
    private void Update()
    {
        if (Application.isPlaying)
        {
            Draw(null);
        }
    }

    // Edit mode: draw for the camera that is about to render. Update is
    // throttled in the editor, so relying on it left the terrain preview empty
    // until something forced a player-loop tick.
    private void HandleBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (!Application.isPlaying)
        {
            Draw(camera);
        }
    }

    private void Draw(Camera camera)
    {
        if (!initialized || localMatrices == null || localMatrices.Length == 0
            || worldBatches == null || batchCounts == null
            || mesh == null || material == null)
        {
            return;
        }

        if (transform.position != lastWorldPosition)
        {
            RebuildWorldMatrices();
        }

        Transform target = viewer != null
            ? viewer
            : (Camera.main != null ? Camera.main.transform : null);

        // No viewer (e.g. a standalone editor preview) means no distance culling.
        if (target != null)
        {
            Bounds worldBounds = new Bounds(
                transform.TransformPoint(localBounds.center),
                Vector3.Scale(localBounds.size, transform.lossyScale));

            if (worldBounds.SqrDistance(target.position) > maxDistanceSqr)
            {
                return;
            }
        }

        for (int batch = 0; batch < worldBatches.Length; batch++)
        {
            if (batchCounts[batch] == 0)
            {
                continue;
            }

            Graphics.DrawMeshInstanced(
                mesh,
                0,
                material,
                worldBatches[batch],
                batchCounts[batch],
                null,
                ShadowCastingMode.Off,
                false,
                gameObject.layer,
                camera,
                LightProbeUsage.Off);
        }
    }

    // Play mode: keep matrices in sync when the floating origin moves a tile.
    private void LateUpdate()
    {
        if (initialized && worldBatches != null
            && transform.position != lastWorldPosition)
        {
            RebuildWorldMatrices();
        }
    }
}

// Resolved runtime data for one VegetationSettings entry. Built once per
// planet by TerrainStreamer so every tile shares the same mesh and material.
public class VegetationPrototype
{
    public Mesh mesh;
    public Material material;
    public VegetationSettings settings;
}
