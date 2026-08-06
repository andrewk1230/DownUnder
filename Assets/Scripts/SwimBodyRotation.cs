using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Turns the body to face the direction you are actually swimming, every frame, everywhere.
///
/// UnderwaterSwimController already has a rotation path, but it is gated twice over: it only runs
/// inside that component's Update, and its strength is `rotationDamping * Time.deltaTime` fed to a
/// Slerp -- which is a no-op at damping 0 and framerate-dependent at every other value. The result
/// was a body that only appeared to turn when something else happened to be supplying it velocity,
/// such as the terrain clamp pushing it along a surface normal.
///
/// This runs in LateUpdate, so it writes after the controller's Update and wins the frame outright
/// regardless of what rotationDamping is set to. RotateTowards with a degrees-per-second rate is
/// framerate-independent and gives a turn rate you can actually reason about.
///
/// Facing the *movement* direction rather than the camera's is deliberate: the controller scales
/// horizontal speed by how well the body aligns with where you are pushing
/// (UnderwaterSwimController :169-179), so keeping the body pointed at the input keeps that factor
/// near 1 and strafing at full speed. Locking the body to the camera instead would drive that factor
/// to zero whenever you pressed A or D.
/// </summary>
[DefaultExecutionOrder(50)]
public class SwimBodyRotation : MonoBehaviour
{
    [Tooltip("Turn rate in degrees per second. Frame-rate independent, unlike the controller's own " +
             "rotationDamping. 540 turns the body a half-circle in a third of a second.")]
    public float turnSpeed = 540f;

    [Tooltip("Frame of reference for the input. Assign the same camera the swim controller uses. " +
             "Left empty, Camera.main is used.")]
    public Transform cameraTransform;

    [Tooltip("Action name in the PlayerInput asset. The stock Player map calls it 'Move'.")]
    public string moveActionName = "Move";

    [Tooltip("When there is no movement input, keep turning to line the body up with the camera. " +
             "Off by default -- with it on you cannot hold a heading while looking around.")]
    public bool faceCameraWhenIdle = false;

    private InputAction moveAction;

    private void Awake()
    {
        PlayerInput playerInput = GetComponent<PlayerInput>();
        if (playerInput != null && playerInput.actions != null)
        {
            moveAction = playerInput.actions.FindAction(moveActionName, false);
        }

        if (moveAction == null)
        {
            Debug.LogWarning($"{name}: no '{moveActionName}' action found; the body will not turn.", this);
            enabled = false;
            return;
        }

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }
    }

    private void LateUpdate()
    {
        Transform reference = cameraTransform != null ? cameraTransform : transform;

        Vector3 forward = reference.forward;
        Vector3 right = reference.right;
        forward.y = 0f;
        right.y = 0f;

        // Looking straight down or straight up flattens the reference axes to nothing, which would
        // otherwise normalize into a garbage direction and snap the body somewhere arbitrary.
        if (forward.sqrMagnitude < 1e-6f || right.sqrMagnitude < 1e-6f) return;

        forward.Normalize();
        right.Normalize();

        Vector2 input = moveAction.ReadValue<Vector2>();
        Vector3 desired = forward * input.y + right * input.x;
        desired.y = 0f;

        if (desired.sqrMagnitude < 1e-4f)
        {
            if (!faceCameraWhenIdle) return;
            desired = forward;
        }

        Quaternion target = Quaternion.LookRotation(desired.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
    }
}
