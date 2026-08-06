using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Two field fixes on UnderwaterSwimController, and nothing else.
///
/// 1. cameraTransform pointed at the root-level Main Camera, which FixThirdPersonSetup disabled.
///    An inactive, unparented transform never rotates, so the controller's movement reference
///    was a constant world +Z. Standing still left rotation to SUPER's mouse look, but holding
///    a movement key made the swim controller slerp the body back to that fixed heading — the
///    "turns when stationary, snaps back when I press forward" symptom.
///
/// 2. swimmingBoolParameter names IsSwimming, which JoshAnimator does not define. Its Swimming
///    state already transitions on the SwimSpeed float, so the bool drives nothing and only
///    produces a per-frame "Parameter does not exist" error. An empty name makes UpdateAnimator
///    skip the SetBool entirely.
///
/// Matched by type name so this still compiles if the swim package is ever removed.
/// Temporary scaffolding — delete once the fix is confirmed.
/// </summary>
[InitializeOnLoad]
public static class FixFirstPersonTurn
{
    // Bumped so the corrected camera resolution re-runs even though the old version already
    // marked itself applied this session.
    private const string AppliedKey = "FixFirstPersonTurn.Applied.v3";

    // Off while the camera rewire is being tested in isolation, so exactly one field changes and
    // the result is unambiguous. Turn back on to clear the bogus IsSwimming parameter.
    private static readonly bool ClearSwimmingBool = false;

    static FixFirstPersonTurn()
    {
        if (SessionState.GetBool(AppliedKey, false)) return;
        EditorApplication.delayCall += TryAutoApply;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += TryAutoApply;
    }

    private static void TryAutoApply()
    {
        // Play-mode edits are discarded on Stop, so only ever apply in Edit mode.
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (SessionState.GetBool(AppliedKey, false)) return;

        if (Apply(out var scene))
        {
            SessionState.SetBool(AppliedKey, true);
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Saved the scene.");
        }
    }

    [MenuItem("Tools/Fix First Person Turn")]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Exit Play mode before running this. Changes made while playing are discarded when you stop.");
            return;
        }

        if (Apply(out var scene))
        {
            SessionState.SetBool(AppliedKey, true);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Saved the scene.");
        }
    }

    private static bool Apply(out UnityEngine.SceneManagement.Scene scene)
    {
        scene = default;

        MonoBehaviour swim = null;
        foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (mb != null && mb.GetType().Name == "UnderwaterSwimController") { swim = mb; break; }
        }

        if (swim == null)
        {
            Debug.LogError("No UnderwaterSwimController found in the open scene. Open SampleScene first.");
            return false;
        }

        scene = swim.gameObject.scene;
        var so = new SerializedObject(swim);
        var changed = false;

        // GetComponentInChildren also matches the object itself, and the player root carries a
        // stray Camera of its own — it returned 'Character.001' (the player transform), which made
        // the swim controller steer by its own forward: holding left/right asks the body to face
        // its own right, which moves the target again next frame, so the root spins without end.
        // SUPERCharacterAIO.playerCamera is the authoritative reference, but it has been wired to
        // that root camera before, so reject the root here no matter which path supplies it.
        var controller = swim.GetComponent<SUPERCharacter.SUPERCharacterAIO>();
        var ownCamera = controller != null ? controller.playerCamera : null;
        if (ownCamera != null && ownCamera.transform == swim.transform)
        {
            Debug.LogWarning("SUPERCharacterAIO.playerCamera points at the player root's own camera. " +
                             "Ignoring it and looking for a genuine child camera instead.");
            ownCamera = null;
        }
        if (ownCamera == null)
        {
            foreach (var cam in swim.GetComponentsInChildren<Camera>(true))
            {
                if (cam.transform == swim.transform) continue; // Skip the root's own camera.
                ownCamera = cam;
                break;
            }
        }

        var camProp = so.FindProperty("cameraTransform");
        if (ownCamera == null)
        {
            Debug.LogError("Could not resolve a child camera on the player; cameraTransform left alone.");
        }
        else if (ownCamera.transform == swim.transform)
        {
            // Belt and braces: writing the root here is the bug, not a fix.
            Debug.LogError("Resolved camera is the player root; refusing to write it to cameraTransform.");
        }
        else if (camProp != null && camProp.objectReferenceValue != (Object)ownCamera.transform)
        {
            var old = camProp.objectReferenceValue;
            camProp.objectReferenceValue = ownCamera.transform;
            changed = true;
            Debug.Log($"Rewired cameraTransform from '{(old != null ? old.name : "NONE")}' to child camera " +
                      $"'{ownCamera.name}' (parent '{ownCamera.transform.parent.name}').");
        }
        else if (camProp != null)
        {
            Debug.Log($"cameraTransform already points at '{ownCamera.name}'.");
        }

        // The camera is a child of the player root, and SUPER pins its world pose in FixedUpdate
        // (SUPERCharacterAIO.UpdateCameraPosition_3rdPerson). Anything that rotates the root from
        // Update therefore swings the camera around a several-metre lever arm on rendered frames
        // that carry no physics step, and it snaps back on the ones that do — a left/right judder
        // for as long as the body is turning. Position writes are self-correcting (the drag equals
        // the delta the re-pin would apply); only rotation writes produce error.
        //
        // Zeroing rotationDamping makes UnderwaterSwimController's Slerp return the current
        // rotation, so its `transform.rotation = smoothedRotation` becomes a no-op and the pivot
        // compensation underneath it cancels. Body yaw is then owned solely by SUPER's
        // UpdateBodyRotation_3rdPerson, which runs in FixedUpdate immediately before the camera
        // pin and so can never drag it.
        var dampProp = so.FindProperty("rotationDamping");
        if (dampProp != null && dampProp.floatValue != 0f)
        {
            Debug.Log($"Set rotationDamping to 0 (was {dampProp.floatValue}); SUPER now owns body yaw in third person.");
            dampProp.floatValue = 0f;
            changed = true;
        }

        // Animator.parameters is unreliable in Edit mode (it can read empty before the controller
        // is bound), so consult the AnimatorController asset directly and log what it actually
        // defines — an earlier attempt cleared nothing and gave no clue why.
        var boolProp = so.FindProperty("swimmingBoolParameter");
        var animProp = so.FindProperty("animator");
        var anim = animProp != null ? animProp.objectReferenceValue as Animator : null;
        if (ClearSwimmingBool && boolProp != null && !string.IsNullOrEmpty(boolProp.stringValue))
        {
            var names = ParameterNames(anim, out var source);
            Debug.Log($"Animator '{source}' defines: {(names.Count == 0 ? "(none readable)" : string.Join(", ", names))}.");

            if (names.Count > 0 && !names.Contains(boolProp.stringValue))
            {
                Debug.Log($"Cleared swimmingBoolParameter (was '{boolProp.stringValue}'); that animator has no such parameter.");
                boolProp.stringValue = string.Empty;
                changed = true;
            }
            else if (names.Count == 0)
            {
                Debug.LogWarning("Could not read the animator's parameters, so swimmingBoolParameter was left alone. " +
                                 "Clear it by hand on the UnderwaterSwimController if the SetBool error persists.");
            }
        }

        if (!changed)
        {
            Debug.Log("Already correct; nothing to change.");
            return false;
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(swim);
        EditorSceneManager.MarkSceneDirty(scene);
        return true;
    }

    private static System.Collections.Generic.HashSet<string> ParameterNames(Animator animator, out string source)
    {
        var names = new System.Collections.Generic.HashSet<string>();
        source = "NONE";
        if (animator == null) return names;

        var rac = animator.runtimeAnimatorController;
        if (rac == null) return names;
        source = rac.name;

        // The asset is the source of truth; Animator.parameters is only populated once bound.
        var path = AssetDatabase.GetAssetPath(rac);
        var ac = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(path);
        if (ac != null)
        {
            foreach (var p in ac.parameters) names.Add(p.name);
            return names;
        }

        foreach (var p in animator.parameters) names.Add(p.name);
        return names;
    }
}
