using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Third-person orbit rig for <see cref="UnderwaterSwimController"/>.
///
/// The previous version of this file was a *corrector*: SUPER positioned the camera every physics
/// step and this component nudged the result sideways to reframe the body. That contract is gone.
/// SUPERCharacterAIO no longer exists anywhere in the scene, so `GetComponent<SUPERCharacterAIO>()`
/// returned null, `LateUpdate` returned on its first line, and nothing placed the camera at all --
/// which is why the diagnostics showed both enabled cameras sitting at offsetFromPlayer=(0,0,0),
/// i.e. inside the character, at capsule-centre height.
///
/// So this owns the camera pose outright: it accumulates yaw/pitch from the Look action, puts the
/// camera on a boom behind the pivot, and writes world position and rotation in LateUpdate. Writing
/// a world pose every frame means the rig does not care how the camera is parented -- inherited
/// transforms are simply overwritten.
///
/// The yaw here is driven by look input only, never by the player's facing. That matters: the
/// controller turns the body toward camera-relative movement (UnderwaterSwimController :138-148,
/// :195-205), so a rig that derived its yaw from the body would close a feedback loop and spin.
/// </summary>
[DefaultExecutionOrder(100)]
public class ThirdPersonCameraPivot : MonoBehaviour
{
    [Header("Rig")]
    [Tooltip("The camera this rig drives. Assign the SAME Transform as the UnderwaterSwimController's " +
             "Camera Transform -- that field is what movement direction is derived from, so if the two " +
             "disagree you will swim somewhere other than where you are looking.")]
    public Transform cameraTransform;

    [Tooltip("Distance from the pivot back to the camera, in metres.")]
    public float boomLength = 4f;

    [Tooltip("Closest the boom may be pulled in by an obstruction. Stops the camera collapsing into " +
             "the character when something is flush against the pivot.")]
    public float minBoomLength = 0.6f;

    [Tooltip("Height of the point the camera centres on, measured from the capsule centre. The capsule " +
             "is 1.75 tall with its centre at the midpoint, so 0 puts the character's mid-height dead " +
             "centre on screen.")]
    [Range(-1f, 1f)]
    public float thirdPersonPivotHeight = 0f;

    [Tooltip("Slides the camera along its own right axis, in metres. NEGATIVE moves the camera LEFT, " +
             "which pushes the character RIGHT across the screen. Camera-relative, so it stays put on " +
             "screen as the rig orbits.")]
    public float lateralOffset = -1.6f;

    [Tooltip("Slides the camera along its own up axis, in metres. POSITIVE moves the camera UP, which " +
             "pushes the character DOWN the screen -- raise it to show more water ahead.")]
    public float heightOffset = 0.8f;

    [Header("Look")]
    [Tooltip("Action name in the PlayerInput asset. The stock Player map calls it 'Look'.")]
    public string lookActionName = "Look";

    [Tooltip("Degrees per unit of look input.")]
    public float lookSensitivity = 0.12f;

    [Tooltip("Pitch limits in degrees. Swimming wants a wider range than walking, since down is a " +
             "real direction of travel here.")]
    public float minPitch = -75f;
    public float maxPitch = 80f;
    public bool invertPitch = false;

    [Header("Collision")]
    [Tooltip("Pull the camera in when geometry comes between it and the pivot.")]
    public bool guardAgainstClipping = true;

    [Tooltip("Layers the obstruction cast considers. The player's own colliders are always ignored.")]
    public LayerMask obstructionMask = ~0;

    [Tooltip("Keeps the camera this far above the terrain surface. Independent of the controller's own " +
             "terrainClearance, which governs the body -- the camera sits behind and below the pivot " +
             "and so reaches the seabed first.")]
    public float terrainCameraClearance = 0.5f;

    // Matches the radius the old SUPER path used, so framing does not pop relative to the tuning that
    // was done against it.
    private const float ObstructionRadius = 0.5f;
    private static readonly RaycastHit[] HitBuffer = new RaycastHit[8];

    private UnderwaterSwimController swim;
    private InputAction lookAction;
    private float yaw;
    private float pitch;

    private void Awake()
    {
        swim = GetComponent<UnderwaterSwimController>();

        PlayerInput playerInput = GetComponent<PlayerInput>();
        if (playerInput != null && playerInput.actions != null)
        {
            lookAction = playerInput.actions.FindAction(lookActionName, false);
        }

        if (lookAction == null)
        {
            Debug.LogWarning($"{name}: no '{lookActionName}' action found; the camera will follow but not orbit.", this);
        }

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        if (cameraTransform == null)
        {
            Debug.LogError($"{name}: no camera assigned and no Camera.main in the scene.", this);
            enabled = false;
            return;
        }

        // The stray-camera-on-the-player-root trap. If the camera IS the player root then "the camera"
        // and "the thing the camera frames" are the same object and orbiting is meaningless. The scene
        // currently has exactly this -- a Camera component on Character.001 itself -- so say so loudly
        // rather than silently producing a first-person view that looks like a bug.
        if (cameraTransform == transform)
        {
            Debug.LogError($"{name}: the assigned camera is the player root itself, so there is nothing " +
                           "to orbit. Move the Camera onto a child object and assign that instead.", this);
            enabled = false;
            return;
        }

        // Start from wherever the camera already points, so enabling the rig does not snap the view.
        Vector3 euler = cameraTransform.rotation.eulerAngles;
        yaw = euler.y;
        pitch = Mathf.Clamp(NormalizeAngle(euler.x), minPitch, maxPitch);
    }

    private void LateUpdate()
    {
        if (cameraTransform == null) return;

        Vector2 look = lookAction != null ? lookAction.ReadValue<Vector2>() : Vector2.zero;
        yaw += look.x * lookSensitivity;
        pitch += (invertPitch ? look.y : -look.y) * lookSensitivity;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 pivot = transform.position + Vector3.up * thirdPersonPivotHeight;

        // Boom first, then the framing offsets in camera space so "left" stays left on screen however
        // far the rig has orbited. World-space offsets would swing around the character as yaw changed.
        Vector3 target = pivot - (rotation * Vector3.forward) * boomLength;
        target += (rotation * Vector3.right) * lateralOffset + (rotation * Vector3.up) * heightOffset;

        if (guardAgainstClipping)
        {
            target = PullInPastObstructions(pivot, target);
        }

        target = LiftAboveTerrain(target);

        cameraTransform.SetPositionAndRotation(target, rotation);
    }

    private Vector3 PullInPastObstructions(Vector3 pivot, Vector3 target)
    {
        Vector3 toCamera = target - pivot;
        float distance = toCamera.magnitude;
        if (distance <= 1e-4f) return target;

        Vector3 direction = toCamera / distance;

        // Gather every hit rather than taking the first: the cast starts at the capsule centre, so the
        // sphere overlaps the player's own collider, and a plain SphereCast would report that as a
        // zero-distance hit and collapse the boom to nothing every frame.
        int count = Physics.SphereCastNonAlloc(pivot, ObstructionRadius, direction, HitBuffer, distance,
                                               obstructionMask, QueryTriggerInteraction.Ignore);

        float nearest = distance;
        for (int i = 0; i < count; i++)
        {
            Transform hitTransform = HitBuffer[i].transform;
            if (hitTransform == null) continue;
            if (hitTransform == transform || hitTransform.IsChildOf(transform)) continue;

            if (HitBuffer[i].distance < nearest) nearest = HitBuffer[i].distance;
        }

        if (nearest >= distance) return target;

        float pulled = Mathf.Max(nearest * 0.9f, minBoomLength);
        return pivot + direction * pulled;
    }

    private Vector3 LiftAboveTerrain(Vector3 position)
    {
        Terrain terrain = swim != null ? swim.TerrainLimit : null;
        if (terrain == null) return position;

        // Same height convention as the controller and SwimDiagnostics: SampleHeight is relative to
        // the terrain object, so the terrain's own Y has to be added back.
        float groundY = terrain.SampleHeight(position) + terrain.transform.position.y;
        float minimumY = groundY + terrainCameraClearance;
        if (position.y < minimumY) position.y = minimumY;

        return position;
    }

    private static float NormalizeAngle(float degrees)
    {
        degrees %= 360f;
        if (degrees > 180f) degrees -= 360f;
        return degrees;
    }
}
