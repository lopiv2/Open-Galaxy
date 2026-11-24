using System.Collections.Generic;

[System.Serializable]
public class StarSystem
{
    public string Planet;
    public float X;
    public float Y;
    public float Z;
    public float sectorRadius;
    public string Region;
    public string Sector;
    public string Grid;
    public string System;
    public int SystemNumber;
    public int seed;
    public string SystemSunType;
    public string planetType;
    public string planetPrefab;
    // Optional per-planet terrain texture keywords (StarSystems.json).
    // Comma-separated, matched against texture names in
    // Resources/objects/terrain/textures. Empty = inherit from PlanetTypes.
    public string terrainTexture;
    // Optional per-planet cliff/mesa terrain settings (StarSystems.json).
    // Missing block = no cliffs, exactly like before.
    public CliffSettings cliffs;
    // Optional base name of the close-up detail texture set. The four maps
    // are expected as <name>_d / _n / _h / _ao in the terrain textures folder.
    public string detailTexture;
    // Optional per-planet texture overlays (paths, cliff-foot sand, cities...).
    public TerrainOverlay[] overlays;
    // Optional per-planet vegetation (trees, bushes, dry shrubs...). Each entry
    // is instantiated per terrain tile around the ship by TerrainStreamer.
    public VegetationSettings[] vegetation;
    // Optional per-planet cloud roof override (StarSystems.json). Zero/empty
    // means "use the PlanetTypes.json climate defaults for planetType".
    public float cloudCoverage;
    public float cloudOpacity;
    public string cloudTint;
    public float cloudAltitude;
    public float cloudThickness;
    public int cloudPuffs;
    public float cloudWhiteout;
    public string faction;
    public float planetDistance;
    public List<StarSystemSun> suns;
    public List<HyperrouteStop> hyperrouteStops;
}

// Per-planet cliff generation, read from the "cliffs" block of a system in
// StarSystems.json. Zero/empty values fall back to the defaults below.
[System.Serializable]
public class CliffSettings
{
    public bool enabled;
    public float mesaHeight;
    public float mesaRoughness;
    public float edgeWidth;
    public float canyonDepth;
    public float canyonWidth;
    public float strength;
    // Fraction of the planet covered by cliff regions (deterministic zones).
    public float coverage;
    public float regionFrequency;
    public float regionFeather;
    public float maskThreshold;
    public float maskFrequency;
    public float slopeStart;
    public float slopeSoftness;
    public string textureType;
    // Mesh resolution. The distant ring needs enough vertices to keep the
    // mesa walls visible while flying; 8 per side flattens them completely.
    public int verticesPerSide;
    public int outerVerticesPerSide;
    public float highDetailRadiusKm;
}

// Per-planet vegetation entry, read from the optional "vegetation" array of a
// StarSystems.json planet. The prefab is loaded from Resources/<folder><prefab>
// and drawn with GPU instancing per terrain tile. The optional material name
// overrides the prefab material (also relative to Resources/<folder>).
[System.Serializable]
public class VegetationSettings
{
    public bool enabled = true;
    public string label;
    public string folder = "objects/trees/";
    public string prefab;
    public string material;
    // Instances per 1000 x 1000 m terrain tile (before slope rejection).
    public int countPerTile = 500;
    public float minScale = 2f;
    public float maxScale = 3.5f;
    // Slopes steeper than this (degrees from up) are skipped. 0 = no filter.
    public float maxSlope = 30f;
    // Tile-level draw distance from the ship, in metres.
    public float maxDistance = 800f;
    // How many random placements to try before giving up on one instance.
    public int placementAttempts = 6;
    // Wind parameters pushed to the material instance at runtime.
    public float windStrength = 0.06f;
    public float windSpeed = 2.2f;
    public float windFrequency = 0.15f;
    // Per-planet offset so two planets do not share the same shrub layout.
    public int seedOffset = 17;
}

// Per-planet terrain texture overlays. Up to three are applied by
// TerrainStreamer; missing/empty entries leave that slot unused.
// mode: "patch", "cliffFoot", "path" or "poiPath".
[System.Serializable]
public class TerrainOverlay
{
    public bool enabled = true;
    public string label;
    // Base name of <name>_d/_n/_ao in the terrain textures folder.
    public string texture;
    public string mode = "patch";
    public float tiling = 0.05f;
    public float strength = 0.7f;
    public float fadeDistance = 600f;
    public float normalStrength = 0.6f;
    public float aoStrength = 0.5f;
    // Path controls (metres / radians).
    public float width = 3f;
    public float feather = 4f;
    public float wanderAmplitude = 10f;
    public float wanderFrequency = 0.002f;
    public float angle = 0f;
    public float offset = 0f;
    // Patch / cliff-foot / edge breakup controls.
    public float noiseScale = 0.002f;
    public float threshold = 0.5f;
    public float patchFeather = 0.12f;
    public float seedOffset = 0f;
    // If true, waypoints come from PlanetPOIs.json instead of a free trail.
    public bool connectPois = false;
}

[System.Serializable]
public class StarSystemSun
{
    public string sunType;
    public float angle;
    public float distance;
    public float scale;
}

// A stop of a star system on a hyperroute, read from StarSystems.json
// ("hyperrouteStops": [{ "route": "Corellian Run", "order": 0 }]).
// Stops are sorted by order to draw the route legs.
[System.Serializable]
public class HyperrouteStop
{
    public string route;
    public int order;
}

[System.Serializable]
public class StarSystems
{
    public StarSystem[] starSystemsData;
}
