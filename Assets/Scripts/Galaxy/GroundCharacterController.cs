using UnityEngine;
using UnityEngine.InputSystem;

// Foot mode prototype: press C near a landed ship to place rebel_pilot on the
// terrain, move it with WASD (Shift to run) and follow it with the game camera.
// Press C again (or take off) to return to the ship.
public class GroundCharacterController : MonoBehaviour
{
    public static GroundCharacterController Instance { get; private set; }

    [Header("Character")]
    public string readyroomResource = "objects/readyrooms/readyroom_white";
    public string characterChildName = "rebel_pilot";
    public float spawnDistanceFromShip = 8f;
    public float groundOffset = 0.05f;

    [Header("Movement")]
    public float walkSpeed = 3.5f;
    public float runSpeed = 7f;
    public float rotationSpeed = 12f;

    [Header("Third-person camera")]
    public float cameraDistance = 2.2f;
    public float cameraHeight = 0.35f;
    public float cameraShoulder = 0.25f;
    public float cameraLookHeight = 1.15f;
    public float cameraSmooth = 12f;
    public float mouseSensitivity = 0.15f;
    public float minPitch = -10f;
    public float maxPitch = 50f;

    private GameObject character;
    private Animator animator;
    private Transform cameraTransform;
    private FlightControlSystem_Small lockedFlight;
    private SoftLanding disabledLanding;
    private OGCamera disabledOGCamera;
    private AudioSource mutedEngine;
    private float engineVolumeBefore;
    private Vector3 cameraLocalPosition;
    private Quaternion cameraLocalRotation;
    private bool ogCameraWasEnabled;
    private bool flightWasEnabled;
    private bool flightLockBefore;
    private bool active;
    private bool hintShown;
    private bool cameraSnap;
    private int landingResumeFrames;
    private float cameraYaw;
    private float cameraPitch = 12f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null)
        {
            return;
        }

        new GameObject("GroundCharacterController").AddComponent<GroundCharacterController>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        // Never leave the ship locked if the scene unloads while on foot.
        OGInputFunctions.shipInputLocked = false;
        RestoreShipAudio();

        if (disabledLanding != null)
        {
            disabledLanding.suspended = false;
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        // Let a frame pass before SoftLanding listens again, so the Z that
        // returned us to the ship is not reused as a takeoff command.
        if (disabledLanding != null && landingResumeFrames > 0)
        {
            landingResumeFrames--;

            if (landingResumeFrames == 0)
            {
                disabledLanding.suspended = false;
                disabledLanding = null;
            }
        }

        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
        {
            return;
        }

        // Ctrl+C belongs to the mission editor.
        if (keyboard.ctrlKey.isPressed == true)
        {
            return;
        }

        if (keyboard.cKey.wasPressedThisFrame == true)
        {
            if (active == true)
            {
                ExitFootMode();
            }
            else
            {
                TryEnterFootMode();
            }

            return;
        }

        if (active == false)
        {
            ShowHintIfNeeded();
            return;
        }

        // SoftLanding is disabled while on foot, so release it first. The next
        // Z press belongs to the ship and performs the actual takeoff.
        if (keyboard.zKey.wasPressedThisFrame == true)
        {
            ExitFootMode();
            Log("Press Z again to take off.");
            return;
        }

        Scene scene = SceneFunctions.GetScene();

        if (GalaxyPlanetEntry.IsOnPlanet == false || scene == null || scene.mainShip == null)
        {
            ExitFootMode();
            return;
        }

        UpdateCameraInput();
        MoveCharacter(keyboard);
    }

    private void LateUpdate()
    {
        if (active == true && character != null)
        {
            FollowCamera();
        }
    }

    private void TryEnterFootMode()
    {
        if (GalaxyPlanetEntry.IsOnPlanet == false)
        {
            Log("Foot mode is only available on a planet surface.");
            return;
        }

        Scene scene = SceneFunctions.GetScene();

        if (scene == null || scene.mainShip == null)
        {
            return;
        }

        Vector3 shipPos = scene.mainShip.transform.position;
        float shipGround = SampleGroundHeight(shipPos);

        if (float.IsNaN(shipGround) == true)
        {
            Log("No terrain below the ship.");
            return;
        }

        if (GalaxyPlanetEntry.IsLanded == false && IsNearGround(shipPos) == false)
        {
            Log("Land with Z before going on foot.");
            return;
        }

        GameObject readyroom = Resources.Load<GameObject>(readyroomResource);

        if (readyroom == null)
        {
            Log("Could not load the character prefab.");
            return;
        }

        Transform template = readyroom.transform.Find(characterChildName);

        if (template == null)
        {
            Log("Could not find the character in the readyroom.");
            return;
        }

        Vector3 spawn = shipPos + scene.mainShip.transform.right * spawnDistanceFromShip;
        float ground = SampleGroundHeight(spawn);

        if (float.IsNaN(ground) == true)
        {
            ground = shipGround;
        }

        character = Instantiate(template.gameObject, new Vector3(spawn.x, ground + groundOffset, spawn.z), Quaternion.identity);
        character.name = "GroundCharacter_" + characterChildName;
        character.SetActive(true);

        animator = character.GetComponent<Animator>();
        cameraYaw = scene.mainShip.transform.eulerAngles.y;
        cameraPitch = 12f;
        cameraSnap = true;
        character.transform.rotation = Quaternion.Euler(0f, cameraYaw, 0f);

        // controlLock only stops rotation: disable flight so WASD cannot thrust.
        lockedFlight = scene.mainShip.GetComponent<FlightControlSystem_Small>();

        if (lockedFlight != null)
        {
            flightWasEnabled = lockedFlight.enabled;
            flightLockBefore = lockedFlight.controlLock;
            lockedFlight.enabled = false;
            lockedFlight.controlLock = true;
            lockedFlight.thrustSpeed = 0f;
            lockedFlight.thrustInput = 0f;
        }

        Rigidbody shipBody = scene.mainShip.GetComponent<Rigidbody>();

        if (shipBody != null)
        {
            shipBody.linearVelocity = Vector3.zero;
            shipBody.angularVelocity = Vector3.zero;
        }

        // Keep the ship landed. Disabling SoftLanding would run OnDisable() and
        // clear IsLanded, so suspend it instead.
        disabledLanding = scene.mainShip.GetComponent<SoftLanding>();
        landingResumeFrames = 0;

        if (disabledLanding != null)
        {
            disabledLanding.suspended = true;
        }

        // OGInput is shared by weapons, power, targeting and countermeasures.
        // Lock it so none of those ship controls work while on foot.
        Ship playerShip = scene.mainShip.GetComponent<Ship>();
        OGInputFunctions.shipInputLocked = true;
        OGInputFunctions.ClearShipInput(playerShip != null && playerShip.ogInput != null
            ? playerShip.ogInput
            : OGInputFunctions.GetOGInput());
        SilenceShipAudio(playerShip);

        // Stop OGCamera while on foot: its FixedUpdate keeps moving the camera
        // root towards the ship, which fought with this script's LateUpdate.
        disabledOGCamera = OGCameraFunctions.GetOGCamera();

        if (disabledOGCamera != null)
        {
            ogCameraWasEnabled = disabledOGCamera.enabled;
            disabledOGCamera.enabled = false;
        }

        cameraTransform = disabledOGCamera == null || disabledOGCamera.mainCamera == null
            ? null
            : disabledOGCamera.mainCamera.transform;

        if (cameraTransform != null)
        {
            cameraLocalPosition = cameraTransform.localPosition;
            cameraLocalRotation = cameraTransform.localRotation;
        }

        active = true;
        hintShown = false;
        Log("On foot: WASD to move, Shift to run, C to return to the ship.");
    }

    private void ExitFootMode()
    {
        if (active == false)
        {
            return;
        }

        active = false;

        if (character != null)
        {
            Destroy(character);
        }

        character = null;
        animator = null;

        // Put the camera back where OGCamera left it before re-enabling it.
        if (cameraTransform != null)
        {
            cameraTransform.localPosition = cameraLocalPosition;
            cameraTransform.localRotation = cameraLocalRotation;
        }

        if (disabledOGCamera != null)
        {
            disabledOGCamera.enabled = ogCameraWasEnabled;
        }

        cameraTransform = null;
        disabledOGCamera = null;

        if (lockedFlight != null)
        {
            lockedFlight.controlLock = flightLockBefore;
            lockedFlight.enabled = flightWasEnabled;
        }

        if (disabledLanding != null)
        {
            landingResumeFrames = 2;
        }

        OGInputFunctions.shipInputLocked = false;
        RestoreShipAudio();

        lockedFlight = null;
        Log("Back in the ship.");
    }

    // The ship's engine loop keeps playing while its flight script is disabled,
    // and a wings_close one-shot can still be fading out when the player gets
    // out. Silence both while walking and restore the engine volume on return.
    private void SilenceShipAudio(Ship ship)
    {
        mutedEngine = ship != null && ship.flightControlSystem_Small != null
            ? ship.flightControlSystem_Small.engineAudioSource
            : null;

        if (mutedEngine != null)
        {
            engineVolumeBefore = mutedEngine.volume;
            mutedEngine.volume = 0f;
        }

        Audio audioManager = AudioFunctions.GetAudioManager();

        if (audioManager == null || audioManager.audioSources == null)
        {
            return;
        }

        foreach (AudioSource source in audioManager.audioSources)
        {
            if (source == null || source.isPlaying == false || source.clip == null)
            {
                continue;
            }

            if (source.clip.name == "wings_close" || source.clip.name == "wings_open")
            {
                source.Stop();
            }
        }
    }

    private void RestoreShipAudio()
    {
        if (mutedEngine != null)
        {
            mutedEngine.volume = engineVolumeBefore;
        }

        mutedEngine = null;
    }

    private void UpdateCameraInput()
    {
        // Right mouse drag orbits the camera; the right stick does the same on a pad.
        Mouse mouse = Mouse.current;

        if (mouse != null && mouse.rightButton.isPressed == true)
        {
            Vector2 delta = mouse.delta.ReadValue();
            cameraYaw += delta.x * mouseSensitivity;
            cameraPitch -= delta.y * mouseSensitivity;
        }

        Gamepad gamepad = Gamepad.current;

        if (gamepad != null)
        {
            Vector2 look = gamepad.rightStick.ReadValue();

            if (look.sqrMagnitude > 0.04f)
            {
                cameraYaw += look.x * 120f * Time.deltaTime;
                cameraPitch -= look.y * 90f * Time.deltaTime;
            }
        }

        cameraPitch = Mathf.Clamp(cameraPitch, minPitch, maxPitch);
    }

    private void MoveCharacter(Keyboard keyboard)
    {
        float horizontal = 0f;
        float vertical = 0f;

        if (keyboard.dKey.isPressed == true) horizontal += 1f;
        if (keyboard.aKey.isPressed == true) horizontal -= 1f;
        if (keyboard.wKey.isPressed == true) vertical += 1f;
        if (keyboard.sKey.isPressed == true) vertical -= 1f;

        Gamepad gamepad = Gamepad.current;

        if (gamepad != null)
        {
            Vector2 stick = gamepad.leftStick.ReadValue();

            if (stick.sqrMagnitude > 0.04f)
            {
                horizontal += stick.x;
                vertical += stick.y;
            }
        }

        Vector3 localMove = new Vector3(horizontal, 0f, vertical);
        float input = Mathf.Clamp01(localMove.magnitude);
        bool running = keyboard.leftShiftKey.isPressed == true || keyboard.rightShiftKey.isPressed == true;

        if (input > 0.01f)
        {
            Vector3 worldMove = Quaternion.Euler(0f, cameraYaw, 0f) * localMove.normalized;
            float speed = running == true ? runSpeed : walkSpeed;

            character.transform.position += worldMove * speed * Time.deltaTime;

            float targetYaw = Mathf.Atan2(worldMove.x, worldMove.z) * Mathf.Rad2Deg;
            Quaternion targetRotation = Quaternion.Euler(0f, targetYaw, 0f);
            character.transform.rotation = Quaternion.RotateTowards(
                character.transform.rotation,
                targetRotation,
                rotationSpeed * 100f * Time.deltaTime);

            StickToGround();
        }

        if (animator != null)
        {
            animator.SetFloat("Speed", input, 0.15f, Time.deltaTime);
            animator.SetBool("IsRunning", running == true && input > 0.01f);
        }
    }

    private void StickToGround()
    {
        float ground = SampleGroundHeight(character.transform.position);

        if (float.IsNaN(ground) == true)
        {
            return;
        }

        Vector3 pos = character.transform.position;
        pos.y = ground + groundOffset;
        character.transform.position = pos;
    }

    private void FollowCamera()
    {
        if (cameraTransform == null)
        {
            OGCamera ogCamera = OGCameraFunctions.GetOGCamera();
            cameraTransform = ogCamera == null || ogCamera.mainCamera == null ? null : ogCamera.mainCamera.transform;

            if (cameraTransform == null)
            {
                return;
            }
        }

        Vector3 focus = character.transform.position + Vector3.up * cameraLookHeight;
        Vector3 offset = Quaternion.Euler(cameraPitch, cameraYaw, 0f) * new Vector3(cameraShoulder, cameraHeight, -cameraDistance);
        Vector3 desired = focus + offset;

        // Never let the camera sink below the terrain when orbiting downwards.
        float ground = SampleGroundHeight(desired);

        if (float.IsNaN(ground) == false && desired.y < ground + 0.3f)
        {
            desired.y = ground + 0.3f;
        }

        if (cameraSnap == true)
        {
            cameraTransform.position = desired;
            cameraSnap = false;
        }
        else
        {
            float t = 1f - Mathf.Exp(-cameraSmooth * Time.deltaTime);
            cameraTransform.position = Vector3.Lerp(cameraTransform.position, desired, t);
        }

        cameraTransform.rotation = Quaternion.LookRotation(focus - cameraTransform.position, Vector3.up);
    }

    private bool IsNearGround(Vector3 worldPosition)
    {
        float ground = SampleGroundHeight(worldPosition);

        return float.IsNaN(ground) == false && worldPosition.y - ground <= 20f;
    }

    private void ShowHintIfNeeded()
    {
        if (hintShown == true || GalaxyPlanetEntry.IsOnPlanet == false || GalaxyPlanetEntry.IsLanded == false)
        {
            return;
        }

        hintShown = true;
        Log("Press C to get out of the ship.");
    }

    private static void Log(string message)
    {
        try
        {
            HudFunctions.AddToMissionLog(message);
        }
        catch (System.Exception)
        {
            // The HUD does not exist in every scene (menus, tests).
            Debug.Log("[GroundCharacterController] " + message);
        }
    }

    // Same rule as SoftLanding: only terrain tiles count as walkable ground.
    private static float SampleGroundHeight(Vector3 worldPosition)
    {
        Vector3 origin = new Vector3(worldPosition.x, worldPosition.y + 400f, worldPosition.z);
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, origin.y + 2000f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;

        foreach (RaycastHit hit in hits)
        {
            if (hit.transform == null || hit.transform.name.StartsWith("Tile_") == false)
            {
                continue;
            }

            if (hit.point.y > best)
            {
                best = hit.point.y;
            }
        }

        return float.IsNegativeInfinity(best) ? float.NaN : best;
    }
}
