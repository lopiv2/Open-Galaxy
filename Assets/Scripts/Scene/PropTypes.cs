[System.Serializable]
public class PropType
{
    public string type;
    public string displayName;
    // Resources folder holding the prefab, with trailing slash.
    public string folder;
    public string prefab;
    // Canon length in meters. The longest horizontal extent is scaled to
    // this (native FBX sizes vary). <= 0 keeps the native size.
    public float targetLength;
}

[System.Serializable]
public class PropTypes
{
    public PropType[] propTypesData;
}
