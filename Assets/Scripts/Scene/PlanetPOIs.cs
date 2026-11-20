[System.Serializable]
public class PlanetPOIEntry
{
    public string name;
    public float x;
    public float z;
    public float radius;
}

[System.Serializable]
public class PlanetPOIProp
{
    public string prefab;
    public float x;
    public float z;
    public float rotY;
    // Art-direction multiplier on top of the VehiclesTypes targetLength.
    public float scale = 1f;
}

[System.Serializable]
public class PlanetPOILocation
{
    public string planet;
    // Entry gate: deterministic spawn XZ in planet-local meters. All POIs
    // are offsets in this same frame (scene-local XZ while on planet).
    public float entryX;
    public float entryZ;
    public PlanetPOIEntry[] pois;
    public PlanetPOIProp[] props;
}

[System.Serializable]
public class PlanetPOIs
{
    public PlanetPOILocation[] planetPOIsData;
}
