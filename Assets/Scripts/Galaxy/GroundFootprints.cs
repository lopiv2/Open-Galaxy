using System.Collections.Generic;
using UnityEngine;

// Spawns fading footprint splats on terrain while the ground character walks.
// Uses the right-foot texture; left steps are mirrored horizontally.
public class GroundFootprints : MonoBehaviour
{
    [Header("Footprint")]
    public string textureResourcePath = "fx/FootPrint_Boot_d";
    public string normalTextureResourcePath = "fx/FootPrint_Boot_n";
    public float stepDistance = 1.8f;
    public float footSpacing = 0.12f;
    public float forwardOffset = 0.05f;
    public float size = 0.28f;
    public float groundLift = 0.015f;
    public float alpha = 0.55f;
    public float normalStrength = 1.0f;
    public float smoothness = 0.05f;

    [Header("Fade")]
    public float lifetimeSeconds = 20f;
    public float fadeOutSeconds = 4f;
    public int maxFootprints = 64;

    [Header("Footstep audio")]
    [Tooltip("If false, footprints are silent (visual only).")]
    public bool footstepAudioEnabled = true;
    public float footstepVolumeScale = 1f;
    [Tooltip("Extra volume when running (1 = same as walking).")]
    public float runVolumeBoost = 1.25f;
    [Tooltip("Play a sound every N spawned footprints. 1 = one sound per footprint.")]
    public int footstepEveryN = 1;
    [Tooltip("Minimum seconds between footstep sounds (only stops burst stacking, e.g. after a frame hitch).")]
    public float footstepMinIntervalWalk = 0.15f;
    public float footstepMinIntervalRun = 0.12f;

    private int footprintsSinceLastSound;
    private float lastSoundTime = -10f;

    private AudioSource footstepSource;
    private TerrainStreamer streamer;
    private readonly Dictionary<string, AudioClip> clipCache = new Dictionary<string, AudioClip>(System.StringComparer.OrdinalIgnoreCase);

    private readonly List<Footprint> footprints = new List<Footprint>();
    private Material material;
    private Mesh quadMesh;
    private Vector3 lastSpawnPosition;
    private bool hasLastSpawnPosition;
    private bool nextIsLeft;

    private class Footprint
    {
        public GameObject gameObject;
        public MeshRenderer renderer;
        public float bornTime;
    }

    private void Awake()
    {
        EnsureResources();
        lastSpawnPosition = transform.position;
        hasLastSpawnPosition = true;
    }

    private void OnDestroy()
    {
        for (int i = 0; i < footprints.Count; i++)
        {
            if (footprints[i] != null && footprints[i].gameObject != null)
            {
                Destroy(footprints[i].gameObject);
            }
        }

        footprints.Clear();

        if (material != null)
        {
            Destroy(material);
            material = null;
        }

        if (quadMesh != null)
        {
            Destroy(quadMesh);
            quadMesh = null;
        }
    }

    private void Update()
    {
        FadeFootprints();
    }

    public void NotifyMove(Vector3 worldPosition, Vector3 forward, bool isMoving, bool isRunning = false)
    {
        EnsureResources();

        if (isMoving == false)
        {
            lastSpawnPosition = worldPosition;
            hasLastSpawnPosition = true;
            return;
        }

        if (hasLastSpawnPosition == false)
        {
            lastSpawnPosition = worldPosition;
            hasLastSpawnPosition = true;
            return;
        }

        Vector3 delta = worldPosition - lastSpawnPosition;
        delta.y = 0f;

        if (delta.magnitude < stepDistance)
        {
            return;
        }

        Vector3 flatForward = forward;
        flatForward.y = 0f;

        if (flatForward.sqrMagnitude < 0.0001f)
        {
            flatForward = transform.forward;
            flatForward.y = 0f;
        }

        if (flatForward.sqrMagnitude < 0.0001f)
        {
            flatForward = Vector3.forward;
        }

        flatForward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, flatForward).normalized;

        // Catch up if the character moved a long distance in one frame.
        int steps = Mathf.FloorToInt(delta.magnitude / stepDistance);
        steps = Mathf.Clamp(steps, 1, 4);

        Vector3 dir = delta.normalized;
        for (int i = 1; i <= steps; i++)
        {
            Vector3 stepPos = lastSpawnPosition + dir * (stepDistance * i);
            SpawnFootprint(stepPos, flatForward, right, nextIsLeft, isRunning);
            nextIsLeft = !nextIsLeft;
        }

        lastSpawnPosition = lastSpawnPosition + dir * (stepDistance * steps);
    }

    public void ResetTracking(Vector3 worldPosition)
    {
        lastSpawnPosition = worldPosition;
        hasLastSpawnPosition = true;
        nextIsLeft = false;
        footprintsSinceLastSound = 0;
    }

    private void SpawnFootprint(Vector3 worldPosition, Vector3 forward, Vector3 right, bool isLeft, bool isRunning = false)
    {
        float ground = SampleGroundHeight(worldPosition);
        if (float.IsNaN(ground))
        {
            return;
        }

        float side = isLeft ? -footSpacing : footSpacing;
        Vector3 pos = worldPosition + right * side + forward * forwardOffset;
        pos.y = ground + groundLift;

        // Don't place prints on steep slopes/walls.
        Vector3 normal = SampleGroundNormal(pos);
        if (normal != Vector3.zero && Vector3.Angle(normal, Vector3.up) > 35f)
        {
            return;
        }

        GameObject go = new GameObject(isLeft ? "Footprint_L" : "Footprint_R");
        go.transform.position = pos;
        go.transform.rotation = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);

        float mirror = isLeft ? -1f : 1f;
        go.transform.localScale = new Vector3(size * mirror, size, size);

        MeshFilter filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = quadMesh;

        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        footprints.Add(new Footprint
        {
            gameObject = go,
            renderer = renderer,
            bornTime = Time.time
        });

        TrimOldFootprints();
        PlayFootstep(pos, isRunning);
    }

    private void FadeFootprints()
    {
        if (footprints.Count == 0)
        {
            return;
        }

        float now = Time.time;
        for (int i = footprints.Count - 1; i >= 0; i--)
        {
            Footprint fp = footprints[i];
            if (fp == null || fp.gameObject == null || fp.renderer == null)
            {
                footprints.RemoveAt(i);
                continue;
            }

            float age = now - fp.bornTime;
            if (age >= lifetimeSeconds)
            {
                Destroy(fp.gameObject);
                footprints.RemoveAt(i);
                continue;
            }

            float fade = 1f;
            if (fadeOutSeconds > 0f && age > lifetimeSeconds - fadeOutSeconds)
            {
                fade = Mathf.Clamp01((lifetimeSeconds - age) / fadeOutSeconds);
            }

            Color c = fp.renderer.material.color;
            c.a = alpha * fade;
            fp.renderer.material.color = c;
        }
    }

    private void TrimOldFootprints()
    {
        while (footprints.Count > maxFootprints)
        {
            Footprint old = footprints[0];
            footprints.RemoveAt(0);
            if (old != null && old.gameObject != null)
            {
                Destroy(old.gameObject);
            }
        }
    }

    private void PlayFootstep(Vector3 worldPosition, bool isRunning)
    {
        if (footstepAudioEnabled == false)
        {
            return;
        }

        // Audible cadence is slower than the visual stride: sound every N
        // footprints plus a minimum interval (also stops catch-up bursts
        // from stacking several sounds in one frame).
        footprintsSinceLastSound++;
        if (footprintsSinceLastSound < Mathf.Max(1, footstepEveryN))
        {
            return;
        }

        float minInterval = isRunning ? footstepMinIntervalRun : footstepMinIntervalWalk;
        if (Time.time - lastSoundTime < minInterval)
        {
            return;
        }

        TerrainStreamer active = GetStreamer();
        if (active == null)
        {
            return;
        }

        if (active.TryGetFootstep(worldPosition, out string clipName, out float volume, out float pitchMin, out float pitchMax) == false)
        {
            return;
        }

        AudioClip clip = GetFootstepClip(clipName);
        if (clip == null)
        {
            return;
        }

        if (footstepSource == null)
        {
            footstepSource = gameObject.AddComponent<AudioSource>();
            footstepSource.spatialBlend = 0.35f;
            footstepSource.rolloffMode = AudioRolloffMode.Linear;
            footstepSource.maxDistance = 60f;
            footstepSource.dopplerLevel = 0f;
            footstepSource.playOnAwake = false;
        }

        float lo = Mathf.Min(pitchMin, pitchMax);
        float hi = Mathf.Max(pitchMin, pitchMax);
        if (hi <= 0f)
        {
            lo = 0.92f;
            hi = 1.08f;
        }

        footstepSource.pitch = Random.Range(lo, hi);
        float runBoost = isRunning ? Mathf.Max(0.1f, runVolumeBoost) : 1f;
        footstepSource.volume = Mathf.Clamp01(volume * footstepVolumeScale * runBoost * Random.Range(0.9f, 1.1f));
        footstepSource.PlayOneShot(clip);
        footprintsSinceLastSound = 0;
        lastSoundTime = Time.time;
    }

    private TerrainStreamer GetStreamer()
    {
        if (streamer != null)
        {
            return streamer;
        }

        streamer = FindObjectOfType<TerrainStreamer>();
        return streamer;
    }

    private AudioClip GetFootstepClip(string clipName)
    {
        if (string.IsNullOrEmpty(clipName))
        {
            return null;
        }

        if (clipCache.TryGetValue(clipName, out AudioClip cached))
        {
            return cached;
        }

        AudioClip clip = Resources.Load<AudioClip>("audio/audioclips/" + clipName);
        clipCache[clipName] = clip;

        if (clip == null)
        {
            Debug.LogWarning("GroundFootprints: footstep clip not found: audio/audioclips/" + clipName);
        }

        return clip;
    }

    private void EnsureResources()
    {
        if (quadMesh == null)
        {
            quadMesh = BuildQuadMesh();
        }

        if (material != null)
        {
            return;
        }

        // Lit shader so the normal map gives depth/relief under scene lighting.
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Particles/Lit");
        }
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        }
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Transparent");
        }

        if (shader == null)
        {
            return;
        }

        material = new Material(shader);
        material.name = "Footprint_Runtime";

        Texture tex = Resources.Load<Texture>(textureResourcePath);
        if (tex != null)
        {
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", tex);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", tex);
        }

        Texture normalTex = Resources.Load<Texture>(normalTextureResourcePath);
        if (normalTex != null)
        {
            if (material.HasProperty("_BumpMap"))
            {
                material.SetTexture("_BumpMap", normalTex);
                material.EnableKeyword("_NORMALMAP");
            }
            if (material.HasProperty("_NormalMap"))
            {
                material.SetTexture("_NormalMap", normalTex);
            }
            if (material.HasProperty("_BumpScale")) material.SetFloat("_BumpScale", normalStrength);
            if (material.HasProperty("_NormalScale")) material.SetFloat("_NormalScale", normalStrength);
        }

        Color baseColor = new Color(0.22f, 0.19f, 0.16f, alpha);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
        if (material.HasProperty("_Color")) material.SetColor("_Color", baseColor);

        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f); // Transparent
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f); // Alpha
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);

        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = 3000;
    }

    private static Mesh BuildQuadMesh()
    {
        Mesh mesh = new Mesh();
        mesh.name = "FootprintQuad";
        mesh.vertices = new Vector3[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(0.5f, -0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f)
        };
        mesh.uv = new Vector2[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f)
        };
        mesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
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

    private static Vector3 SampleGroundNormal(Vector3 worldPosition)
    {
        Vector3 origin = new Vector3(worldPosition.x, worldPosition.y + 400f, worldPosition.z);
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, origin.y + 2000f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        Vector3 normal = Vector3.zero;

        foreach (RaycastHit hit in hits)
        {
            if (hit.transform == null || hit.transform.name.StartsWith("Tile_") == false)
            {
                continue;
            }

            if (hit.point.y > best)
            {
                best = hit.point.y;
                normal = hit.normal;
            }
        }

        return float.IsNegativeInfinity(best) ? Vector3.zero : normal;
    }
}
