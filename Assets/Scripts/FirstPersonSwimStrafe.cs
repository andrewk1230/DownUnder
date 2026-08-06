using System.Reflection;
using SUPERCharacter;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Restores sideways movement in first person, and only in first person.
///
/// UnderwaterSwimController scales horizontal velocity by how well the body already faces the
/// direction you are pushing (UnderwaterSwimController :169-179):
///
///     alignmentFactor = Clamp01((Dot(transform.forward, planarDir) - turnAlignmentThreshold) / (1 - turnAlignmentThreshold))
///
/// In third person SwimBodyRotation turns the body into the input direction, so that factor stays
/// near 1 and strafing works. First person cannot do that: SUPER pins the body yaw to the camera yaw
/// every frame (SUPERCharacterAIO :689), so pressing A or D produces a planarDir exactly
/// perpendicular to transform.forward, Dot lands at ~0, which is under the scene's
/// turnAlignmentThreshold of 0.103, and the factor clamps to zero. Forward and back survive because
/// they stay aligned; sideways is deleted outright.
///
/// The gate lives in the swim package, which is a git dependency (com.budorf.underwater-swim) and so
/// cannot be edited without forking it. This component instead adds back the strafe the gate removed,
/// running at execution order 10 so it applies on top of the position the controller has already
/// written and clamped in its own Update.
///
/// Third person is untouched: the first line of Move is a perspective check, and in third person this
/// component does nothing at all.
/// </summary>
[DefaultExecutionOrder(10)]
[RequireComponent(typeof(UnderwaterSwimController))]
public class FirstPersonSwimStrafe : MonoBehaviour
{
    [Tooltip("Frame of reference for 'sideways'. Leave empty to use the body's own right axis, which " +
             "in first person is the same thing -- SUPER keeps the body yaw locked to the camera yaw.")]
    public Transform cameraTransform;

    [Tooltip("Action name in the PlayerInput asset. The stock Player map calls it 'Move'.")]
    public string moveActionName = "Move";

    [Tooltip("Action name for sprint. Leave empty to ignore sprinting.")]
    public string sprintActionName = "Sprint";

    [Tooltip("Fallback speed in metres/second, used only if the controller's own swimSpeed cannot be " +
             "read. The real value is taken from UnderwaterSwimController at Awake so the two cannot " +
             "drift apart when you retune it.")]
    public float fallbackSwimSpeed = 4f;

    [Tooltip("Fallback sprint multiplier, used on the same terms as the speed above.")]
    public float fallbackSprintMultiplier = 1.5f;

    private UnderwaterSwimController swim;
    private SUPERCharacterAIO super;
    private InputAction moveAction;
    private InputAction sprintAction;

    private float swimSpeed;
    private float sprintMultiplier;

    private void Awake()
    {
        swim = GetComponent<UnderwaterSwimController>();
        super = GetComponent<SUPERCharacterAIO>();

        if (super == null)
        {
            Debug.LogWarning($"{name}: no SUPERCharacterAIO, so there is no perspective to read. " +
                             "Disabling rather than guessing -- this component must never run in third person.", this);
            enabled = false;
            return;
        }

        PlayerInput playerInput = GetComponent<PlayerInput>();
        if (playerInput != null && playerInput.actions != null)
        {
            moveAction = playerInput.actions.FindAction(moveActionName, false);
            if (!string.IsNullOrEmpty(sprintActionName))
            {
                sprintAction = playerInput.actions.FindAction(sprintActionName, false);
            }
        }

        if (moveAction == null)
        {
            Debug.LogWarning($"{name}: no '{moveActionName}' action found; first-person strafing stays broken.", this);
            enabled = false;
            return;
        }

        // Read the controller's real tuning rather than duplicating it in the Inspector. Both fields
        // are [SerializeField] private, so they survive IL2CPP stripping and reflection finds them in
        // a build. The fallbacks only matter if the package renames them.
        swimSpeed = ReadPrivateFloat(swim, "swimSpeed", fallbackSwimSpeed);
        sprintMultiplier = ReadPrivateFloat(swim, "sprintMultiplier", fallbackSprintMultiplier);
    }

    private void Update()
    {
        if (super.cameraPerspective != PerspectiveModes._1stPerson) return;
        if (!swim.IsInWater) return;

        float sideways = moveAction.ReadValue<Vector2>().x;
        if (Mathf.Abs(sideways) < 0.01f) return;

        Transform reference = cameraTransform != null ? cameraTransform : transform;
        Vector3 right = reference.right;
        right.y = 0f;

        // Looking straight up or down collapses the flattened axis to nothing, which would normalise
        // into a garbage direction and fling the player sideways at full speed.
        if (right.sqrMagnitude < 1e-6f) return;
        right.Normalize();

        bool sprinting = sprintAction != null && sprintAction.IsPressed();
        float speed = swimSpeed * (sprinting ? sprintMultiplier : 1f);

        Vector3 position = transform.position + right * (sideways * speed * Time.deltaTime);

        // The controller's own ApplyMovementLimits is private, so the position half of it is mirrored
        // here. Without this the strafe walks straight through the seabed, since it runs after the
        // controller has already done its clamp for the frame.
        transform.position = ClampToLimits(position);
    }

    /// <summary>
    /// Mirrors UnderwaterSwimController.ApplyMovementLimits :240-287 -- the position clamp only. The
    /// velocity half is skipped deliberately: currentVelocity is private with a read-only accessor,
    /// and this component never writes to it, so there is no velocity to correct.
    /// </summary>
    private Vector3 ClampToLimits(Vector3 position)
    {
        Terrain terrain = swim.TerrainLimit;
        if (terrain != null && terrain.terrainData != null)
        {
            Vector3 terrainOrigin = terrain.transform.position;
            TerrainData data = terrain.terrainData;
            Vector3 localPosition = position - terrainOrigin;
            Vector3 terrainSize = data.size;
            float normalizedX = terrainSize.x > 0f ? Mathf.Clamp01(localPosition.x / terrainSize.x) : 0f;
            float normalizedZ = terrainSize.z > 0f ? Mathf.Clamp01(localPosition.z / terrainSize.z) : 0f;

            float terrainHeight = terrain.SampleHeight(position) + terrainOrigin.y;
            Vector3 surfacePoint = new Vector3(position.x, terrainHeight, position.z);
            Vector3 terrainNormal = data.GetInterpolatedNormal(normalizedX, normalizedZ);
            terrainNormal = terrainNormal.sqrMagnitude > 0.0001f ? terrainNormal.normalized : Vector3.up;

            float clearance = Mathf.Max(swim.TerrainClearance, 0f);
            float distanceAlongNormal = Vector3.Dot(position - surfacePoint, terrainNormal);
            if (distanceAlongNormal < clearance)
            {
                position += terrainNormal * (clearance - distanceAlongNormal);
            }
        }

        if (swim.ClampToWorldHeight && position.y > swim.MaxWorldHeight)
        {
            position.y = swim.MaxWorldHeight;
        }

        return position;
    }

    private static float ReadPrivateFloat(object target, string fieldName, float fallback)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        return field != null && field.FieldType == typeof(float) ? (float)field.GetValue(target) : fallback;
    }
}
