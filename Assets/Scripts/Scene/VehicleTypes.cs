[System.Serializable]
public class VehicleType
{
    public string type;
    public string displayName;
    public string prefab;
    // Canon length in meters. Props auto-scale so their longest horizontal
    // extent matches it (Sandcrawler ~= 37 m).
    public float targetLength;
}

[System.Serializable]
public class VehicleTypes
{
    public VehicleType[] vehicleTypesData;
}
