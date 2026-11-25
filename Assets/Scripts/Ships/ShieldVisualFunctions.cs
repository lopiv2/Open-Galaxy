using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

//This contains all the functions used to display and animate the ship energy shields
public static class ShieldVisualFunctions
{
    #region settings

    //This turns the whole shield visual system on or off
    public static bool shieldVisualsEnabled = true;

    //This is the maximum distance from the camera at which shields are drawn
    public static float maxRenderDistance = 6000;

    //This is the minimum on screen size of a shield before it stops being drawn
    public static float minScreenRatio = 0.002f;

    //These are the shield colours
    public static Color innerColour = new Color(0.60f, 0.90f, 1.00f, 1f);
    public static Color outerColour = new Color(0.15f, 0.45f, 0.95f, 1f);

    //This is how bright the shield glow is when the shields are at full strength
    public static float tintIntensity = 0.45f;

    //This is how visible the shield bubble is when it has not been hit (the "background" field).
    //The bubble itself is hidden, only the impact rings are drawn.
    public static float backgroundOpacity = 0f;

    //This is the minimum time between impacts on the same ship (stops a single burst filling every slot)
    public static float impactInterval = 0.03f;

    //These are the ripple settings, all in shield radius units
    public static float rippleWidth = 0.06f; //Thickness of each expanding ring
    public static float rippleSpeed = 0.7f; //How fast the rings open (radius per second)
    public static float rippleDecay = 2.0f; //How fast the rings fade out
    public static float rippleMaxRadius = 0.7f; //The leading ring stops here

    //The number of impact slots in the shader and the slot used for the background field
    private const int impactSlots = 24;
    private const int backgroundSlot = 0;

    #endregion

    #region references

    private static Material shieldMaterial;
    private static Texture2D fieldTexture;
    private static Mesh bubbleMesh;
    private static bool materialNotFound;

    private static int[] impactPositionIds;
    private static int[] impactPowerIds;
    private static int[] impactRingIds;

    //Shader property IDs
    private static readonly int fieldTextureId = Shader.PropertyToID("_FieldTex");
    private static readonly int fieldTextureStId = Shader.PropertyToID("_FieldTex_ST");
    private static readonly int innerTintId = Shader.PropertyToID("_InnerTint");
    private static readonly int outerTintId = Shader.PropertyToID("_OuterTint");
    private static readonly int offsetId = Shader.PropertyToID("_Offset");
    private static readonly int fieldPanSpeedId = Shader.PropertyToID("_FieldPanSpeed");
    private static readonly int fieldBackgroundId = Shader.PropertyToID("_FieldBG");
    private static readonly int fieldSparksId = Shader.PropertyToID("_FieldSparks");

    //Camera cache so the scene is not searched for every ship every frame
    private static Camera cachedCamera;
    private static float nextCameraSearch;

    #endregion

    #region set up

    //This sets up the shield visual for a ship
    public static void SetUpShieldVisual(ShieldVisual shieldVisual)
    {
        if (shieldVisual == null) { return; }
        if (shieldVisual.setUp == true) { return; }

        shieldVisual.setUp = true;

        //This gets the ship scripts
        shieldVisual.ship = shieldVisual.GetComponent<Ship>();
        shieldVisual.largeShip = shieldVisual.GetComponent<LargeShip>();

        if (shieldVisual.ship == null && shieldVisual.largeShip == null)
        {
            shieldVisual.enabled = false;
            return;
        }

        //This gets the shield type and the shield rating
        if (shieldVisual.ship != null)
        {
            shieldVisual.shieldType = shieldVisual.ship.shieldType;
            shieldVisual.maxShieldLevel = shieldVisual.ship.shieldRating;
        }
        else if (shieldVisual.largeShip != null)
        {
            shieldVisual.shieldType = shieldVisual.largeShip.shieldType;
            shieldVisual.maxShieldLevel = shieldVisual.largeShip.shieldRating;
        }

        if (shieldVisual.maxShieldLevel < 0) { shieldVisual.maxShieldLevel = 0; }

        //This creates the impact data
        shieldVisual.impactPositions = new Vector4[impactSlots];
        shieldVisual.impactRadii = new float[impactSlots];
        shieldVisual.impactWidths = new float[impactSlots];
        shieldVisual.propertyBlock = new MaterialPropertyBlock();
        shieldVisual.nextImpact = 1;

        CreateImpactIds();

        //Only ships with shields get a shield mesh, the rest create it later if a mission gives them shields
        if (shieldVisual.maxShieldLevel > 0)
        {
            SetUpShieldMesh(shieldVisual);
        }
    }

    //This creates the shield mesh and sets the shader values that do not change from frame to frame
    private static void SetUpShieldMesh(ShieldVisual shieldVisual)
    {
        if (shieldVisual.shieldRenderers.Count > 0) { return; }
        if (shieldVisual.enabled == false) { return; }

        //This finds or creates the shield mesh
        FindOrCreateShieldMesh(shieldVisual);

        if (shieldVisual.shieldRenderers == null || shieldVisual.shieldRenderers.Count == 0)
        {
            shieldVisual.enabled = false;
            return;
        }

        shieldVisual.shieldTransform = shieldVisual.shieldRenderers[0].transform;

        //This sets the static shader values
        SetUpMaterialProperties(shieldVisual);

        //This sets the background field (disabled by default so the bubble stays invisible)
        UpdateBackground(shieldVisual);

        shieldVisual.blockDirty = true;
        PushPropertyBlock(shieldVisual);
    }

    //This creates the shader property IDs for the impact slots
    private static void CreateImpactIds()
    {
        if (impactPositionIds != null) { return; }

        impactPositionIds = new int[impactSlots];
        impactPowerIds = new int[impactSlots];
        impactRingIds = new int[impactSlots];

        for (int i = 0; i < impactSlots; i++)
        {
            impactPositionIds[i] = Shader.PropertyToID("_Pos_" + i.ToString());
            impactPowerIds[i] = Shader.PropertyToID("_Pow_" + i.ToString());
            impactRingIds[i] = Shader.PropertyToID("_Ring_" + i.ToString());
        }
    }

    //This finds an existing shield mesh on the ship prefab or creates a bubble around it
    private static void FindOrCreateShieldMesh(ShieldVisual shieldVisual)
    {
        shieldVisual.shieldRenderers.Clear();

        Material material = GetShieldMaterial();

        if (material == null)
        {
            shieldVisual.enabled = false;
            return;
        }

        //This looks for a shield mesh that is already part of the ship model
        Transform existingShield = FindChildShieldMesh(shieldVisual.transform);

        if (existingShield != null)
        {
            MeshFilter meshFilter = existingShield.GetComponent<MeshFilter>();
            Renderer renderer = existingShield.GetComponent<Renderer>();

            if (meshFilter != null && meshFilter.sharedMesh != null && renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.enabled = false;

                shieldVisual.shieldRenderers.Add(renderer);
                shieldVisual.shieldRadius = meshFilter.sharedMesh.bounds.extents.magnitude;
                shieldVisual.fieldTiling = 1;

                if (shieldVisual.shieldRadius <= 0) { shieldVisual.shieldRadius = 1; }

                return;
            }
        }

        //This creates a bubble that fits around the ship
        Bounds localBounds = new Bounds(Vector3.zero, Vector3.one);

        if (TryGetLocalBounds(shieldVisual.gameObject, out localBounds) == false)
        {
            localBounds = new Bounds(Vector3.zero, Vector3.one * 10);
        }

        GameObject bubble = new GameObject("shieldfield_nocollider");
        bubble.layer = shieldVisual.gameObject.layer;
        bubble.transform.SetParent(shieldVisual.transform, false);
        bubble.transform.localPosition = localBounds.center;
        bubble.transform.localRotation = Quaternion.identity;
        bubble.transform.localScale = localBounds.extents * 2.1f;

        MeshFilter bubbleMeshFilter = bubble.AddComponent<MeshFilter>();
        bubbleMeshFilter.sharedMesh = GetBubbleMesh();

        MeshRenderer bubbleRenderer = bubble.AddComponent<MeshRenderer>();
        bubbleRenderer.sharedMaterial = material;
        bubbleRenderer.shadowCastingMode = ShadowCastingMode.Off;
        bubbleRenderer.receiveShadows = false;
        bubbleRenderer.lightProbeUsage = LightProbeUsage.Off;
        bubbleRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        bubbleRenderer.enabled = false;

        //The bubble is a unit sphere so the shader works in a normalised space
        shieldVisual.shieldRenderers.Add(bubbleRenderer);
        shieldVisual.shieldRadius = 1;
        shieldVisual.fieldTiling = 3;
    }

    //This finds a shield mesh that is already part of a ship model
    private static Transform FindChildShieldMesh(Transform ship)
    {
        Transform[] children = ship.GetComponentsInChildren<Transform>(true);

        foreach (Transform child in children)
        {
            if (child == ship) { continue; }

            string childName = child.name.ToLower();

            if (childName.Contains("shield") == false) { continue; }
            if (childName.Contains("system")) { continue; }

            if (child.GetComponent<MeshFilter>() == null) { continue; }
            if (child.GetComponent<Renderer>() == null) { continue; }
            if (child.GetComponent<ShipSystem>() != null) { continue; }

            return child;
        }

        return null;
    }

    //This gets the bounds of the ship in the local space of the ship root
    private static bool TryGetLocalBounds(GameObject ship, out Bounds localBounds)
    {
        localBounds = new Bounds(Vector3.zero, Vector3.zero);

        bool found = false;

        Renderer[] renderers = ship.GetComponentsInChildren<Renderer>();

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) { continue; }
            if (renderer is ParticleSystemRenderer) { continue; }
            if (renderer is TrailRenderer) { continue; }
            if (renderer is LineRenderer) { continue; }

            string rendererName = renderer.gameObject.name.ToLower();

            if (rendererName.Contains("shield")) { continue; }
            if (rendererName.Contains("shadow")) { continue; }

            Bounds worldBounds = renderer.bounds;

            if (worldBounds.size.sqrMagnitude <= 0.0001f) { continue; }

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = worldBounds.center;

                corner.x = corner.x + ((i & 1) == 0 ? -worldBounds.extents.x : worldBounds.extents.x);
                corner.y = corner.y + ((i & 2) == 0 ? -worldBounds.extents.y : worldBounds.extents.y);
                corner.z = corner.z + ((i & 4) == 0 ? -worldBounds.extents.z : worldBounds.extents.z);

                Vector3 localCorner = ship.transform.InverseTransformPoint(corner);

                if (found == false)
                {
                    localBounds = new Bounds(localCorner, Vector3.zero);
                    found = true;
                }
                else
                {
                    localBounds.Encapsulate(localCorner);
                }
            }
        }

        //This makes sure the bubble is never degenerate
        if (localBounds.size.x <= 0.0001f) { localBounds.size = new Vector3(0.0001f, localBounds.size.y, localBounds.size.z); }
        if (localBounds.size.y <= 0.0001f) { localBounds.size = new Vector3(localBounds.size.x, 0.0001f, localBounds.size.z); }
        if (localBounds.size.z <= 0.0001f) { localBounds.size = new Vector3(localBounds.size.x, localBounds.size.y, 0.0001f); }

        return found;
    }

    //This sets the shader values that do not change from frame to frame
    private static void SetUpMaterialProperties(ShieldVisual shieldVisual)
    {
        MaterialPropertyBlock block = shieldVisual.propertyBlock;

        float radius = shieldVisual.shieldRadius;

        if (radius <= 0) { radius = 1; }

        block.SetFloat(offsetId, radius * 0.01f);
        block.SetFloat(fieldPanSpeedId, 0.03f);
        block.SetFloat(fieldSparksId, 0.5f);

        //This uses the texture set on the material if there is one, otherwise it generates the default energy pattern
        Material material = GetShieldMaterial();

        bool materialHasTexture = false;

        if (material != null && material.GetTexture(fieldTextureId) != null)
        {
            materialHasTexture = true;
        }

        if (materialHasTexture == false)
        {
            block.SetTexture(fieldTextureId, GetFieldTexture());
            block.SetVector(fieldTextureStId, new Vector4(shieldVisual.fieldTiling, shieldVisual.fieldTiling, 0, 0));
        }
    }

    #endregion

    #region impact functions

    //This registers a hit on a ship's shields using a world space position
    public static void RegisterImpact(GameObject objectHit, Vector3 hitPosition, float strength = 0.6f)
    {
        if (objectHit == null) { return; }

        ShieldVisual shieldVisual = objectHit.GetComponentInParent<ShieldVisual>();

        if (shieldVisual == null) { return; }

        OnHit(shieldVisual, hitPosition, strength);
    }

    //This registers a hit on a ship's shields using a world space position
    public static void OnHit(ShieldVisual shieldVisual, Vector3 hitPosition, float strength)
    {
        if (shieldVisual == null) { return; }
        if (shieldVisual.setUp == false) { return; }
        if (shieldVisual.shieldTransform == null) { return; }
        if (shieldVisual.shieldFraction <= 0.001f) { return; }
        if (shieldVisual.shieldType == "blackhole") { return; }

        Vector3 localPoint = shieldVisual.shieldTransform.InverseTransformPoint(hitPosition);

        AddImpact(shieldVisual, localPoint, strength);
    }

    //This stores a new ripple in the next free shader slot
    private static void AddImpact(ShieldVisual shieldVisual, Vector3 localPoint, float strength)
    {
        if (impactPositionIds == null) { CreateImpactIds(); }

        //This stops a single burst of lasers filling every slot with the same hit
        if (Time.time - shieldVisual.lastImpactTime < impactInterval) { return; }

        shieldVisual.lastImpactTime = Time.time;

        int slot = shieldVisual.nextImpact;

        shieldVisual.nextImpact = shieldVisual.nextImpact + 1;

        if (shieldVisual.nextImpact >= impactSlots) { shieldVisual.nextImpact = backgroundSlot + 1; }

        float unit = Mathf.Max(shieldVisual.shieldRadius, 0.0001f);
        float clampedStrength = Mathf.Clamp01(strength);

        //The ring starts as a small point at the hit position and opens outwards
        shieldVisual.impactPositions[slot] = new Vector4(localPoint.x, localPoint.y, localPoint.z, clampedStrength);
        shieldVisual.impactRadii[slot] = unit * 0.01f;

        //Harder hits produce a slightly thicker ring
        shieldVisual.impactWidths[slot] = unit * rippleWidth * (0.6f + 0.8f * clampedStrength);

        shieldVisual.propertyBlock.SetVector(impactPositionIds[slot], shieldVisual.impactPositions[slot]);
        shieldVisual.propertyBlock.SetFloat(impactRingIds[slot], shieldVisual.impactRadii[slot]);
        shieldVisual.propertyBlock.SetFloat(impactPowerIds[slot], shieldVisual.impactWidths[slot]);

        shieldVisual.blockDirty = true;
    }

    #endregion

    #region update functions

    //This updates the shield visual
    public static void UpdateShieldVisual(ShieldVisual shieldVisual)
    {
        if (shieldVisual == null) { return; }
        if (shieldVisual.setUp == false) { return; }
        if (shieldVisual.shieldRenderers == null) { return; }

        if (shieldVisualsEnabled == false)
        {
            SetRenderersEnabled(shieldVisual, false);
            return;
        }

        UpdateShieldLevel(shieldVisual);

        //This creates the shield mesh the first time the ship actually has shields
        if (shieldVisual.shieldRenderers.Count == 0)
        {
            if (shieldVisual.shieldFraction <= 0.001f) { return; }

            SetUpShieldMesh(shieldVisual);

            if (shieldVisual.shieldRenderers.Count == 0) { return; }
        }

        UpdateImpacts(shieldVisual);
        UpdateIntensity(shieldVisual);
        UpdateBackground(shieldVisual);
        UpdateVisibility(shieldVisual);

        if (shieldVisual.blockDirty == true)
        {
            shieldVisual.blockDirty = false;
            PushPropertyBlock(shieldVisual);
        }
    }

    //This gets the current shield level of the ship
    private static void UpdateShieldLevel(ShieldVisual shieldVisual)
    {
        float level = 0;
        float rating = shieldVisual.maxShieldLevel;

        if (shieldVisual.ship != null)
        {
            if (shieldVisual.ship.energyManagementSystem != null)
            {
                level = shieldVisual.ship.energyManagementSystem.shieldLevel;
            }

            if (shieldVisual.ship.shieldRating > 0)
            {
                rating = shieldVisual.ship.shieldRating;
            }
        }
        else if (shieldVisual.largeShip != null)
        {
            level = shieldVisual.largeShip.shieldLevel;

            if (shieldVisual.largeShip.shieldRating > 0)
            {
                rating = shieldVisual.largeShip.shieldRating;
            }
        }

        if (rating <= 0) { rating = 1; }

        shieldVisual.maxShieldLevel = rating;
        shieldVisual.shieldFraction = Mathf.Clamp01(level / rating);
    }

    //This fades out the ripples that are still active
    private static void UpdateImpacts(ShieldVisual shieldVisual)
    {
        if (shieldVisual.impactPositions == null) { return; }

        float decay = Time.deltaTime * rippleDecay;
        float unit = Mathf.Max(shieldVisual.shieldRadius, 0.0001f);
        float expansion = Time.deltaTime * rippleSpeed * unit;
        float maxRadius = rippleMaxRadius * unit;

        bool active = false;

        for (int i = backgroundSlot + 1; i < impactSlots; i++)
        {
            Vector4 impact = shieldVisual.impactPositions[i];

            if (impact.w <= 0) { continue; }

            //The ring opens outwards and fades out as it grows
            float radius = shieldVisual.impactRadii[i] + expansion;

            impact.w = impact.w - decay;

            if (impact.w <= 0 | radius >= maxRadius)
            {
                impact.w = 0;
            }

            shieldVisual.impactPositions[i] = impact;
            shieldVisual.impactRadii[i] = radius;

            shieldVisual.propertyBlock.SetVector(impactPositionIds[i], impact);
            shieldVisual.propertyBlock.SetFloat(impactRingIds[i], radius);

            active = true;
        }

        if (active == true | shieldVisual.impactsActive == true)
        {
            shieldVisual.blockDirty = true;
        }

        shieldVisual.impactsActive = active;
    }

    //This sets the shield brightness based on the shield level
    private static void UpdateIntensity(ShieldVisual shieldVisual)
    {
        float target = tintIntensity * (0.4f + 0.6f * shieldVisual.shieldFraction);

        shieldVisual.intensity = Mathf.MoveTowards(shieldVisual.intensity, target, Time.deltaTime * 2f);

        if (Mathf.Abs(shieldVisual.intensity - shieldVisual.pushedIntensity) <= 0.002f) { return; }

        shieldVisual.pushedIntensity = shieldVisual.intensity;

        Color innerTint = new Color(innerColour.r, innerColour.g, innerColour.b, shieldVisual.intensity);
        Color outerTint = new Color(outerColour.r, outerColour.g, outerColour.b, shieldVisual.intensity);

        shieldVisual.propertyBlock.SetColor(innerTintId, innerTint);
        shieldVisual.propertyBlock.SetColor(outerTintId, outerTint);
        shieldVisual.propertyBlock.SetFloat(fieldBackgroundId, 0.8f * shieldVisual.shieldFraction);

        shieldVisual.blockDirty = true;
    }

    //This keeps the faint background field in sync with the shield level
    private static void UpdateBackground(ShieldVisual shieldVisual)
    {
        float alpha = backgroundOpacity * (0.4f + 0.6f * shieldVisual.shieldFraction);

        Vector4 background = shieldVisual.impactPositions[backgroundSlot];

        if (Mathf.Abs(background.w - alpha) <= 0.002f) { return; }

        background = new Vector4(0, 0, 0, alpha);

        shieldVisual.impactPositions[backgroundSlot] = background;

        shieldVisual.propertyBlock.SetVector(impactPositionIds[backgroundSlot], background);
        shieldVisual.propertyBlock.SetFloat(impactPowerIds[backgroundSlot], 0.1f);

        shieldVisual.blockDirty = true;
    }

    //This shows or hides the shield based on the shield level, the distance and the camera
    private static void UpdateVisibility(ShieldVisual shieldVisual)
    {
        bool show = shieldVisual.shieldFraction > 0.001f;

        //Black hole (dovin basal) shields do not use a force field bubble
        if (shieldVisual.shieldType == "blackhole") { show = false; }

        //Ships with no shield mesh in the model always use a bubble, they are left visible at full strength
        Camera camera = GetSceneCamera();

        if (show == true & camera != null)
        {
            Vector3 cameraPosition = camera.transform.position;

            float worldRadius = shieldVisual.shieldTransform.lossyScale.magnitude * shieldVisual.shieldRadius;
            float distance = Vector3.Distance(cameraPosition, shieldVisual.shieldTransform.position);

            if (distance > maxRenderDistance + worldRadius) { show = false; }

            if (show == true & worldRadius / Mathf.Max(distance, 0.01f) < minScreenRatio) { show = false; }

            //This stops the bubble filling the screen when the camera is inside it (own ship cockpit camera)
            if (show == true)
            {
                Vector3 localCameraPosition = shieldVisual.shieldTransform.InverseTransformPoint(cameraPosition);

                if (localCameraPosition.magnitude < shieldVisual.shieldRadius * 0.9f) { show = false; }
            }
        }

        SetRenderersEnabled(shieldVisual, show);
    }

    //This turns the shield renderers on or off
    private static void SetRenderersEnabled(ShieldVisual shieldVisual, bool enabled)
    {
        if (shieldVisual.renderersEnabled == enabled) { return; }

        shieldVisual.renderersEnabled = enabled;

        for (int i = 0; i < shieldVisual.shieldRenderers.Count; i++)
        {
            Renderer renderer = shieldVisual.shieldRenderers[i];

            if (renderer != null)
            {
                renderer.enabled = enabled;
            }
        }

        //This makes sure the shader values are applied the next time the shield is shown
        if (enabled == true)
        {
            shieldVisual.pushedIntensity = -1;
            shieldVisual.blockDirty = true;
            PushPropertyBlock(shieldVisual);
        }
    }

    //This applies the material property block to all the shield renderers
    private static void PushPropertyBlock(ShieldVisual shieldVisual)
    {
        if (shieldVisual.propertyBlock == null) { return; }

        for (int i = 0; i < shieldVisual.shieldRenderers.Count; i++)
        {
            Renderer renderer = shieldVisual.shieldRenderers[i];

            if (renderer != null)
            {
                renderer.SetPropertyBlock(shieldVisual.propertyBlock);
            }
        }
    }

    #endregion

    #region assets

    //This gets the camera used to draw the scene
    private static Camera GetSceneCamera()
    {
        if (cachedCamera != null && Time.time < nextCameraSearch) { return cachedCamera; }

        nextCameraSearch = Time.time + 1;

        cachedCamera = null;

        Scene scene = SceneFunctions.GetScene();

        //The scene does not exist in the main menu or while a planet is loading,
        //so each reference has to be checked on its own (a missing scene or
        //camera used to throw a NullReferenceException here)
        if (scene == null) { return null; }

        if (scene.ogCamera == null) { return null; }

        if (scene.ogCamera.mainCamera == null) { return null; }

        cachedCamera = scene.ogCamera.mainCamera.GetComponent<Camera>();

        return cachedCamera;
    }

    //This loads the shield material
    public static Material GetShieldMaterial()
    {
        if (shieldMaterial != null) { return shieldMaterial; }
        if (materialNotFound == true) { return null; }

        shieldMaterial = Resources.Load<Material>(OGGetAddress.ships + "common/shield_forcefield");

        if (shieldMaterial == null)
        {
            materialNotFound = true;
            Debug.LogWarning("ShieldVisualFunctions: could not find materials/objects/ships/common/shield_forcefield");
        }

        return shieldMaterial;
    }

    //This creates the field texture that gives the shield its energy pattern
    private static Texture2D GetFieldTexture()
    {
        if (fieldTexture == null)
        {
            fieldTexture = CreateFieldTexture(256, 8);
        }

        return fieldTexture;
    }

    //This creates a seamless energy cell texture used by the force field shader
    private static Texture2D CreateFieldTexture(int size, int cells)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true);
        texture.name = "shield_field_texture";
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.filterMode = FilterMode.Bilinear;
        texture.anisoLevel = 4;

        float cellSize = (float)size / (float)cells;

        //This places a random feature point inside every cell
        Vector2[] points = new Vector2[cells * cells];

        for (int y = 0; y < cells; y++)
        {
            for (int x = 0; x < cells; x++)
            {
                float offsetX = Hash(x, y, 1);
                float offsetY = Hash(x, y, 2);

                points[(y * cells) + x] = new Vector2((x + offsetX) * cellSize, (y + offsetY) * cellSize);
            }
        }

        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 point = new Vector2(x + 0.5f, y + 0.5f);

                int cellX = Mathf.FloorToInt(point.x / cellSize);
                int cellY = Mathf.FloorToInt(point.y / cellSize);

                float nearest = float.MaxValue;
                float second = float.MaxValue;
                int nearestCell = 0;

                for (int offsetY = -1; offsetY <= 1; offsetY++)
                {
                    for (int offsetX = -1; offsetX <= 1; offsetX++)
                    {
                        int gridX = ((cellX + offsetX) % cells + cells) % cells;
                        int gridY = ((cellY + offsetY) % cells + cells) % cells;

                        Vector2 feature = points[(gridY * cells) + gridX];
                        Vector2 delta = feature - point;

                        //This wraps the distance so the texture tiles without seams
                        delta.x -= Mathf.Round(delta.x / size) * size;
                        delta.y -= Mathf.Round(delta.y / size) * size;

                        float distance = delta.magnitude;

                        if (distance < nearest)
                        {
                            second = nearest;
                            nearest = distance;
                            nearestCell = (gridY * cells) + gridX;
                        }
                        else if (distance < second)
                        {
                            second = distance;
                        }
                    }
                }

                //The edge between two cells is the brightest part of the pattern
                float edge = Mathf.Clamp01((second - nearest) / (cellSize * 0.18f));
                float glow = (1f - edge) * (1f - edge);

                float cellBrightness = 0.25f + (0.35f * Hash(nearestCell % cells, nearestCell / cells, 3));

                float value = Mathf.Clamp01((glow * 0.75f) + cellBrightness);

                //This adds a little grain so the texture does not look flat
                value = value * (0.9f + (0.1f * Hash(x, y, 4)));

                pixels[(y * size) + x] = new Color(value, value, value, 1f);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(true, false);

        return texture;
    }

    //This creates a simple sphere mesh used as the shield bubble
    private static Mesh GetBubbleMesh()
    {
        if (bubbleMesh == null)
        {
            bubbleMesh = CreateBubbleMesh(24, 16);
        }

        return bubbleMesh;
    }

    //This creates a unit sphere with smooth normals and spherical UVs
    private static Mesh CreateBubbleMesh(int longitudes, int latitudes)
    {
        Mesh mesh = new Mesh();
        mesh.name = "shield_bubble";

        Vector3[] vertices = new Vector3[((latitudes + 1) * (longitudes + 1))];
        Vector3[] normals = new Vector3[vertices.Length];
        Vector2[] uvs = new Vector2[vertices.Length];

        int index = 0;

        for (int latitude = 0; latitude <= latitudes; latitude++)
        {
            float theta = Mathf.PI * ((float)latitude / (float)latitudes);
            float sinTheta = Mathf.Sin(theta);
            float cosTheta = Mathf.Cos(theta);

            for (int longitude = 0; longitude <= longitudes; longitude++)
            {
                float phi = 2f * Mathf.PI * ((float)longitude / (float)longitudes);

                Vector3 direction = new Vector3(sinTheta * Mathf.Cos(phi), cosTheta, sinTheta * Mathf.Sin(phi));

                vertices[index] = direction;
                normals[index] = direction;
                uvs[index] = new Vector2((float)longitude / (float)longitudes, 1f - ((float)latitude / (float)latitudes));

                index++;
            }
        }

        int[] triangles = new int[latitudes * longitudes * 6];
        int triangleIndex = 0;

        for (int latitude = 0; latitude < latitudes; latitude++)
        {
            for (int longitude = 0; longitude < longitudes; longitude++)
            {
                int current = (latitude * (longitudes + 1)) + longitude;
                int next = current + longitudes + 1;

                triangles[triangleIndex] = current;
                triangles[triangleIndex + 1] = next;
                triangles[triangleIndex + 2] = current + 1;

                triangles[triangleIndex + 3] = current + 1;
                triangles[triangleIndex + 4] = next;
                triangles[triangleIndex + 5] = next + 1;

                triangleIndex = triangleIndex + 6;
            }
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();

        return mesh;
    }

    //This gives a stable pseudo random number between 0 and 1
    private static float Hash(int x, int y, int seed)
    {
        int value = (x * 73856093) ^ (y * 19349663) ^ (seed * 83492791);

        value = (value ^ (value >> 13)) * 1274126177;
        value = value ^ (value >> 16);

        int wrapped = ((value % 10000) + 10000) % 10000;

        return wrapped / 10000f;
    }

    #endregion
}
