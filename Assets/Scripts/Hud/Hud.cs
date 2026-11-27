using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Hud : MonoBehaviour
{
    //Key References
    public Scene scene;
    public Ship mainShip;
    public Camera mainCamera;

    //Object lists
    public List<CanvasGroup> canvasGroups;
    public List<Text> textBoxes;
    public List<Slider> sliders;
    
    //Radar Display
    public GameObject radarTarget;
    public GameObject radarTargetSelected;
    public List<GameObject> radarTargets;

    //Targeting System
    public GameObject target;
    public GameObject targetSelected;
    public List<GameObject> targets;

    //Crosshair
    [HideInInspector] public CanvasGroup crosshair;
    [HideInInspector] public CanvasGroup crosshairLocked;
    [HideInInspector] public CanvasGroup autoaim;
    [HideInInspector] public Image crosshairImage;
    [HideInInspector] public Image crosshairLockedImage;
    [HideInInspector] public Text autoaimLabel;

    //Offscreen target arrow (edge indicator ported from Hud_Old.DisplaySelectionBraces)
    public GameObject arrowTarget;
    [HideInInspector] public CanvasGroup arrowCanvasGroup;
    [HideInInspector] public float arrowPreviousRotation;
    [HideInInspector] public float arrowTargetRotation;
    [HideInInspector] public float arrowLerpTime;

    //Ship Preview
    public List<GameObject> wireframePool; 
    public GameObject wireframeObject;
    public Material wireframeMaterial;

    //Waypoint
    public bool waypointActive;
    public string waypointTitle;
    public GameObject waypointGO;

    void Start()
    {
       
    }

    // Update is called once per frame
    void Update()
    {
        HudFunctions.FindMainShip(this);

        // While walking, GroundCompass hides every ship-only widget. Skip these
        // updates so they do not re-enable or reposition the hidden elements.
        if (GroundCharacterController.Instance != null && GroundCharacterController.Instance.IsActive == true)
        {
            return;
        }

        HudFunctions.DisplayShipInfo(this);
        HudFunctions.DisplayFrontRadar(this);
        HudFunctions.DisplayTargets(this);
        HudFunctions.DisplayTargetArrow(this);
        HudFunctions.DisplayEnergyManagementMeters(this);
        HudFunctions.DisplayShipPreview(this);
        HudFunctions.DisplayTargetInformation(this);
        HudFunctions.DisplayCrosshair(this);
        HudFunctions.DisplayInterceptPoint(this);
        HudFunctions.DisplayWaypoint(this);
        HudFunctions.DisplayShieldWarning(this);
    }
}
