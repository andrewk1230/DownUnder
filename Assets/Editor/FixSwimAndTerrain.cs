using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Fixes the sinking character and the dead swim animations. These turned out to be one bug.
///
/// The capsule was ending up inside the TerrainCollider, and a downward raycast whose origin is
/// below a heightfield does not hit it — so SUPERCharacterAIO.cs:960 reported Grounded = false
/// forever, the AnyState -> AirBorn transition fired every frame, and the animator was pinned in
/// the jump clip. That is why neither Treading Water (the clip on the Idle state) nor Swimming
/// ever appeared.
///
/// Two fixes, applied together because the second is only observable once the first lands:
///
/// 1. Terrain. UnderwaterSwimController moves the player by assigning transform.position, which is
///    a teleport that ignores colliders entirely. Its own substitute for collision is
///    ApplyMovementLimits, which was doing nothing because terrainLimit was unassigned. Assigning
///    it, with a clearance derived from the capsule, gives the swim controller a seabed floor and
///    ejects the capsule on the first frame. Nothing else pushes it out: SUPER never snaps to the
///    ground (playerGroundPosition is only ever drawn as a gizmo) and it hard-writes
///    p_Rigidbody.linearVelocity every FixedUpdate, which destroys PhysX's depenetration velocity.
///
/// 2. Animator. The graph is a land controller with Idle swapped to Treading Water and a Swimming
///    state bolted on, but nothing told it whether the player was in water, so Idle -> Moving (no
///    exit time, fires on the first frame of input) always beat Idle -> Swimming (gated behind
///    exitTime 0.9375 and a SwimSpeed threshold that needs a few frames to reach). An InWater bool
///    now gates the land transitions, and Swimming's exit condition is made satisfiable — it
///    required "SwimSpeed &lt; 0.1 AND IsSwimming is true", but both come from the same value, so
///    IsSwimming is already false by the time SwimSpeed drops that low.
///
/// Idempotent. Runs on entering Edit mode, or Tools -> Fix Swim And Terrain.
/// Backup of the pre-change scene and controller: Backups/2026-07-26-swim-and-terrain-fix/
/// </summary>
[InitializeOnLoad]
public static class FixSwimAndTerrain
{
    private const string AppliedKey = "FixSwimAndTerrain.Applied.v1";
    private const string InWaterParam = "InWater";
    private const float SwimSpeedThreshold = 0.1f;

    // The root pivot sits at the capsule's centre, so the feet are half the capsule height below
    // it. Clamping at exactly that height would fight the resting contact — a capsule on a slope
    // legitimately rests closer to the vertically-sampled surface point than it does on the flat —
    // so back off slightly and let the floor act as a safety net rather than a constant push.
    private const float ClearanceSlack = 0.075f;

    static FixSwimAndTerrain()
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
        Run(auto: true);
    }

    [MenuItem("Tools/Fix Swim And Terrain")]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Exit Play mode before running this. Changes made while playing are discarded when you stop.");
            return;
        }
        Run(auto: false);
    }

    private static void Run(bool auto)
    {
        var swim = Object.FindObjectsByType<UnderwaterSwimController>(FindObjectsInactive.Include)
                         .FirstOrDefault();
        if (swim == null)
        {
            if (!auto) Debug.LogError("No UnderwaterSwimController in the open scene. Open SampleScene first.");
            return;
        }

        var sceneChanged = FixTerrainFloor(swim) | AttachBridge(swim);
        var animatorChanged = FixAnimator(swim);

        if (sceneChanged)
        {
            EditorSceneManager.MarkSceneDirty(swim.gameObject.scene);
            EditorSceneManager.SaveScene(swim.gameObject.scene);
            Debug.Log("Saved the scene.");
        }
        if (animatorChanged)
        {
            AssetDatabase.SaveAssets();
            Debug.Log("Saved the animator controller.");
        }
        if (!sceneChanged && !animatorChanged) Debug.Log("FixSwimAndTerrain: already correct, nothing to change.");

        SessionState.SetBool(AppliedKey, true);
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
    }

    private static bool FixTerrainFloor(UnderwaterSwimController swim)
    {
        var terrain = Object.FindAnyObjectByType<Terrain>();
        if (terrain == null)
        {
            Debug.LogWarning("No Terrain in the scene; terrainLimit left unassigned, so the swim controller still has no seabed floor.");
            return false;
        }

        var capsule = swim.GetComponent<CapsuleCollider>();
        if (capsule == null)
        {
            Debug.LogError("The player has no CapsuleCollider; cannot work out a sensible terrainClearance.");
            return false;
        }

        var clearance = Mathf.Max(0f, (capsule.height * 0.5f) - ClearanceSlack);

        var so = new SerializedObject(swim);
        var limitProp = so.FindProperty("terrainLimit");
        var clearanceProp = so.FindProperty("terrainClearance");
        var changed = false;

        if (limitProp != null && limitProp.objectReferenceValue != terrain)
        {
            limitProp.objectReferenceValue = terrain;
            changed = true;
            Debug.Log($"Assigned terrainLimit to '{terrain.name}'. ApplyMovementLimits now has something to clamp against.");
        }
        if (clearanceProp != null && !Mathf.Approximately(clearanceProp.floatValue, clearance))
        {
            Debug.Log($"Set terrainClearance to {clearance} (was {clearanceProp.floatValue}); " +
                      $"capsule height {capsule.height} means the feet are {capsule.height * 0.5f} below the pivot.");
            clearanceProp.floatValue = clearance;
            changed = true;
        }

        if (changed)
        {
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(swim);
        }
        return changed;
    }

    private static bool AttachBridge(UnderwaterSwimController swim)
    {
        if (swim.GetComponent<SwimAnimatorBridge>() != null) return false;
        Undo.AddComponent<SwimAnimatorBridge>(swim.gameObject);
        Debug.Log($"Added SwimAnimatorBridge to '{swim.gameObject.name}'; it publishes {InWaterParam} to the Animator each frame.");
        return true;
    }

    private static bool FixAnimator(UnderwaterSwimController swim)
    {
        var animator = swim.GetComponentInChildren<Animator>();
        var runtime = animator != null ? animator.runtimeAnimatorController : null;
        if (runtime == null)
        {
            Debug.LogError("Could not find an Animator with a controller under the player; animator fixes skipped.");
            return false;
        }

        var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GetAssetPath(runtime));
        if (ac == null)
        {
            Debug.LogError($"'{runtime.name}' is not an editable AnimatorController asset; animator fixes skipped.");
            return false;
        }

        var changed = false;

        if (!ac.parameters.Any(p => p.name == InWaterParam))
        {
            ac.AddParameter(InWaterParam, AnimatorControllerParameterType.Bool);
            changed = true;
            Debug.Log($"Added bool parameter '{InWaterParam}' to '{ac.name}'.");
        }

        var sm = ac.layers[0].stateMachine;
        var states = new Dictionary<string, AnimatorState>();
        foreach (var child in sm.states) states[child.state.name] = child.state;

        foreach (var required in new[] { "Idle", "Swimming", "Moving", "AirBorn" })
        {
            if (!states.ContainsKey(required))
            {
                Debug.LogError($"'{ac.name}' has no state named '{required}'; animator fixes skipped to avoid mangling the graph.");
                return changed;
            }
        }

        // Being in water must not read as falling. Grounded is false whenever the player is off the
        // seabed, and this AnyState transition outranks every state's own transitions, so without
        // this guard the swim states are unreachable exactly when they are wanted.
        var toAirborne = sm.anyStateTransitions.FirstOrDefault(t => t.destinationState == states["AirBorn"]);
        if (toAirborne != null) changed |= EnsureCondition(toAirborne, AnimatorConditionMode.IfNot, InWaterParam, 0f);

        // Idle -> Moving fires on the first frame of input, long before SwimSpeed can climb past
        // its threshold, so it has to be restricted to dry land or it always wins the race.
        foreach (var t in states["Idle"].transitions.Where(t => t.destinationState == states["Moving"]))
        {
            changed |= EnsureCondition(t, AnimatorConditionMode.IfNot, InWaterParam, 0f);
        }

        // Idle -> Swimming: drop the exit time so it can fire as soon as you are moving in water,
        // and key it off InWater rather than the speed-derived IsSwimming bool.
        var toSwimming = states["Idle"].transitions.FirstOrDefault(t => t.destinationState == states["Swimming"]);
        if (toSwimming == null)
        {
            toSwimming = states["Idle"].AddTransition(states["Swimming"]);
            changed = true;
            Debug.Log("Added a missing Idle -> Swimming transition.");
        }
        changed |= SetTransition(toSwimming, "Idle -> Swimming", new[]
        {
            NewCondition(AnimatorConditionMode.If, InWaterParam, 0f),
            NewCondition(AnimatorConditionMode.Greater, "SwimSpeed", SwimSpeedThreshold),
        });

        // Swimming's only exit demanded "SwimSpeed < 0.1 AND IsSwimming is true". Both are written
        // from the same normalised speed, so IsSwimming is already false by the time SwimSpeed is
        // under the threshold — the condition could never be satisfied and the state was a trap.
        var swimToIdle = states["Swimming"].transitions.FirstOrDefault(t => t.destinationState == states["Idle"]
                                                                            && t.conditions.Any(c => c.parameter == "SwimSpeed"));
        if (swimToIdle == null)
        {
            swimToIdle = states["Swimming"].AddTransition(states["Idle"]);
            changed = true;
            Debug.Log("Added a missing Swimming -> Idle transition.");
        }
        changed |= SetTransition(swimToIdle, "Swimming -> Idle (slowed down)", new[]
        {
            NewCondition(AnimatorConditionMode.Less, "SwimSpeed", SwimSpeedThreshold),
        });

        // And an exit for leaving the water mid-stroke, which the speed condition alone would miss.
        var swimToIdleDry = states["Swimming"].transitions.FirstOrDefault(t => t.destinationState == states["Idle"]
                                                                               && t.conditions.Any(c => c.parameter == InWaterParam));
        if (swimToIdleDry == null)
        {
            swimToIdleDry = states["Swimming"].AddTransition(states["Idle"]);
            changed = true;
            Debug.Log("Added Swimming -> Idle for leaving the water.");
        }
        changed |= SetTransition(swimToIdleDry, "Swimming -> Idle (left water)", new[]
        {
            NewCondition(AnimatorConditionMode.IfNot, InWaterParam, 0f),
        });

        if (changed) EditorUtility.SetDirty(ac);
        return changed;
    }

    private static AnimatorCondition NewCondition(AnimatorConditionMode mode, string parameter, float threshold)
    {
        return new AnimatorCondition { mode = mode, parameter = parameter, threshold = threshold };
    }

    private static bool EnsureCondition(AnimatorStateTransition t, AnimatorConditionMode mode, string parameter, float threshold)
    {
        if (t.conditions.Any(c => c.parameter == parameter && c.mode == mode)) return false;
        var conditions = t.conditions.ToList();
        conditions.Add(NewCondition(mode, parameter, threshold));
        t.conditions = conditions.ToArray();
        Debug.Log($"Added condition '{parameter} {mode}' to the transition into '{t.destinationState.name}'.");
        return true;
    }

    private static bool SetTransition(AnimatorStateTransition t, string label, AnimatorCondition[] conditions)
    {
        var changed = false;

        if (t.hasExitTime)
        {
            t.hasExitTime = false;
            changed = true;
            Debug.Log($"Cleared hasExitTime on {label}; it could previously only fire in the last few percent of the source clip.");
        }

        var same = t.conditions.Length == conditions.Length && t.conditions.All(existing =>
            conditions.Any(c => c.parameter == existing.parameter
                                && c.mode == existing.mode
                                && Mathf.Approximately(c.threshold, existing.threshold)));
        if (!same)
        {
            var before = string.Join(" AND ", t.conditions.Select(Describe));
            t.conditions = conditions;
            changed = true;
            Debug.Log($"{label} conditions: [{before}] -> [{string.Join(" AND ", conditions.Select(Describe))}]");
        }

        return changed;
    }

    private static string Describe(AnimatorCondition c)
    {
        return c.mode switch
        {
            AnimatorConditionMode.If => $"{c.parameter} is true",
            AnimatorConditionMode.IfNot => $"{c.parameter} is false",
            AnimatorConditionMode.Greater => $"{c.parameter} > {c.threshold}",
            AnimatorConditionMode.Less => $"{c.parameter} < {c.threshold}",
            _ => $"{c.parameter} {c.mode} {c.threshold}",
        };
    }
}
