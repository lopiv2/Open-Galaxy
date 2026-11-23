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
