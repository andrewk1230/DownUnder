using UnityEngine;

/// <summary>
/// Publishes "am I in water?" to the Animator.
///
/// UnderwaterSwimController only ever writes SwimSpeed and IsSwimming, and both are derived from
/// the same number (current speed / max speed). So the animator could tell how fast the player was
/// moving but never whether they were in water — which meant the land transitions (Idle -> Moving)
/// always won the race against Idle -> Swimming, because they fire on the first frame of input
/// while SwimSpeed needs a few frames to climb past its threshold.
///
/// This drives a single bool, InWater, from the swim controller's own IsInWater property, so the
/// controller graph can gate the land states behind it. IsInWater currently returns true always,
/// because requireWaterVolume is off on the component; assign a WaterVolume and turn that on and
/// this bridge starts reporting real transitions without any further change here.
/// </summary>
[RequireComponent(typeof(UnderwaterSwimController))]
public class SwimAnimatorBridge : MonoBehaviour
{
    [Tooltip("Leave empty to use the swim controller on this GameObject.")]
    [SerializeField] private UnderwaterSwimController swimController;

    [Tooltip("Leave empty to use the first Animator found on this GameObject or its children.")]
    [SerializeField] private Animator animator;

    [SerializeField] private string inWaterParameter = "InWater";

    private int inWaterHash;
    private bool hasParameter;

    private void Awake()
    {
        if (swimController == null) swimController = GetComponent<UnderwaterSwimController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        if (swimController == null || animator == null)
        {
            Debug.LogError($"{nameof(SwimAnimatorBridge)} needs both a swim controller and an Animator; disabling.", this);
            enabled = false;
            return;
        }

        inWaterHash = Animator.StringToHash(inWaterParameter);

        // Setting a parameter the controller does not define logs an error every single frame, so
        // check once up front rather than letting it spam. Animator.parameters is reliable here
        // because Awake runs after the controller is bound at runtime.
        foreach (var p in animator.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Bool && p.name == inWaterParameter)
            {
                hasParameter = true;
                break;
            }
        }

        if (!hasParameter)
        {
            Debug.LogError($"Animator '{animator.name}' has no bool parameter '{inWaterParameter}'. " +
                           "Run Tools -> Fix Swim And Terrain to add it. Disabling.", this);
            enabled = false;
        }
    }

    private void Update()
    {
        if (hasParameter) animator.SetBool(inWaterHash, swimController.IsInWater);
    }
}
