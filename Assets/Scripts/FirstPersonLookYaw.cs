using SUPERCharacter;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Owns body yaw in first person, and only in first person.
///
/// SUPER applies first-person yaw to the Rigidbody rather than the transform
/// (SUPERCharacterAIO :623, inside RotateView's _1stPerson branch):
///
///     p_Rigidbody.MoveRotation(Quaternion.Euler(Vector3.up*targetAngles.y));
///
/// and re-seeds itself from p_Rigidbody.rotation.eulerAngles.y on the following frame. A direct
/// `transform.rotation = ...` assignment discards a kinematic Rigidbody's pending MoveRotation, and
/// UnderwaterSwimController :201 does exactly that every Update whenever desiredFacing is non-zero.
/// The queued yaw is thrown away before physics applies it, SUPER reads back the unchanged angle next
/// frame, and the view never accumulates any turn -- looking left and right does nothing.
///
/// Pitch is unaffected and stays SUPER's: it goes to playerCamera.transform.localEulerAngles.x, which
/// nothing else in the scene writes. This component deliberately never touches pitch.
///
/// Rather than try to win an ordering race against two other writers, this runs in LateUpdate, after
/// every Update-phase writer has had its turn, and sets the body yaw outright. Whatever stomped the
/// Rigidbody earlier in the frame no longer matters, because this is the last write.
///
/// Third person is untouched: LateUpdate returns on its first line unless the perspective is first
/// person, where SwimBodyRotation and SUPER's own third-person path keep working exactly as before.
/// </summary>
[DefaultExecutionOrder(150)]
public class FirstPersonLookYaw : MonoBehaviour
{
    [Tooltip("Action name in the PlayerInput asset. The stock Player map calls it 'Look'.")]
    public string lookActionName = "Look";

    [Tooltip("Degrees per unit of look input. Matches ThirdPersonCameraPivot's default so the two " +
             "perspectives turn at the same rate.")]
    public float lookSensitivity = 0.12f;

    [Tooltip("Invert horizontal look.")]
    public bool invertYaw = false;

    private SUPERCharacterAIO super;
    private InputAction lookAction;
    private float yaw;
    private bool wasFirstPerson;

    private void Awake()
    {
        super = GetComponent<SUPERCharacterAIO>();
        if (super == null)
        {
            Debug.LogWarning($"{name}: no SUPERCharacterAIO, so there is no perspective to read. " +
                             "Disabling rather than guessing -- this must never run in third person.", this);
            enabled = false;
            return;
        }

        PlayerInput playerInput = GetComponent<PlayerInput>();
        if (playerInput != null && playerInput.actions != null)
        {
            lookAction = playerInput.actions.FindAction(lookActionName, false);
        }

        if (lookAction == null)
        {
            Debug.LogWarning($"{name}: no '{lookActionName}' action found; first-person yaw stays broken.", this);
            enabled = false;
            return;
        }

        yaw = transform.eulerAngles.y;
    }

    private void LateUpdate()
    {
        if (super.cameraPerspective != PerspectiveModes._1stPerson)
        {
            wasFirstPerson = false;
            return;
        }

        // Adopt whatever heading third person left the body on, so switching in does not snap the view.
        if (!wasFirstPerson)
        {
            wasFirstPerson = true;
            yaw = transform.eulerAngles.y;
        }

        Vector2 look = lookAction.ReadValue<Vector2>();
        yaw += (invertYaw ? -look.x : look.x) * lookSensitivity;

        // Yaw only. Pitch belongs to SUPER, which writes it to the camera's local X -- rewriting the
        // whole body rotation here would flatten the camera's inherited pitch to zero every frame.
        Vector3 euler = transform.eulerAngles;
        transform.rotation = Quaternion.Euler(euler.x, yaw, euler.z);
    }
}
