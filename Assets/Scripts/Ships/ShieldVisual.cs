using System.Collections.Generic;
using UnityEngine;

//This holds the energy shield effect (force field bubble) for a ship
public class ShieldVisual : MonoBehaviour
{
    [Header("Shield State")]
    public Ship ship;
    public LargeShip largeShip;
    public string shieldType = "default";
    public float maxShieldLevel = 100;
    public float shieldFraction = 1;

    [Header("Shield References")]
    [HideInInspector] public Transform shieldTransform;
    public List<Renderer> shieldRenderers = new List<Renderer>();
    [System.NonSerialized] public MaterialPropertyBlock propertyBlock;

    [Header("Shield Geometry")]
    public float shieldRadius = 1; //The radius of the shield mesh in its own local space
    public float fieldTiling = 3; //How many times the field texture repeats over the mesh

    [Header("Impact Slots")]
    public Vector4[] impactPositions;
    public float[] impactRadii;
    public float[] impactWidths;
    public int nextImpact = 1; //Slot zero is reserved for the background field
    public float lastImpactTime;
    public bool impactsActive;

    [Header("Internal State")]
    public bool setUp;
    public bool blockDirty;
    public bool renderersEnabled;
    public float intensity;
    public float pushedIntensity = -1;

    //This sets the shield visual up once the ship has been created and scaled
    private void Start()
    {
        ShieldVisualFunctions.SetUpShieldVisual(this);
    }

    //This updates the shield visual every frame
    private void Update()
    {
        ShieldVisualFunctions.UpdateShieldVisual(this);
    }

    //This registers an impact from another script (world space hit point)
    public void OnHit(Vector3 hitPoint, float strength = 0.6f)
    {
        ShieldVisualFunctions.OnHit(this, hitPoint, strength);
    }
}
