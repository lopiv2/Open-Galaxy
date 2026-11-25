using UnityEngine;

// Terrain surface family used by grounded effects (landing dust, footsteps...).
// The value is resolved from the tile's ground texture, so Tatooine's dunes
// report Sand while a cliff-only tile reports Rock.
public enum TerrainSurfaceType
{
    Default,
    Sand,
    Rock,
    Snow,
    Grass,
    Dirt,
    Metal,
    Water
}

// Per-tile description of what the ground is made of. TerrainStreamer adds
// one to every near tile (the ones with a MeshCollider), so anything that
// hits the ground can ask the collider instead of guessing from the planet
// name. That means dust kicked up by a landing ship matches the actual spot
// it lands on: dune sea, cliff top, snow field, landing pad...
[DisallowMultipleComponent]
public class TerrainSurface : MonoBehaviour
{
    [Tooltip("Surface family selected from the ground texture.")]
    public TerrainSurfaceType surfaceType = TerrainSurfaceType.Default;

    [Tooltip("Ground texture this tile resolved (diagnostics).")]
    public string textureName;

    [Header("Landing dust overrides")]
    [Tooltip("Turn off to suppress ship landing dust on this surface.")]
    public bool dustEnabled = true;
    [Tooltip("Multiplied over the default dust colour of the surface family.")]
    public Color dustTint = Color.white;
    [Tooltip("Multiplies the amount of dust particles.")]
    public float dustAmount = 1f;
    [Tooltip("Multiplies the size of the dust puffs.")]
    public float dustSize = 1f;

    // Classifies a texture name ("dunes_d", "cliff01", "snow_01"...).
    public static TerrainSurfaceType Classify(string textureName)
    {
        if (string.IsNullOrEmpty(textureName))
        {
            return TerrainSurfaceType.Default;
        }

        string lower = textureName.ToLowerInvariant();

        // Order matters: "sandstone" should be rock before sand, and
        // "snow_rock" should be snow before rock.
        if (lower.Contains("snow") || lower.Contains("ice") || lower.Contains("frost")
            || lower.Contains("glacier"))
        {
            return TerrainSurfaceType.Snow;
        }

        if (lower.Contains("metal") || lower.Contains("industrial") || lower.Contains("plate")
            || lower.Contains("pad") || lower.Contains("durasteel"))
        {
            return TerrainSurfaceType.Metal;
        }

        if (lower.Contains("water") || lower.Contains("ocean") || lower.Contains("sea")
            || lower.Contains("lake") || lower.Contains("river"))
        {
            return TerrainSurfaceType.Water;
        }

        if (lower.Contains("rock") || lower.Contains("stone") || lower.Contains("cliff")
            || lower.Contains("mountain") || lower.Contains("basalt") || lower.Contains("gravel"))
        {
            return TerrainSurfaceType.Rock;
        }

        if (lower.Contains("grass") || lower.Contains("forest") || lower.Contains("moss")
            || lower.Contains("jungle") || lower.Contains("foliage"))
        {
            return TerrainSurfaceType.Grass;
        }

        if (lower.Contains("dirt") || lower.Contains("mud") || lower.Contains("soil"))
        {
            return TerrainSurfaceType.Dirt;
        }

        if (lower.Contains("sand") || lower.Contains("dune") || lower.Contains("desert")
            || lower.Contains("beach") || lower.Contains("coast"))
        {
            return TerrainSurfaceType.Sand;
        }

        return TerrainSurfaceType.Default;
    }

    // Called by TerrainStreamer for every near tile. The optional planet
    // block from StarSystems.json tints/tunes the dust (Tatooine sand is a
    // warmer tone than a generic grey beach).
    public void ApplyTexture(string groundTextureName, LandingDustSettings planetDust)
    {
        textureName = groundTextureName;
        surfaceType = Classify(groundTextureName);

        dustEnabled = true;
        dustTint = Color.white;
        dustAmount = 1f;
        dustSize = 1f;

        if (planetDust == null)
        {
            return;
        }

        dustEnabled = planetDust.enabled;

        if (string.IsNullOrEmpty(planetDust.color) == false
            && ColorUtility.TryParseHtmlString(planetDust.color, out Color tint))
        {
            dustTint = tint;
        }

        if (planetDust.amount > 0f)
        {
            dustAmount = planetDust.amount;
        }

        if (planetDust.size > 0f)
        {
            dustSize = planetDust.size;
        }
    }

    public static TerrainSurface FromCollider(Collider collider)
    {
        if (collider == null)
        {
            return null;
        }

        return collider.GetComponentInParent<TerrainSurface>();
    }

    // Fallback for surfaces without the component (props, landing pads,
    // imported meshes): guess from the renderer's main texture.
    public static TerrainSurfaceType Resolve(Collider collider)
    {
        TerrainSurface surface = FromCollider(collider);
        if (surface != null)
        {
            return surface.surfaceType;
        }

        if (collider == null)
        {
            return TerrainSurfaceType.Default;
        }

        Renderer renderer = collider.GetComponentInChildren<Renderer>();
        if (renderer != null && renderer.sharedMaterial != null)
        {
            Texture main = renderer.sharedMaterial.mainTexture;
            if (main != null)
            {
                return Classify(main.name);
            }
        }

        return TerrainSurfaceType.Default;
    }
}
