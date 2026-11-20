[System.Serializable]
public class PlanetType
{

    public string planetType;

    public string terrainTexture;
    public string waterTexture;
    public float terrainScale;
    public float waterScale;
    public float terrainSmoothness;
    public float waterSmoothness;
    public float terrainMetallic;
    public float waterMetallic;
    public float waterSpeed;

    public string lightsTexture;
    public float lights;
    public float lightsHeight;
    public string lightsColor;
    public float lightsScale;
    public float lightsVisibility;

    public string atmosphereColor;

    public float depthsHeight; //Default is 0f
    public float shallowsHeight; //Default is 0.4f
    public float shoreHeight; //Default is 0.48f
    public float sandHeight; //Default is 0.5f
    public float grassHeight; //Default is 0.625f
    public float dirtHeight; //Default is 0.75f
    public float rockHeight; //Default is 0.875f
    public float snowHeight; //Default is 1.0000f

    public string depthsColor;
    public string shallowsColor;
    public string shoreColor;
    public string sandColor;
    public string grassColor;
    public string dirtColor;
    public string rockColor;
    public string snowColor;

    // Cloud deck climate (hybrid Elite + Starfield). All optional in JSON:
    // missing/zero values fall back to code defaults in GalaxyPlanetEntry.
    public float cloudCoverage;   // 0 = clear, 1 = overcast
    public float cloudOpacity;    // per-layer alpha multiplier
    public string cloudTint;      // hex, e.g. "#FFF8F0"
    public float cloudAltitude;   // deck center height
    public float cloudThickness;  // band half-thickness * 2
    public int cloudPuffs;        // billboard puff count inside the band
    public float cloudWhiteout;   // 0-1 fullscreen haze while inside

}

[System.Serializable]
public class PlanetTypes
{
    public PlanetType[] planetTypesData;
}

