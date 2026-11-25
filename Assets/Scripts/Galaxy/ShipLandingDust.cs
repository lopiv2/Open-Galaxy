using UnityEngine;
using UnityEngine.Rendering;

// Dust/sand/snow cloud kicked up by a ship when it settles on a planet.
//
// The look is chosen from the terrain actually under the landing spot: each
// near tile carries a TerrainSurface (added by TerrainStreamer), so Tatooine's
// dunes throw warm sand, a cliff top throws a smaller grey puff and a landing
// pad throws nothing at all. Per-planet tint/amount come from the optional
// "landingDust" block of StarSystems.json (applied to the tiles), so new
// planets only need a JSON entry, not new code.
//
// SoftLanding drives it: BeginDescent() when the descent starts, UpdateDescent()
// every frame while descending, PlayTouchdown() when the ship rests on the
// ground and StopDust() on takeoff / aborted landing.
public class ShipLandingDust : MonoBehaviour
{
    [Header("Ground effect")]
    [Tooltip("Master switch for the procedural landing dust.")]
    public bool dustEnabled = true;
    [Tooltip("Dust starts blowing below this altitude (metres above ground).")]
    public float startAltitude = 35f;
    [Tooltip("Seconds the cloud keeps puffing after touchdown.")]
    public float settleSeconds = 1.2f;
    [Tooltip("Global particle count multiplier.")]
    public float amountScale = 1f;
    [Tooltip("Global particle lifetime multiplier.")]
    public float lifetimeScale = 1f;
    [Tooltip("Extra radius added around the ship footprint (metres).")]
    public float extraRadius = 1.5f;
    [Tooltip("Hard cap on live dust particles.")]
    public int maxParticles = 900;

    [Header("Audio (optional)")]
    public bool audioEnabled = true;
    [Tooltip("Touchdown clip in Resources/audio/audioclips (no extension).")]
    public string touchdownAudioClip = "";
    public float touchdownVolume = 0.8f;

    // One shape per terrain family: how much dust, how big, how fast it
    // spreads and how quickly it rises. Metal pads and water are silent.
    private struct DustProfile
    {
        public bool visible;
        public Color color;
        public float amount;
        public float size;
        public float lifetime;
        public float outSpeed;
        public float updraft;
        public float spread;
    }

    private ParticleSystem dust;
    private ParticleSystemRenderer dustRenderer;
    private Material dustMaterial;
    private Texture2D dustTexture;
    private AudioSource dustAudio;
    private AudioClip dustClip;
    private Transform anchor;

    private TerrainSurface surface;
    private TerrainSurfaceType surfaceType = TerrainSurfaceType.Default;
    private bool surfaceResolved;
    private float footprintRadius = 4f;
    private bool footprintReady;

    private bool descending;
    private bool descentTargetReady;
    private bool effectActive;
    private float emitAccumulator;
    private float settleTimer;
    private Vector3 shipPosition;
    private float groundHeight;

    // Per-planet scalars from TerrainStreamer.landingDust.
    private float planetStartAltitude;
    private float planetSpeed = 1f;
    private float planetLifetime = 1f;
    private float planetUpdraft = 1f;

    // ---------------------------------------------------------------- public

    public void BeginDescent()
    {
        if (dustEnabled == false)
        {
            return;
        }

        if (EnsureEffect() == false)
        {
            return;
        }

        if (footprintReady == false)
        {
            ComputeFootprint();
        }

        ResolvePlanetSettings();

        descending = true;
        descentTargetReady = false;
        settleTimer = 0f;
        emitAccumulator = 0f;
        effectActive = true;
        surfaceResolved = false;

        dust.gameObject.SetActive(true);
        dust.Play(false);
    }

    // Called every frame of the descent coroutine.
    public void UpdateDescent(Vector3 shipWorldPosition, float groundWorldY)
    {
        shipPosition = shipWorldPosition;
        groundHeight = groundWorldY;
        descentTargetReady = true;

        // The ship mostly drops straight down, so the surface under the final
        // spot is known from the first frame. Resolve it once per descent.
        if (surfaceResolved == false)
        {
            ResolveSurface(new Vector3(shipPosition.x, groundHeight, shipPosition.z));
            surfaceResolved = true;
        }
    }

    public void PlayTouchdown(Vector3 shipWorldPosition, float groundWorldY)
    {
        if (dustEnabled == false)
        {
            return;
        }

        if (EnsureEffect() == false)
        {
            return;
        }

        if (footprintReady == false)
        {
            ComputeFootprint();
        }

        shipPosition = shipWorldPosition;
        groundHeight = groundWorldY;
        descending = false;
        settleTimer = settleSeconds;
        emitAccumulator = 0f;
        effectActive = true;

        Vector3 groundPoint = new Vector3(shipWorldPosition.x, groundWorldY, shipWorldPosition.z);
        dust.transform.position = groundPoint;

        ResolveSurface(groundPoint);
        surfaceResolved = true;
        DustProfile profile = BuildProfile();

        if (profile.visible == false)
        {
            dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            dust.gameObject.SetActive(false);
            effectActive = false;
            return;
        }

        dust.gameObject.SetActive(true);
        dust.Play(false);

        int count = Mathf.RoundToInt(profile.amount);
        count = Mathf.Clamp(count, 8, 400);

        for (int i = 0; i < count; i++)
        {
            // Every fourth puff is a slow, large ground-hugging cloud.
            EmitPuff(profile, groundPoint, 1f, i % 4 == 0);
        }

        PlayTouchdownAudio(groundPoint);
    }

    public void StopDust()
    {
        descending = false;
        descentTargetReady = false;
        settleTimer = 0f;
        emitAccumulator = 0f;

        if (dust != null)
        {
            dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            dust.gameObject.SetActive(false);
        }

        effectActive = false;
    }

    // ------------------------------------------------------------------ loop

    private void Update()
    {
        if (effectActive == false || dust == null)
        {
            return;
        }

        if (descending)
        {
            UpdateDescentEmission();
        }
        else if (settleTimer > 0f)
        {
            UpdateSettleEmission();
        }
        else if (dust.particleCount == 0)
        {
            // Everything has faded: park the system until the next landing.
            dust.gameObject.SetActive(false);
            effectActive = false;
        }
    }

    private void UpdateDescentEmission()
    {
        if (descentTargetReady == false)
        {
            return;
        }

        DustProfile profile = BuildProfile();

        if (profile.visible == false)
        {
            return;
        }

        float startAlt = planetStartAltitude > 0f ? planetStartAltitude : startAltitude;
        float agl = Mathf.Max(0f, shipPosition.y - groundHeight);
        float closeness = startAlt > 0f ? 1f - Mathf.Clamp01(agl / startAlt) : 0f;

        if (closeness <= 0f)
        {
            return;
        }

        // Thin trickle far away, heavy wash right before touchdown.
        float rate = Mathf.Lerp(2f, 44f, closeness * closeness) * (profile.amount / 65f);
        float intensity = 0.45f + 0.55f * closeness;

        EmitOverTime(profile, rate, intensity);
    }

    private void UpdateSettleEmission()
    {
        DustProfile profile = BuildProfile();

        if (profile.visible == false)
        {
            settleTimer = 0f;
            return;
        }

        settleTimer -= Time.deltaTime;

        float t = Mathf.Clamp01(settleTimer / Mathf.Max(0.01f, settleSeconds));
        EmitOverTime(profile, 9f * t * (profile.amount / 65f), 0.25f + 0.35f * t);
    }

    private void EmitOverTime(DustProfile profile, float rate, float intensity)
    {
        emitAccumulator += Mathf.Max(0f, rate) * Time.deltaTime;

        int count = Mathf.FloorToInt(emitAccumulator);
        emitAccumulator -= count;

        // Never flood the effect after a long frame.
        count = Mathf.Min(count, 12);

        Vector3 groundPoint = new Vector3(shipPosition.x, groundHeight, shipPosition.z);

        for (int i = 0; i < count; i++)
        {
            EmitPuff(profile, groundPoint, intensity, i % 5 == 4);
        }
    }

    private void EmitPuff(DustProfile profile, Vector3 groundPoint, float intensity, bool slow)
    {
        float angle = Random.value * Mathf.PI * 2f;
        float radius = Mathf.Max(0.5f, footprintRadius) * profile.spread
            * Mathf.Lerp(0.15f, 1.05f, Random.value);

        Vector3 position = groundPoint + new Vector3(
            Mathf.Cos(angle) * radius,
            Random.Range(0.05f, 0.5f),
            Mathf.Sin(angle) * radius);

        float outward;
        float upward;

        if (slow)
        {
            // Ground-hugging haze: barely moves, lingers and swallows the legs.
            outward = Random.Range(0.2f, 0.9f) * profile.outSpeed * 0.35f;
            upward = Random.Range(0.25f, 0.8f) * profile.updraft * 0.7f;
        }
        else
        {
            // Radial ring: thrown away from the hull and rising.
            outward = Random.Range(0.45f, 1f) * profile.outSpeed;
            upward = Random.Range(0.55f, 1.15f) * profile.updraft;
        }

        Vector3 velocity = new Vector3(
            Mathf.Cos(angle) * outward,
            upward,
            Mathf.Sin(angle) * outward);

        float sizeMultiplier = slow ? Random.Range(1.5f, 2.2f) : Random.Range(0.7f, 1.3f);

        Color color = profile.color;
        color.a *= Random.Range(0.65f, 1f) * Mathf.Clamp01(intensity);

        ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams();
        emit.position = position;
        emit.velocity = velocity;
        emit.startLifetime = profile.lifetime * Random.Range(0.8f, 1.25f);
        emit.startSize = profile.size * sizeMultiplier;
        emit.startColor = color;
        emit.rotation = Random.Range(0f, 360f);
        emit.angularVelocity = Random.Range(-20f, 20f);
        emit.applyShapeToPosition = true;

        dust.Emit(emit, 1);
    }

    // --------------------------------------------------------------- surface

    private void ResolveSurface(Vector3 groundPoint)
    {
        surface = null;
        surfaceType = TerrainSurfaceType.Default;

        Vector3 origin = groundPoint + Vector3.up * 4f;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore);

        Collider best = null;
        float bestY = float.NegativeInfinity;

        foreach (RaycastHit hit in hits)
        {
            if (hit.transform == null || hit.collider == null)
            {
                continue;
            }

            // Ignore the ship's own hull, which hovers over the ground point.
            if (hit.transform.IsChildOf(transform) == true)
            {
                continue;
            }

            // Only consider the surface we are actually landing on.
            if (Mathf.Abs(hit.point.y - groundPoint.y) > 2f)
            {
                continue;
            }

            if (hit.point.y > bestY)
            {
                bestY = hit.point.y;
                best = hit.collider;
            }
        }

        if (best == null)
        {
            return;
        }

        surface = TerrainSurface.FromCollider(best);
        surfaceType = TerrainSurface.Resolve(best);
    }

    private DustProfile BuildProfile()
    {
        DustProfile profile = GetDefaultProfile(surfaceType);

        if (surface != null)
        {
            if (surface.dustEnabled == false)
            {
                profile.visible = false;
            }

            Color tint = surface.dustTint;
            profile.color = new Color(
                profile.color.r * tint.r,
                profile.color.g * tint.g,
                profile.color.b * tint.b,
                profile.color.a * tint.a);

            profile.amount *= Mathf.Max(0.05f, surface.dustAmount);
            profile.size *= Mathf.Max(0.05f, surface.dustSize);
        }

        profile.amount *= Mathf.Max(0.05f, amountScale);
        profile.size *= Mathf.Clamp(footprintRadius / 4f, 0.75f, 2.5f);
        profile.lifetime *= Mathf.Max(0.1f, lifetimeScale) * planetLifetime;
        profile.outSpeed *= planetSpeed;
        profile.updraft *= planetUpdraft;

        return profile;
    }

    private static DustProfile GetDefaultProfile(TerrainSurfaceType type)
    {
        DustProfile profile = new DustProfile();
        profile.visible = true;
        profile.color = new Color(0.68f, 0.65f, 0.58f);
        profile.amount = 30f;
        profile.size = 1.6f;
        profile.lifetime = 1.9f;
        profile.outSpeed = 4.8f;
        profile.updraft = 1.7f;
        profile.spread = 1f;

        switch (type)
        {
            case TerrainSurfaceType.Sand:
                profile.color = new Color(0.86f, 0.76f, 0.56f);
                profile.amount = 85f;
                profile.size = 2.5f;
                profile.lifetime = 2.6f;
                profile.outSpeed = 7f;
                profile.updraft = 2.6f;
                profile.spread = 1.2f;
                break;

            case TerrainSurfaceType.Rock:
                profile.color = new Color(0.52f, 0.49f, 0.45f);
                profile.amount = 30f;
                profile.size = 1.4f;
                profile.lifetime = 1.5f;
                profile.outSpeed = 4.2f;
                profile.updraft = 1.4f;
                profile.spread = 0.9f;
                break;

            case TerrainSurfaceType.Snow:
                profile.color = new Color(0.92f, 0.95f, 1f);
                profile.amount = 52f;
                profile.size = 2f;
                profile.lifetime = 3f;
                profile.outSpeed = 4.6f;
                profile.updraft = 3.1f;
                profile.spread = 1.2f;
                break;

            case TerrainSurfaceType.Grass:
                profile.color = new Color(0.48f, 0.51f, 0.31f);
                profile.amount = 22f;
                profile.size = 1.25f;
                profile.lifetime = 1.3f;
                profile.outSpeed = 3.2f;
                profile.updraft = 1.2f;
                profile.spread = 0.85f;
                break;

            case TerrainSurfaceType.Dirt:
                profile.color = new Color(0.45f, 0.35f, 0.25f);
                profile.amount = 42f;
                profile.size = 1.7f;
                profile.lifetime = 2f;
                profile.outSpeed = 5f;
                profile.updraft = 1.8f;
                break;

            case TerrainSurfaceType.Metal:
                // Landing pads / durasteel: burners just glow, no dust.
                profile.visible = false;
                profile.amount = 0f;
                break;

            case TerrainSurfaceType.Water:
                // Water splash is a different effect; keep the ground dry here.
                profile.visible = false;
                profile.amount = 0f;
                break;
        }

        return profile;
    }

    // ---------------------------------------------------------------- setup

    private void ResolvePlanetSettings()
    {
        planetStartAltitude = 0f;
        planetSpeed = 1f;
        planetLifetime = 1f;
        planetUpdraft = 1f;

        TerrainStreamer streamer = FindAnyObjectByType<TerrainStreamer>();

        if (streamer == null || streamer.landingDust == null)
        {
            return;
        }

        LandingDustSettings settings = streamer.landingDust;

        if (settings.altitude > 0f)
        {
            planetStartAltitude = settings.altitude;
        }

        if (settings.speed > 0f)
        {
            planetSpeed = settings.speed;
        }

        if (settings.lifetime > 0f)
        {
            planetLifetime = settings.lifetime;
        }

        if (settings.updraft > 0f)
        {
            planetUpdraft = settings.updraft;
        }

        if (audioEnabled == true && string.IsNullOrEmpty(touchdownAudioClip)
            && string.IsNullOrEmpty(settings.audioClip) == false)
        {
            touchdownAudioClip = settings.audioClip;

            if (settings.audioVolume > 0f)
            {
                touchdownVolume = settings.audioVolume;
            }
        }
    }

    private void ComputeFootprint()
    {
        Bounds bounds = new Bounds(transform.position, Vector3.zero);
        bool hasBounds = false;

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            // Engine glows / trails are much larger than the hull and would
            // inflate the dust ring.
            if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer)
            {
                continue;
            }

            if (hasBounds == false)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (hasBounds == false)
        {
            footprintRadius = 4f;
        }
        else
        {
            footprintRadius = Mathf.Clamp(
                Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.8f, 2.5f, 30f);
        }

        footprintRadius += Mathf.Max(0f, extraRadius);
        footprintReady = true;
    }

    private bool EnsureEffect()
    {
        if (dust != null)
        {
            return true;
        }

        Scene scene = SceneFunctions.GetScene();
        anchor = scene != null ? scene.transform : transform;

        GameObject go = new GameObject("ShipLandingDust");
        go.transform.SetParent(anchor, false);
        go.SetActive(false);

        dust = go.AddComponent<ParticleSystem>();
        dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = dust.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.startSpeed = 0f;
        main.startSize = 1f;
        main.startLifetime = 1f;
        main.startColor = Color.white;
        main.gravityModifier = 0.12f;
        main.maxParticles = Mathf.Max(32, maxParticles);

        ParticleSystem.EmissionModule emission = dust.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = dust.shape;
        shape.enabled = false;

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = dust.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.1f),
                new GradientAlphaKey(0.8f, 0.45f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = dust.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(
                new Keyframe(0f, 0.45f),
                new Keyframe(0.35f, 0.95f),
                new Keyframe(1f, 1.45f)));

        ParticleSystem.RotationOverLifetimeModule rotationOverLifetime = dust.rotationOverLifetime;
        rotationOverLifetime.enabled = true;
        rotationOverLifetime.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);

        ParticleSystem.LimitVelocityOverLifetimeModule limitVelocity = dust.limitVelocityOverLifetime;
        limitVelocity.enabled = true;
        limitVelocity.separateAxes = false;
        limitVelocity.limit = 6f;
        limitVelocity.dampen = 0.08f;
        limitVelocity.space = ParticleSystemSimulationSpace.World;

        ParticleSystem.NoiseModule noise = dust.noise;
        noise.enabled = true;
        noise.strength = 0.35f;
        noise.frequency = 0.4f;
        noise.scrollSpeed = 0.25f;
        noise.damping = true;
        noise.octaveCount = 2;

        dustRenderer = go.GetComponent<ParticleSystemRenderer>();
        dustRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        dustRenderer.alignment = ParticleSystemRenderSpace.View;
        dustRenderer.sortMode = ParticleSystemSortMode.Distance;
        dustRenderer.shadowCastingMode = ShadowCastingMode.Off;
        dustRenderer.receiveShadows = false;
        dustRenderer.maxParticleSize = 12f;

        dustMaterial = CreateDustMaterial();
        if (dustMaterial != null)
        {
            dustRenderer.sharedMaterial = dustMaterial;
        }

        if (audioEnabled == true)
        {
            dustAudio = go.AddComponent<AudioSource>();
            dustAudio.playOnAwake = false;
            dustAudio.spatialBlend = 0.85f;
            dustAudio.rolloffMode = AudioRolloffMode.Linear;
            dustAudio.maxDistance = 250f;
            dustAudio.dopplerLevel = 0f;
        }

        return true;
    }

    private Material CreateDustMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Particles/Lit");
        }

        if (shader == null)
        {
            shader = Shader.Find("Particles/Standard Unlit");
        }

        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        if (shader == null)
        {
            return null;
        }

        dustTexture = CreatePuffTexture();

        Material material = new Material(shader);
        material.name = "ShipLandingDust_Runtime";

        if (dustTexture != null)
        {
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", dustTexture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", dustTexture);
        }

        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
        if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);

        material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = 3000;

        return material;
    }

    // Soft irregular puff: smooth radial falloff broken up by noise so the
    // billboards do not read as perfect circles.
    private static Texture2D CreatePuffTexture()
    {
        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "ShipLandingDustPuff";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f;
                float dy = (y + 0.5f) / size * 2f - 1f;
                float radius = Mathf.Sqrt(dx * dx + dy * dy);

                float noise = Mathf.PerlinNoise(dx * 4.2f + 13.7f, dy * 4.2f + 7.1f);
                noise = Mathf.Lerp(0.55f, 1f, noise);

                float radial = Mathf.Clamp01(1f - radius);
                radial = Mathf.SmoothStep(0f, 1f, radial);
                radial *= radial;

                float alpha = Mathf.Clamp01(radial * noise);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private void PlayTouchdownAudio(Vector3 groundPoint)
    {
        if (audioEnabled == false || dustAudio == null || string.IsNullOrEmpty(touchdownAudioClip))
        {
            return;
        }

        if (dustClip == null)
        {
            dustClip = Resources.Load<AudioClip>("audio/audioclips/" + touchdownAudioClip);

            if (dustClip == null)
            {
                Debug.LogWarning("ShipLandingDust: audio clip not found: audio/audioclips/" + touchdownAudioClip);
            }
        }

        if (dustClip == null)
        {
            return;
        }

        dustAudio.transform.position = groundPoint;
        dustAudio.pitch = Random.Range(0.94f, 1.06f);
        dustAudio.PlayOneShot(dustClip, Mathf.Clamp01(touchdownVolume));
    }

    private void OnDestroy()
    {
        if (dust != null)
        {
            Destroy(dust.gameObject);
            dust = null;
        }

        if (dustMaterial != null)
        {
            Destroy(dustMaterial);
            dustMaterial = null;
        }

        if (dustTexture != null)
        {
            Destroy(dustTexture);
            dustTexture = null;
        }
    }
}
