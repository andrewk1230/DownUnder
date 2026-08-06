using System.Linq;
using SUPERCharacter;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The player root carries a **second enabled Camera** that nothing drives.
///
/// SampleScene.unity: `--- !u!20 &1071137440` has `m_GameObject: {fileID: 1071137426}` — the player
/// root, `Character.001` — with `m_Enabled: 1`, `m_Depth: 0`, full viewport, culling mask
/// 4294967295. The real third-person camera is the child, `&1042309916`, and it is the one
/// SUPERCharacterAIO.playerCamera points at (`SampleScene.unity:5356`), so it is the only one that
/// ever receives the orbit pose from UpdateCameraPosition_3rdPerson.
///
/// Both cameras render full-screen to display 0 at **the same depth**. Unity sorts cameras by
/// depth and leaves ties unordered, so which one you actually see is undefined and can flip
/// between domain reloads. When the stray one wins you are looking out of the player root: capsule
/// centre, body yaw, no pitch, no boom distance — i.e. "the camera is not aligned with the
/// character", identically while idle and while swimming. That the misalignment is constant across
/// both states is what rules out an animation-pose cause.
///
/// The component is also **orphaned**: `m_PrefabInstance: 0` and `m_CorrespondingSourceObject: 0`,
/// yet the player is a prefab instance and the component is not listed in that instance's
/// `m_AddedComponents` (only the two MonoBehaviours, &1071137432 and &1071137433, are). It is not
/// part of the prefab and not a tracked override.
///
/// This is the same stray camera that caused the earlier camera-shake misdiagnosis chain, via
/// `GetComponentInChildren&lt;Camera&gt;(true)` matching the root object itself before its children.
///
/// **Disabled rather than deleted** — it is not something this session created, and disabling
/// removes the render conflict completely while staying trivially reversible. Delete it by hand
/// once you are happy the view is right.
///
/// Idempotent. Runs on entering Edit mode, or Tools -> Fix Stray Player Camera.
/// Scene backup: Backups/2026-07-26-humanoid-clip-rigs/SampleScene.unity.bak
/// </summary>
[InitializeOnLoad]
public static class FixStrayPlayerCamera
{
    private const string AppliedKey = "FixStrayPlayerCamera.Applied.v1";

    static FixStrayPlayerCamera()
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
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (SessionState.GetBool(AppliedKey, false)) return;
        Run();
    }

    [MenuItem("Tools/Fix Stray Player Camera")]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Exit Play mode before running this. Changes made while playing are discarded when you stop.");
            return;
        }
        SessionState.SetBool(AppliedKey, false);
        Run();
    }

    private static void Run()
    {
        SessionState.SetBool(AppliedKey, true);
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;

        var super = Object.FindObjectsByType<SUPERCharacterAIO>(FindObjectsInactive.Include).FirstOrDefault();
        if (super == null)
        {
            Debug.LogWarning("[StrayCam] No SUPERCharacterAIO in the open scene; skipping.");
            return;
        }

        var keep = super.playerCamera;
        if (keep == null)
        {
            Debug.LogError("[StrayCam] SUPERCharacterAIO.playerCamera is unassigned. Refusing to guess which " +
                           "camera is the real one — assign it in the Inspector first.");
            return;
        }
        if (keep.gameObject == super.gameObject)
        {
            Debug.LogError("[StrayCam] SUPERCharacterAIO.playerCamera points at the player root itself. That is " +
                           "the bug from the earlier camera-shake chain; point it at the child camera first.");
            return;
        }

        Debug.Log($"[StrayCam] Real player camera: '{Path(keep.transform)}' (depth {keep.depth}).");

        // Only the root is in scope. A camera further down the hierarchy is somebody's deliberate
        // choice (a portal, a minimap); one sitting on the movement root is not.
        var changed = false;
        foreach (var cam in super.GetComponents<Camera>())
        {
            if (cam == keep) continue;

            Debug.LogWarning($"[StrayCam] Disabling stray Camera on the player root '{Path(cam.transform)}' " +
                             $"— enabled={cam.enabled}, depth={cam.depth}, targetDisplay={cam.targetDisplay}, " +
                             $"same depth as the real camera ({keep.depth}), so render order between them was undefined.");
            Undo.RecordObject(cam, "Disable stray player camera");
            cam.enabled = false;
            EditorUtility.SetDirty(cam);
            changed = true;
        }

        // Say what is left standing, so the next session does not have to re-derive it. Two
        // full-screen cameras at equal depth is the failure mode; naming them makes it obvious.
        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude)
                                  .OrderBy(c => c.depth))
        {
            if (!cam.enabled) continue;
            Debug.Log($"[StrayCam] still rendering: '{Path(cam.transform)}' depth={cam.depth} " +
                      $"display={cam.targetDisplay} rect={cam.rect}");
        }

        if (!changed)
        {
            Debug.Log("[StrayCam] No stray camera on the player root; nothing to change.");
            return;
        }

        EditorSceneManager.MarkSceneDirty(super.gameObject.scene);
        EditorSceneManager.SaveScene(super.gameObject.scene);
        Debug.Log("[StrayCam] Saved the scene.");
    }

    private static string Path(Transform t)
    {
        var path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }
}
