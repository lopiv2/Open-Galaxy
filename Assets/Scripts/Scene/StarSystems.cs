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
    public string faction;
    public float planetDistance;
    public List<StarSystemSun> suns;
    public List<HyperrouteStop> hyperrouteStops;
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
