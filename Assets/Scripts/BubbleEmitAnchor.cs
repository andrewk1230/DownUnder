using System.Reflection;
using UnityEngine;

/// <summary>
/// Keeps UnderwaterVisuals' bubble bursts on the swimmer instead of on the camera.
///
/// UnderwaterVisuals is [RequireComponent(typeof(Camera))] -- "Attach to the main camera" -- and
/// computes its emit origin from its own transform (UnderwaterVisuals :205):
///
///     var emitOrigin = transform.position + transform.TransformVector(bubbleLocalOffset);
///
/// In first person that reads correctly, because the camera sits at the head. In third person
/// ThirdPersonCameraPivot puts the camera on a 4 m boom behind the character, so the bubbles trail
/// out of empty water behind the player rather than out of the player.
///
/// The emit origin cannot be redirected directly: EmitBubbleBurst is private and the component lives
/// in a git package (com.budorf.underwater-swim) that cannot be edited without forking. But
/// bubbleLocalOffset is a [SerializeField] private Vector3 that the origin is derived from, so
/// solving for the offset that lands the origin on the swimmer and writing it each frame produces the
/// right result through the package's own code path:
///
///     bubbleLocalOffset = camera.InverseTransformVector(desiredWorldOrigin - camera.position)
///
/// This is safe with respect to the emitter GameObject, whose localPosition is also seeded from
/// bubbleLocalOffset (UnderwaterVisuals :322): that happens once in OnEnable, and the particle system
/// is configured with simulationSpace = World (:328, :415), so emission uses the explicit world
/// EmitParams.position and the emitter object's own placement does not enter into it.
///
/// Runs in LateUpdate at order 200, after ThirdPersonCameraPivot (100) and FirstPersonLookYaw (150),
/// so the camera pose it solves against is the final one for the frame. UnderwaterVisuals emits in
/// Update, so each burst uses the offset written at the end of the previous frame -- an error of one
/// frame of camera motion, a couple of centimetres at normal swim speeds.
/// </summary>
[DefaultExecutionOrder(200)]
public class BubbleEmitAnchor : MonoBehaviour
{
    [Tooltip("The swimmer the bubbles should come from. Leave empty to find the UnderwaterSwimController " +
             "in the scene automatically.")]
    public Transform anchor;

    [Tooltip("Offset from the anchor, in the anchor's own space. The package's camera-relative default " +
             "was (0, -0.2, 0.25) -- just below and slightly in front, i.e. roughly at the mouth.")]
    public Vector3 anchorLocalOffset = new Vector3(0f, -0.2f, 0.25f);

    private Component visuals;
    private FieldInfo offsetField;

    private void Awake()
    {
        // Matched by type name so this still compiles if the swim package is ever removed.
        foreach (Component component in GetComponents<Component>())
        {
            if (component != null && component.GetType().Name == "UnderwaterVisuals")
            {
                visuals = component;
                break;
            }
        }

        if (visuals == null)
        {
            Debug.LogWarning($"{name}: no UnderwaterVisuals on this GameObject. This belongs on the same " +
                             "camera that component is on. Disabling.", this);
            enabled = false;
            return;
        }

        offsetField = visuals.GetType().GetField("bubbleLocalOffset", BindingFlags.Instance | BindingFlags.NonPublic);
        if (offsetField == null || offsetField.FieldType != typeof(Vector3))
        {
            Debug.LogWarning($"{name}: UnderwaterVisuals has no private Vector3 'bubbleLocalOffset'. The " +
                             "package has probably been updated and renamed it. Disabling.", this);
            enabled = false;
            return;
        }

        if (anchor == null)
        {
            var swim = FindAnyObjectByType<UnderwaterSwimController>();
            if (swim != null) anchor = swim.transform;
        }

        if (anchor == null)
        {
            Debug.LogWarning($"{name}: no anchor assigned and no UnderwaterSwimController in the scene. " +
                             "Disabling.", this);
            enabled = false;
        }
    }

    private void LateUpdate()
    {
        Vector3 desiredOrigin = anchor.position + anchor.TransformVector(anchorLocalOffset);
        offsetField.SetValue(visuals, transform.InverseTransformVector(desiredOrigin - transform.position));
    }
}
