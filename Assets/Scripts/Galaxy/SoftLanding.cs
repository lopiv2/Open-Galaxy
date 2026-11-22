using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Soft landing system: press 'Z' near the ground on a planet to land the ship.
// The ship descends smoothly, levels with the horizon and stays landed with the
// flight controls locked. Press 'Z' or push the throttle forward to take off.
public class SoftLanding : MonoBehaviour
{
    [Header("Reference")]
    public Ship ship;

    [Header("Landing Settings")]
    public float landingAltitude = 2f; // Final height above the ground (meters)
    public float descentSpeed = 15f; // Vertical descent speed when landing (m/s)
    public float maxLandingAltitude = 250f; // Highest altitude (AGL) where Z is accepted
    public float markerRange = 250f; // Marker / hint appears below this altitude

    [Header("Visual Feedback")]
    public ParticleSystem landingEffect;

    private FlightControlSystem_Small flight;
    private Rigidbody shipRigidbody;
    private GameObject landingMarkerGO;
    private Coroutine landingRoutine;
    private bool landing = false; // Descent in progress
    private bool landed = false; // Resting on the ground
    private bool takeoffArmed = false;
    private bool hintShown = false;
    private bool previousControlLock = false;
    private bool previousDocking = false;
    private bool previousInvincible = false;
    private bool wingsClosedByLanding = false;
    private float landingStartEnginePitch = 1f;

    // Foot mode freezes the landed state instead of disabling this component:
    // OnDisable() would call ResetLandingState(), clear GalaxyPlanetEntry.IsLanded
    // and make the MinClearance clamp pop the ship back into the air.
    [HideInInspector] public bool suspended = false;

    private void Start()
    {
        EnsureReferences();

        if (landingEffect != null)
        {
            landingEffect.gameObject.SetActive(false);
        }

        CreateLandingMarker();
    }

    private void EnsureReferences()
    {
        if (ship == null)
        {
            ship = GetComponent<Ship>();
        }

        if (flight == null)
        {
            flight = GetComponent<FlightControlSystem_Small>();
        }

        if (shipRigidbody == null)
        {
            // The rigidbody is added by PrepareShip() on the first Update.
            shipRigidbody = GetComponent<Rigidbody>();
        }
    }

    private void CreateLandingMarker()
    {
        // Simple plane mesh used as a visual landing spot (placed on the ground).
        landingMarkerGO = new GameObject("SoftLandingMarker");

        MeshFilter filter = landingMarkerGO.AddComponent<MeshFilter>();
        Mesh planeMesh = new Mesh();
        planeMesh.name = "LandingMarkerMesh";
        planeMesh.vertices = new Vector3[4] {
            new Vector3(-5f, 0, -5f),
            new Vector3(5f, 0, -5f),
            new Vector3(5f, 0, 5f),
            new Vector3(-5f, 0, 5f)
        };
        planeMesh.triangles = new int[6] {
            1, 0, 2,
            1, 2, 3
        };
        planeMesh.RecalculateNormals();
        planeMesh.RecalculateBounds();
        filter.mesh = planeMesh;

        MeshRenderer renderer = landingMarkerGO.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader != null)
        {
            Material transparentMat = new Material(shader);
            transparentMat.color = new Color(0.8f, 1f, 0.6f, 0.7f);
            transparentMat.SetFloat("_Cull", 0f);
            transparentMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            transparentMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            transparentMat.SetInt("_ZWrite", 0);
            transparentMat.DisableKeyword("_ALPHATEST_ON");
            transparentMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            renderer.sharedMaterial = transparentMat;
        }

        landingMarkerGO.SetActive(false);
    }

    private void Update()
    {
        // The player is on foot: keep the ship landed and ignore all input.
        if (suspended == true)
        {
            return;
        }

        // Only active while flying inside a planet's atmosphere.
        if (GalaxyPlanetEntry.IsOnPlanet == false)
        {
            if (landed == true || landing == true)
            {
                ResetLandingState();
            }

            return;
        }

        Scene scene = SceneFunctions.GetScene();

        if (scene == null || scene.mainShip != this.gameObject)
        {
            return;
        }

        EnsureReferences();

        // Prevent AI ships from attempting to land
        if (ship == null || ship.isAI == true)
        {
            return;
        }

        // If we're already in hyperspace, don't allow landing
        if (ship.jumpingToHyperspace || ship.exitingHyperspace)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;

        // The descent coroutine drives the ship; only refresh the marker.
        if (landing == true)
        {
            UpdateLandingMarker();
            return;
        }

        if (landed == true)
        {
            UpdateLandedState(keyboard);
            UpdateLandingMarker();
            return;
        }

        // Check for landing key press
        if (keyboard != null && keyboard.zKey.wasPressedThisFrame == true)
        {
            AttemptSoftLanding();
            return;
        }

        UpdateLandingMarker();
    }

    private void AttemptSoftLanding()
    {
        Vector3 currentPos = transform.position;
        float groundHeight = SampleGroundHeight(currentPos);

        if (float.IsNaN(groundHeight))
        {
            HudFunctions.AddToMissionLog("Cannot land - no ground detected below.");
            return;
        }

        float currentAltitude = currentPos.y - groundHeight;

        if (currentAltitude <= landingAltitude + 1f)
        {
            HudFunctions.AddToMissionLog("Already on the ground.");
            return;
        }

        if (currentAltitude > maxLandingAltitude)
        {
            HudFunctions.AddToMissionLog("Too high to land - descend below " + Mathf.RoundToInt(maxLandingAltitude) + "m.");
            return;
        }

        landingRoutine = StartCoroutine(LandSequence(groundHeight));
    }

    private IEnumerator LandSequence(float groundHeight)
    {
        landing = true;
        landed = false;
        takeoffArmed = false;

        // Skip the planet's minimum clearance clamp while we settle on the ground.
        GalaxyPlanetEntry.IsLanded = true;

        previousControlLock = flight != null && flight.controlLock;
        previousDocking = flight != null && flight.docking;
        previousInvincible = ship != null && ship.damageSystem != null && ship.damageSystem.invincible;

        if (flight != null)
        {
            // MoveShip() ignores thrust while docking, so this freezes the ship.
            flight.docking = true;
            flight.controlLock = true;
            flight.thrustSpeed = 0f;
            flight.thrustInput = 0f;
        }

        StopShipMotion();

        if (ship != null && ship.damageSystem != null)
        {
            ship.damageSystem.invincible = true;
        }

        // Fold the S-foils (if the ship has them) while it settles down.
        FoldSFoils();

        // Remember the current engine tone so it can wind down to silence
        // together with the descent instead of snapping off when thrust hits zero.
        landingStartEnginePitch = 1f;

        if (flight != null && flight.engineAudioSource != null)
        {
            landingStartEnginePitch = Mathf.Max(0f, flight.engineAudioSource.pitch);
        }
        else if (flight != null && ship != null && ship.speedRating > 0f)
        {
            landingStartEnginePitch = Mathf.Clamp01(flight.thrustSpeed / ship.speedRating);
        }

        if (flight != null)
        {
            flight.engineAudioFade = 1f;
            flight.engineAudioPitchOverride = landingStartEnginePitch;
        }

        Vector3 start = transform.position;
        Vector3 end = new Vector3(start.x, groundHeight + landingAltitude, start.z);

        // Level the ship with the horizon while it settles (keeps current heading).
        Quaternion startRotation = transform.rotation;
        Vector3 heading = Vector3.ProjectOnPlane(startRotation * Vector3.forward, Vector3.up);
        Quaternion endRotation = heading.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(heading.normalized, Vector3.up)
            : startRotation;

        float distance = Vector3.Distance(start, end);
        float duration = Mathf.Clamp(distance / Mathf.Max(descentSpeed, 1f), 0.75f, 8f);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));

            transform.position = Vector3.Lerp(start, end, t);
            transform.rotation = Quaternion.Slerp(startRotation, endRotation, t);
            StopShipMotion();

            // Engine pitch and volume follow the same curve as the descent:
            // by the time the ship touches down the engine is completely off.
            if (flight != null)
            {
                float engineLevel = 1f - t;
                flight.engineAudioFade = engineLevel;
                flight.engineAudioPitchOverride = landingStartEnginePitch * engineLevel;
            }

            yield return null;
        }

        transform.position = end;
        transform.rotation = endRotation;
        StopShipMotion();

        // Fully shut the engine down while landed.
        if (flight != null)
        {
            flight.engineAudioFade = 0f;
            flight.engineAudioPitchOverride = 0f;
        }

        if (landingEffect != null)
        {
            landingEffect.gameObject.SetActive(true);
            landingEffect.Play();
        }

        if (ship != null && ship.damageSystem != null)
        {
            ship.damageSystem.invincible = previousInvincible;
        }

        landing = false;
        landed = true;
        landingRoutine = null;

        HudFunctions.AddToMissionLog("LANDED - press Z or throttle up to take off.");
    }

    private void UpdateLandedState(Keyboard keyboard)
    {
        if (flight != null)
        {
            flight.thrustSpeed = 0f;
        }

        // Z always lifts off. The throttle also works, but only after the
        // player releases it once: holding W through the descent must not
        // bounce the ship straight back into the air.
        bool throttlePressed = ship != null && ship.ogInput != null && ship.ogInput.thrustInput > 0.1f;

        if (throttlePressed == false)
        {
            takeoffArmed = true;
        }

        bool takeOff = keyboard != null && keyboard.zKey.wasPressedThisFrame == true;

        if (takeoffArmed == true && throttlePressed == true)
        {
            takeOff = true;
        }

        if (takeOff == true)
        {
            TakeOff();
        }
    }

    private void TakeOff()
    {
        landed = false;
        landing = false;
        takeoffArmed = false;
        GalaxyPlanetEntry.IsLanded = false;

        if (flight != null)
        {
            flight.docking = previousDocking;
            flight.controlLock = previousControlLock;
        }

        // Lift clear of the planet's minimum flight clearance so the clamp
        // doesn't snap the ship upwards a frame later.
        Vector3 position = transform.position;
        float groundHeight = SampleGroundHeight(position);

        if (float.IsNaN(groundHeight) == false)
        {
            position.y = Mathf.Max(position.y, groundHeight + GalaxyPlanetEntry.MinClearance);
            transform.position = position;
        }

        StopShipMotion();

        // Open the S-foils again before lifting off.
        OpenSFoils();
        RestoreEngineAudio();

        if (landingMarkerGO != null)
        {
            landingMarkerGO.SetActive(false);
        }

        hintShown = false;

        HudFunctions.AddToMissionLog("LIFTOFF!");
    }

    private void ResetLandingState()
    {
        if (landingRoutine != null)
        {
            StopCoroutine(landingRoutine);
            landingRoutine = null;
        }

        landing = false;
        landed = false;
        takeoffArmed = false;
        GalaxyPlanetEntry.IsLanded = false;

        if (flight != null)
        {
            flight.docking = previousDocking;
            flight.controlLock = previousControlLock;
        }

        if (ship != null && ship.damageSystem != null)
        {
            ship.damageSystem.invincible = previousInvincible;
        }

        OpenSFoils();
        RestoreEngineAudio();

        if (landingMarkerGO != null)
        {
            landingMarkerGO.SetActive(false);
        }

        hintShown = false;
    }

    // Folds the S-foils while landed (ships without movable wings ignore this).
    private void FoldSFoils()
    {
        if (ship == null || ship.wingSystems == null)
        {
            return;
        }

        if (WingSystemFunctions.HasMovableWings(ship.wingSystems) == false)
        {
            return;
        }

        wingsClosedByLanding = true;

        if (ship.wingSystems.wingsOpen == true)
        {
            WingSystemFunctions.CloseWings(ship.wingSystems);
        }
    }

    // Opens the S-foils again on takeoff / aborted landing.
    private void OpenSFoils()
    {
        if (wingsClosedByLanding == false)
        {
            return;
        }

        wingsClosedByLanding = false;

        if (ship == null || ship.wingSystems == null)
        {
            return;
        }

        if (WingSystemFunctions.HasMovableWings(ship.wingSystems) == false)
        {
            return;
        }

        if (ship.wingSystems.wingsOpen == false)
        {
            WingSystemFunctions.OpenWings(ship.wingSystems);
        }
    }

    // Hands engine sound control back to the normal speed-based behaviour.
    private void RestoreEngineAudio()
    {
        if (flight == null)
        {
            return;
        }

        flight.engineAudioFade = 1f;
        flight.engineAudioPitchOverride = -1f;
    }

    private void StopShipMotion()
    {
        if (shipRigidbody == null)
        {
            shipRigidbody = GetComponent<Rigidbody>();
        }

        if (shipRigidbody != null)
        {
            shipRigidbody.linearVelocity = Vector3.zero;
            shipRigidbody.angularVelocity = Vector3.zero;
        }
    }

    private void UpdateLandingMarker()
    {
        if (landingMarkerGO == null)
        {
            return;
        }

        Scene scene = SceneFunctions.GetScene();

        if (scene == null)
        {
            landingMarkerGO.SetActive(false);
            return;
        }

        // Parent the marker to the scene anchor so it stays flat on the ground
        // (the ship is scaled/rotated, which would distort the plane).
        if (landingMarkerGO.transform.parent != scene.transform)
        {
            landingMarkerGO.transform.SetParent(scene.transform, true);
        }

        Vector3 shipPos = transform.position;
        float groundHeight = SampleGroundHeight(shipPos);

        if (float.IsNaN(groundHeight))
        {
            landingMarkerGO.SetActive(false);
            hintShown = false;
            return;
        }

        float currentAltitude = shipPos.y - groundHeight;
        bool inRange = currentAltitude <= markerRange && currentAltitude > landingAltitude;

        landingMarkerGO.SetActive(inRange == true || landing == true || landed == true);

        if (inRange == false)
        {
            hintShown = false;
            return;
        }

        landingMarkerGO.transform.position = new Vector3(shipPos.x, groundHeight + 0.25f, shipPos.z);
        landingMarkerGO.transform.rotation = Quaternion.identity;

        if (hintShown == false)
        {
            hintShown = true;
            HudFunctions.AddToMissionLog("Press Z to land.");
        }
    }

    // Sample terrain height using raycast. Only terrain tiles ("Tile_x_y") are
    // valid ground: the ray starts above the ship and would otherwise hit the
    // ship's own collider (or nearby traffic) and report zero altitude.
    private static float SampleGroundHeight(Vector3 worldPosition)
    {
        Vector3 origin = new Vector3(worldPosition.x, worldPosition.y + 400f, worldPosition.z);
        float rayLength = origin.y + 2000f;

        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, rayLength, ~0, QueryTriggerInteraction.Ignore);
        float bestHeight = float.NegativeInfinity;

        foreach (RaycastHit hitInfo in hits)
        {
            if (hitInfo.transform == null || hitInfo.transform.name.StartsWith("Tile_") == false)
            {
                continue;
            }

            if (hitInfo.point.y > bestHeight)
            {
                bestHeight = hitInfo.point.y;
            }
        }

        if (float.IsNegativeInfinity(bestHeight))
        {
            return float.NaN;
        }

        return bestHeight;
    }

    private void OnDisable()
    {
        // Do not undo the landing just because foot mode suspended us.
        if (suspended == true)
        {
            return;
        }

        if (landed == true || landing == true)
        {
            ResetLandingState();
        }
    }

    private void OnDestroy()
    {
        // Cleanup
        if (landingMarkerGO != null)
        {
            GameObject.Destroy(landingMarkerGO);
        }

        if (landed == true || landing == true)
        {
            GalaxyPlanetEntry.IsLanded = false;
        }

        RestoreEngineAudio();
    }
}
