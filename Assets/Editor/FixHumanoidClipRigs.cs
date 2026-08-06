using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The reason no animation plays: the two water clips are imported as **Generic** while Josh's
/// avatar is **Humanoid**, and Unity silently refuses to bind a generic clip's transform curves to
/// a humanoid-configured rig.
///
/// The proof was already baked into the scene. SampleScene.unity:5197 stores the Animator's own
/// m_WarningMessage:
///
///     "Binding warning: Some generic clip(s) animate transforms that are already bound by a
///      Humanoid avatar. These transforms can only be changed by Humanoid clips.
///      Transform 'mixamorig:Hips' ... From animation clip 'mixamo.com'"
///
/// Confirmed against the importers:
///
///   | Animator state | Source FBX                     | animationType |
///   |----------------|--------------------------------|---------------|
///   | Idle           | Movement/Treading Water (1).fbx | 2 = Generic   |
///   | Swimming       | Movement/Swimming (1).fbx      | 2 = Generic   |
///   | Moving         | HumanM@Walk01_Forward [RM].fbx | 3 = Humanoid  |
///   | Moving_Fast    | HumanM@Run01_Forward [RM].fbx  | 3 = Humanoid  |
///   | AirBorn        | HumanM@Jump01.fbx              | 3 = Humanoid  |
///
/// Josh.fbx itself is animationType 3. So every clip the player can currently reach is a clip the
/// avatar rejects: InWater is unconditionally true, which makes Moving/Moving_Fast unreachable and
/// blocks AnyState -> AirBorn, leaving only Idle (Treading Water) and Swimming — the two generic
/// ones. That is exactly the reported symptom set: no tread, no swim, and no run.
///
/// It also explains the sinking. With no bound curves the humanoid pose is not driven, so the
/// rig collapses toward the avatar default while SUPER's stickRendererToCapsuleBottom keeps
/// pinning the FBX root to the capsule bottom — which reads as "standing in the terrain" even
/// though the capsule is where it should be. (The FBX root is *not* the problem: Josh.fbx's
/// top-level nodes are the mesh at the origin and mixamorig:Hips at Y = 107.6 cm, so the root
/// origin is at the feet, as stickRendererToCapsuleBottom requires. That hypothesis is dead.)
///
/// Fix: re-import both as Humanoid with an avatar generated from their own mixamorig skeleton, so
/// they become retargetable humanoid motion. Their AnimationClip fileIDs are name-derived
/// (fileIdsGeneration: 2), so the controller's references survive the reimport — verified below
/// rather than assumed.
///
/// Idempotent. Runs on entering Edit mode, or Tools -> Fix Humanoid Clip Rigs.
/// </summary>
[InitializeOnLoad]
public static class FixHumanoidClipRigs
{
    private const string AppliedKey = "FixHumanoidClipRigs.Applied.v3";

    // Addressed by GUID rather than path: these live under "Kevin Iglesias/..." but are Mixamo
    // downloads dropped into that folder, and the " (1)" suffixes show they have been re-added
    // before. GUIDs survive a rename; paths have not.
    private static readonly (string guid, string state)[] Targets =
    {
        ("793098c78149747ee87ba49fdddcb8ac", "Idle (Treading Water)"),
        ("ea5ce7c35f4c042aa845f4c99714c56b", "Swimming"),
    };

    static FixHumanoidClipRigs()
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

    [MenuItem("Tools/Fix Humanoid Clip Rigs")]
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
        foreach (var (guid, state) in Targets) FixImporter(guid, state);

        AttachDiagnostics();
        AttachCameraPivot();
        ReportAnimatorState();

        SessionState.SetBool(AppliedKey, true);
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
    }

    private static void FixImporter(string guid, string state)
    {
        var path = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogError($"[ClipRigs] {state}: GUID {guid} resolves to nothing. Was the FBX deleted?");
            return;
        }

        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"[ClipRigs] {state}: '{path}' has no ModelImporter.");
            return;
        }

        var needsRig = importer.animationType != ModelImporterAnimationType.Human;
        if (needsRig)
        {
            Debug.Log($"[ClipRigs] {state}: '{path}' was {importer.animationType}; converting to Humanoid " +
                      "(avatar generated from its own mixamorig skeleton).");
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        }

        var unbakedXZ = UnbakeRootPositionXZ(importer, state);

        if (!needsRig && !unbakedXZ)
        {
            Debug.Log($"[ClipRigs] {state}: '{path}' already Humanoid with XZ root motion unbaked; left alone.");
            return;
        }

        importer.SaveAndReimport();

        // Generating a humanoid avatar can fail on an unmapped skeleton, and it fails *quietly* —
        // the clip just carries on not playing. Say so here rather than costing another Play cycle.
        //
        // ModelImporter exposes no public animationImportErrors property in this Unity version, but
        // the importer still serialises the strings, so read them straight out of the .meta.
        ReportMetaField(path, "animationImportErrors", state, isError: true);
        ReportMetaField(path, "animationImportWarnings", state, isError: false);

        var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
        if (avatar == null || !avatar.isValid || !avatar.isHuman)
            Debug.LogError($"[ClipRigs] {state}: no valid humanoid Avatar was generated for '{path}' " +
                           "(valid=" + (avatar != null ? avatar.isValid.ToString() : "no avatar") + "). " +
                           "The clip will still not play; the skeleton needs mapping by hand in the Rig tab.");
        else
            Debug.Log($"[ClipRigs] {state}: generated humanoid Avatar '{avatar.name}'.");
    }

    /// <summary>
    /// "Root Transform Position (XZ) -> Bake Into Pose", forced **off**.
    ///
    /// This reverts the 14:35 change, which had the semantics backwards. Per Unity's model:
    ///
    ///   Bake Into Pose ON  -> Root Transform Position (XZ) is held *constant* and the horizontal
    ///                         motion stays on the body/pose. Delta Position (XZ) is zero, so the
    ///                         GameObject is never moved — and the visible body walks away from its
    ///                         own transform. This is the setting that *causes* the drift.
    ///   Bake Into Pose OFF -> the root transform tracks the body each frame and the motion is
    ///                         extracted as a delta. With `m_ApplyRootMotion` 0 that delta is simply
    ///                         discarded, and the pose renders on the root — i.e. pinned to the
    ///                         GameObject, which is what we want here.
    ///
    /// So OFF is correct for this project, and it is also the state both clips shipped in. The
    /// measurements bear that out: `headBoneVP.x` sat at ~0.500 in the logs both before and after
    /// the bake was enabled, and `modelRootOffsetFromCapsule` is exactly (0, -0.875, 0) throughout.
    /// The mesh was never displaced horizontally; the bake was a no-op applied for a misdiagnosis.
    /// The real framing error is vertical and lives in the camera — see ThirdPersonCameraPivot.cs.
    ///
    /// Only XZ is touched. Y is deliberately left alone — the vertical placement of the *model* is
    /// already correct (`modelY == feetY` in the diagnostics, feet on the terrain).
    /// </summary>
    private static bool UnbakeRootPositionXZ(ModelImporter importer, string state)
    {
        // clipAnimations is empty when the FBX uses its default take, which is the case here; the
        // settings have to be materialised from defaultClipAnimations before they can be edited.
        var clips = importer.clipAnimations;
        var usingDefaults = clips == null || clips.Length == 0;
        if (usingDefaults) clips = importer.defaultClipAnimations;

        if (clips == null || clips.Length == 0)
        {
            Debug.LogWarning($"[ClipRigs] {state}: no animation clips on the importer; cannot unbake root motion.");
            return false;
        }

        // Materialising the defaults replaces "whatever the default take did" with an explicit
        // definition, and defaultClipAnimations reports loopTime as false regardless of how the
        // imported clip currently behaves. Silently un-looping Treading Water would be a nasty
        // regression to hand back, so carry the real value over from the imported asset.
        var live = AssetDatabase.LoadAllAssetsAtPath(importer.assetPath)
                                .OfType<AnimationClip>()
                                .ToDictionary(c => c.name, c => c.isLooping);

        var changed = false;
        foreach (var clip in clips)
        {
            if (!clip.lockRootPositionXZ) continue;
            clip.lockRootPositionXZ = false;
            changed = true;

            if (usingDefaults && live.TryGetValue(clip.name, out var wasLooping) && clip.loopTime != wasLooping)
            {
                Debug.Log($"[ClipRigs] {state}: clip '{clip.name}' — preserving loopTime={wasLooping} " +
                          "while materialising the default take.");
                clip.loopTime = wasLooping;
            }

            Debug.Log($"[ClipRigs] {state}: clip '{clip.name}' — disabled Root Transform Position (XZ) " +
                      "Bake Into Pose, reverting the 14:35 change. With Apply Root Motion off, this " +
                      "is the setting that keeps the rendered body pinned to its GameObject.");
        }

        // Assigning always, not only when changed, would rewrite the importer needlessly; but when
        // we did change something the array must be written back even if it came from the defaults.
        if (changed) importer.clipAnimations = clips;
        return changed;
    }

    /// <summary>
    /// Pulls a single scalar field out of the asset's .meta. Only sane for the flat one-line
    /// entries under `animations:`; a multi-line YAML block would need a real parser.
    /// </summary>
    private static void ReportMetaField(string assetPath, string field, string state, bool isError)
    {
        var metaPath = assetPath + ".meta";
        if (!System.IO.File.Exists(metaPath)) return;

        foreach (var line in System.IO.File.ReadAllLines(metaPath))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith(field + ":")) continue;

            var value = trimmed.Substring(field.Length + 1).Trim();
            if (value.Length == 0) return; // empty is the good case

            var message = $"[ClipRigs] {state}: {field} = {value}";
            if (isError) Debug.LogError(message); else Debug.LogWarning(message);
            return;
        }
    }

    /// <summary>
    /// Reads back what the controller actually resolves to now. The clip fileIDs are name-derived,
    /// so the reimport should preserve the motion references — but "should" is what cost the last
    /// three sessions, so check.
    /// </summary>
    private static void ReportAnimatorState()
    {
        var swim = Object.FindObjectsByType<UnderwaterSwimController>(FindObjectsInactive.Include).FirstOrDefault();
        var animator = swim != null ? swim.GetComponentInChildren<Animator>() : null;
        var runtime = animator != null ? animator.runtimeAnimatorController : null;
        if (runtime == null)
        {
            Debug.LogWarning("[ClipRigs] No player Animator in the open scene; skipping the readback.");
            return;
        }

        Debug.Log($"[ClipRigs] Player avatar: '{(animator.avatar != null ? animator.avatar.name : "<none>")}' " +
                  $"isHuman={(animator.avatar != null && animator.avatar.isHuman)}");

        var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GetAssetPath(runtime));
        if (ac == null || ac.layers.Length == 0) return;

        foreach (var child in ac.layers[0].stateMachine.states)
        {
            var motion = child.state.motion;
            if (motion == null)
            {
                Debug.LogError($"[ClipRigs]   state '{child.state.name}' -> MOTION IS NULL (the reimport broke the reference).");
                continue;
            }
            var clip = motion as AnimationClip;
            var human = clip != null && clip.isHumanMotion;
            var line = $"[ClipRigs]   state '{child.state.name}' -> '{motion.name}' " +
                       $"({AssetDatabase.GetAssetPath(motion)}) isHumanMotion={human}";
            if (human) Debug.Log(line);
            else Debug.LogWarning(line + "  <- will NOT play on a Humanoid avatar");
        }
    }

    private static void AttachDiagnostics()
    {
        var swim = Object.FindObjectsByType<UnderwaterSwimController>(FindObjectsInactive.Include).FirstOrDefault();
        if (swim == null) return;
        if (swim.GetComponent<SwimDiagnostics>() != null) return;

        Undo.AddComponent<SwimDiagnostics>(swim.gameObject);
        EditorSceneManager.MarkSceneDirty(swim.gameObject.scene);
        EditorSceneManager.SaveScene(swim.gameObject.scene);
        Debug.Log($"[ClipRigs] Added SwimDiagnostics to '{swim.gameObject.name}' and saved the scene. " +
                  "It logs once a second for 20 seconds, then switches itself off.");
    }

    /// <summary>
    /// Unlike SwimDiagnostics this is not scaffolding — it is the actual framing fix, and it stays.
    /// It gives third person its own camera pivot so the character sits mid-frame instead of hanging
    /// below the stock head-height aim point, without disturbing standingEyeHeight, which first
    /// person still needs at 0.795.
    /// </summary>
    private static void AttachCameraPivot()
    {
        var super = Object.FindObjectsByType<SUPERCharacter.SUPERCharacterAIO>(FindObjectsInactive.Include)
                          .FirstOrDefault();
        if (super == null)
        {
            Debug.LogWarning("[ClipRigs] No SUPERCharacterAIO in the scene; skipped the camera pivot.");
            return;
        }
        if (super.GetComponent<ThirdPersonCameraPivot>() != null) return;

        Undo.AddComponent<ThirdPersonCameraPivot>(super.gameObject);
        EditorSceneManager.MarkSceneDirty(super.gameObject.scene);
        EditorSceneManager.SaveScene(super.gameObject.scene);
        Debug.Log($"[ClipRigs] Added ThirdPersonCameraPivot to '{super.gameObject.name}' " +
                  "(thirdPersonPivotHeight = 0, i.e. the capsule centre) and saved the scene. " +
                  "The character's mid-height should now land on viewport (0.5, 0.5); " +
                  "watch rootVP on the [SwimDiag/frame] lines to confirm it reads ~0.500.");
    }
}
