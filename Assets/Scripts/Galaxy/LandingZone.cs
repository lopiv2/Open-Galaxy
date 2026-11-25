using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Blue halo ring drawn on the ground around a landed ship. It marks the
// boarding area: on foot the player can only get back in the ship when
// standing inside this circle. The mesh follows the terrain height under
// every vertex, so the ring hugs dunes/rocks instead of clipping through.
public class LandingZone : MonoBehaviour
{
    [Header("Zone")]
    [Tooltip("Boarding radius in metres (distance from the ship).")]
    public float radius = 14f;
    public Color color = new Color(0.24f, 0.62f, 1f, 1f);
    [Tooltip("How far above the ground the ring is drawn.")]
    public float groundLift = 0.08f;

    [Header("Halo")]
    [Tooltip("Breathe speed of the glow.")]
    public float pulseSpeed = 1.8f;
    [Range(0f, 1f)] public float pulseAmount = 0.22f;

    private const int Segments = 72;
    private const int ProfileSamples = 128;

    // Radial samples from the centre to the outer halo. Alpha defines the
    // look: near-invisible disc inside, bright rim, soft glow outside.
    private static readonly float[] ProfileRadius = { 0f, 0.35f, 0.65f, 0.85f, 0.92f, 0.97f, 1f, 1.03f, 1.08f, 1.18f, 1.4f, 1.75f };
    private static readonly float[] ProfileAlpha = { 0.03f, 0.035f, 0.04f, 0.06f, 0.14f, 0.55f, 1f, 0.55f, 0.22f, 0.10f, 0.04f, 0f };

    private GameObject zoneObject;
    private Mesh mesh;
    private Material material;
    private Texture2D profileTexture;
    private Vector3 lastCenter;
    private float lastGroundY;
    private bool built;
    private bool visible;

    public bool Contains(Vector3 worldPosition)
    {
        Vector3 delta = worldPosition - transform.position;
        delta.y = 0f;
        return delta.sqrMagnitude <= radius * radius;
    }

    public void Show(Vector3 shipWorldPosition, float groundWorldY)
    {
        EnsureBuilt();

        // Rebuild only when the zone actually moves (landing spots differ),
        // never every frame while standing still.
        if (built == false
            || Mathf.Abs(shipWorldPosition.x - lastCenter.x) > 0.75f
            || Mathf.Abs(shipWorldPosition.z - lastCenter.z) > 0.75f
            || Mathf.Abs(groundWorldY - lastGroundY) > 0.5f)
        {
            Rebuild(shipWorldPosition, groundWorldY);
        }

        if (visible == false)
        {
            visible = true;
            zoneObject.SetActive(true);
        }
    }

    public void Hide()
    {
        if (zoneObject != null && visible == true)
        {
            visible = false;
            zoneObject.SetActive(false);
        }
    }

    private void Update()
    {
        if (visible == false || material == null)
        {
            return;
        }

        // Unscaled time keeps the glow alive even if gameplay is paused while
        // the ship is landed / foot mode is active.
        float pulse = 1f - pulseAmount * 0.5f
            + pulseAmount * 0.5f * Mathf.Sin(Time.unscaledTime * pulseSpeed);

        material.color = new Color(color.r, color.g, color.b, color.a * pulse);
    }

    private void EnsureBuilt()
    {
        if (zoneObject != null)
        {
            return;
        }

        zoneObject = new GameObject("LandingZone");
        zoneObject.transform.SetParent(transform, false);

        MeshFilter filter = zoneObject.AddComponent<MeshFilter>();
        mesh = new Mesh();
        mesh.name = "LandingZoneRing";
        filter.sharedMesh = mesh;

        MeshRenderer meshRenderer = zoneObject.AddComponent<MeshRenderer>();
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        // Radial alpha profile baked into a 1-pixel-tall texture. The ring
        // mesh maps it across UV.x, so no vertex-colour shader is needed and
        // the fragment uses the same URP Unlit shader as the landing marker.
        profileTexture = BuildProfileTexture();

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
        {
            shader = Shader.Find("Unlit/Transparent");
        }

        material = new Material(shader);
        material.name = "LandingZone_Runtime";

        if (profileTexture != null)
        {
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", profileTexture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", profileTexture);
        }

        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);
        material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = 3000;

        meshRenderer.sharedMaterial = material;
        zoneObject.SetActive(false);
    }

    // White RGB with the ring alpha profile so the material tint can pulse
    // the whole glow without touching the mesh.
    private static Texture2D BuildProfileTexture()
    {
        Texture2D texture = new Texture2D(ProfileSamples, 1, TextureFormat.RGBA32, false);
        texture.name = "LandingZoneProfile";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[ProfileSamples];
        int lastProfile = ProfileAlpha.Length - 1;

        for (int x = 0; x < ProfileSamples; x++)
        {
            float t = x / (float)(ProfileSamples - 1) * lastProfile;
            int index = Mathf.FloorToInt(t);
            int next = Mathf.Min(index + 1, lastProfile);
            float alpha = Mathf.Lerp(ProfileAlpha[index], ProfileAlpha[next], t - index);
            pixels[x] = new Color(1f, 1f, 1f, alpha);
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private void Rebuild(Vector3 center, float groundWorldY)
    {
        built = true;
        lastCenter = center;
        lastGroundY = groundWorldY;

        // World position of the zone; vertices are local to it.
        transform.position = new Vector3(center.x, groundWorldY, center.z);
        transform.rotation = Quaternion.identity;

        int rings = ProfileRadius.Length;
        int vertexCount = rings * Segments;

        Vector3[] vertices = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];
        List<int> triangles = new List<int>((rings - 1) * Segments * 6);
        float uvStep = 1f / (rings - 1);

        for (int ring = 0; ring < rings; ring++)
        {
            float ringRadius = radius * ProfileRadius[ring];
            float uvX = ring * uvStep;

            for (int segment = 0; segment < Segments; segment++)
            {
                float angle = segment / (float)Segments * Mathf.PI * 2f;
                float x = Mathf.Cos(angle) * ringRadius;
                float z = Mathf.Sin(angle) * ringRadius;

                Vector3 worldPoint = new Vector3(center.x + x, center.y, center.z + z);
                float ground = SampleGroundHeight(worldPoint);

                if (float.IsNaN(ground) == true)
                {
                    ground = groundWorldY;
                }

                int index = ring * Segments + segment;
                vertices[index] = new Vector3(x, ground - groundWorldY + groundLift, z);
                uvs[index] = new Vector2(uvX, 0.5f);
            }
        }

        for (int ring = 0; ring < rings - 1; ring++)
        {
            for (int segment = 0; segment < Segments; segment++)
            {
                int next = (segment + 1) % Segments;
                int a = ring * Segments + segment;
                int b = ring * Segments + next;
                int c = (ring + 1) * Segments + segment;
                int d = (ring + 1) * Segments + next;

                triangles.Add(a); triangles.Add(c); triangles.Add(b);
                triangles.Add(b); triangles.Add(c); triangles.Add(d);
            }
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateBounds();
    }

    private static float SampleGroundHeight(Vector3 worldPosition)
    {
        Vector3 origin = new Vector3(worldPosition.x, worldPosition.y + 400f, worldPosition.z);
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, origin.y + 2000f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;

        foreach (RaycastHit hit in hits)
        {
            if (hit.transform == null || hit.transform.name.StartsWith("Tile_") == false)
            {
                continue;
            }

            if (hit.point.y > best)
            {
                best = hit.point.y;
            }
        }

        return float.IsNegativeInfinity(best) ? float.NaN : best;
    }

    private void OnDestroy()
    {
        if (material != null)
        {
            Destroy(material);
            material = null;
        }

        if (profileTexture != null)
        {
            Destroy(profileTexture);
            profileTexture = null;
        }

        if (mesh != null)
        {
            Destroy(mesh);
            mesh = null;
        }
    }
}
