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
    // Swarm mode: count instances scattered in a disc of radius (meters)
    // around (x, z). count <= 1 keeps the classic single placement.
    public int count = 1;
    public float radius = 0f;
    public int seed = 1;
    public bool randomYaw = false;
    public float scaleVar = 0f;
    public float minDist = 30f;
}

[System.Serializable]
public class PlanetPOICreature
{
    // Resources prefab name inside objects/creatures/ (e.g. "Bantha").
    public string prefab;
    // Herd centre, planet-local meters (same frame as POI/prop x/z).
    public float x;
    public float z;
    // Random herd size, inclusive.
    public int herdMin = 3;
    public int herdMax = 6;
    // Initial spread around the herd centre (meters).
    public float herdRadius = 14f;
    // Roaming radius around the herd centre (meters).
    public float wanderRadius = 90f;
    // Walk speed (meters per second).
    public float speed = 2.2f;
    public int seed = 1;
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
    // Ambient fauna herds (not POIs/navigation targets).
    public PlanetPOICreature[] creatures;
}

[System.Serializable]
public class PlanetPOIs
{
    public PlanetPOILocation[] planetPOIsData;
}
