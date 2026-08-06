using SUPERCharacter;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// One-shot repair for the first/third person setup on the SUPER Character Controller.
/// Applies itself automatically the next time the editor is in Edit mode, and is also
/// available manually from Tools > Fix Third Person Setup.
/// </summary>
[InitializeOnLoad]
public static class FixThirdPersonSetup
{
    private const string AppliedKey = "FixThirdPersonSetup.Applied";

    static FixThirdPersonSetup()
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
        // Anything changed during Play mode is discarded on Stop, so wait for Edit mode.
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (SessionState.GetBool(AppliedKey, false)) return;

        if (Apply(out var controller))
        {
            SessionState.SetBool(AppliedKey, true);
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorSceneManager.SaveScene(controller.gameObject.scene);
            Debug.Log("Saved the scene. Press Play — the camera should now follow the character.");
        }
    }

    [MenuItem("Tools/Fix Third Person Setup")]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Exit Play mode before running this. Changes made while playing are discarded when you stop.");
            return;
        }

        if (Apply(out var controller))
        {
            SessionState.SetBool(AppliedKey, true);
            EditorSceneManager.SaveScene(controller.gameObject.scene);
            Debug.Log("Saved the scene. Press Play — the camera should now follow the character.");
        }
    }

    private static bool Apply(out SUPERCharacterAIO controller)
    {
        controller = Object.FindFirstObjectByType<SUPERCharacterAIO>(FindObjectsInactive.Include);
        if (controller == null)
        {
            Debug.LogError("No SUPERCharacterAIO found in the open scene. Open SampleScene first.");
            return false;
        }

        Undo.RecordObject(controller, "Fix Third Person Setup");

        // 1. The auto-crosshair block throws a NullReferenceException partway through
        // Start(), which aborts before the capsule and rigidbody are ever cached. That
        // null capsule is what then throws every frame in Update and GetGroundInfo.
        controller.autoGenerateCrosshair = false;
        controller.drawPrimitiveUI = false;

        // 2. The controller positions the camera with localPosition, so the camera has to
        // be a child of the player. Wiring it to a root-level Main Camera cannot work.
        var ownCamera = controller.GetComponentInChildren<Camera>(true);
        if (ownCamera == null)
        {
            Debug.LogError("The player has no child Camera. Cannot rewire playerCamera.");
            return false;
        }

        if (controller.playerCamera != ownCamera)
        {
            controller.playerCamera = ownCamera;
            Debug.Log($"Rewired playerCamera to the player's own child camera '{ownCamera.name}'.");
        }

        Undo.RecordObject(ownCamera, "Fix Third Person Setup");
        Undo.RecordObject(ownCamera.gameObject, "Fix Third Person Setup");
        ownCamera.gameObject.SetActive(true);
        ownCamera.enabled = true;

        // Clear flags of "Nothing" skip the skybox pass, which leaves whatever garbage
        // was in the colour buffer behind the geometry. That is the flat yellow.
        if (ownCamera.clearFlags != CameraClearFlags.Skybox)
        {
            ownCamera.clearFlags = CameraClearFlags.Skybox;
            Debug.Log("Set the player camera to clear to Skybox so the sky renders instead of buffer garbage.");
        }

        ownCamera.tag = "MainCamera";

        // 3. A second active camera means a second AudioListener and a double render.
        foreach (var stray in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (stray == ownCamera || stray.GetComponentInParent<SUPERCharacterAIO>() != null) continue;
            Undo.RecordObject(stray.gameObject, "Fix Third Person Setup");
            stray.gameObject.SetActive(false);
            Debug.Log($"Disabled the redundant camera '{stray.name}' (duplicate AudioListener and double render).");
        }

        // 4. Both animator slots pointed at the same object. ChangePerspective enables one
        // slot then disables the other, so a shared object always ends up hidden.
        var thirdPerson = controller._3rdPersonCharacterAnimator;
        if (thirdPerson == null)
        {
            thirdPerson = controller._1stPersonCharacterAnimator;
            controller._3rdPersonCharacterAnimator = thirdPerson;
        }

        if (thirdPerson != null && controller._1stPersonCharacterAnimator == thirdPerson)
        {
            controller._1stPersonCharacterAnimator = null;
            Debug.Log($"Cleared the 1st Person Animator slot (was sharing '{thirdPerson.name}' with the 3rd Person slot).");
        }

        // A disabled Animator ignores the SetFloat/SetBool calls the controller makes.
        if (thirdPerson != null)
        {
            Undo.RecordObject(thirdPerson, "Fix Third Person Setup");
            Undo.RecordObject(thirdPerson.gameObject, "Fix Third Person Setup");
            thirdPerson.enabled = true;
            thirdPerson.gameObject.SetActive(true);
        }
        else
        {
            Debug.LogWarning("No character Animator is assigned, so nothing will be visible in third person.");
        }

        // 5. CameraSwitcher reads the legacy Input class, which throws under Input System
        // only. Matched by name so this still compiles once the script is deleted.
        foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (mb != null && mb.GetType().Name == "CameraSwitcher")
            {
                Debug.Log($"Removed the CameraSwitcher component from '{mb.gameObject.name}'; SUPER already handles the Q toggle.");
                Undo.DestroyObjectImmediate(mb);
            }
        }

        // 6. Q toggles perspective; automatic switching would hijack it via the scroll wheel.
        controller.perspectiveSwitchingKey = Key.Q;
        controller.automaticallySwitchPerspective = false;
        controller.cameraPerspective = PerspectiveModes._3rdPerson;

        if (controller.maxCameraDistance < 2f) controller.maxCameraDistance = 5f;

        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
        Debug.Log($"Fix applied. 3rd Person Animator = '{(thirdPerson != null ? thirdPerson.name : "NONE")}', " +
                  $"1st Person Animator = NONE, camera = '{ownCamera.name}', switch key = Q.");
        return true;
    }
}
