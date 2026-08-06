using System.Text;
using SUPERCharacter;
using UnityEngine;

/// <summary>
/// Temporary. Dumps the live state of the player once per second for a bounded window, because
/// every diagnosis in this project so far has been static reasoning over saved assets and the
/// saved assets have repeatedly disagreed with what actually runs.
///
/// Logs once per second (not per frame — Logs/Editor.log is already ~38 MB) and switches itself
/// off after <see cref="durationSeconds"/> so a forgotten Play session cannot flood the log.
/// Delete this component and this file once the swim setup is confirmed good.
/// </summary>
public class SwimDiagnostics : MonoBehaviour
{
    // Deliberately consts, not serialized fields: the component is already saved in the scene with
    // the old 20 s window, and a serialized value would override any default changed here.
    private const float intervalSeconds = 1f;
    private const float durationSeconds = 75f;

    private UnderwaterSwimController swim;
    private Animator animator;
    private CapsuleCollider capsule;
    private Terrain terrain;
    private Rigidbody body;

    private float nextLogTime;
    private float stopTime;

    private void Awake()
    {
        swim = GetComponent<UnderwaterSwimController>();
        capsule = GetComponent<CapsuleCollider>();
        animator = GetComponentInChildren<Animator>();
        terrain = FindAnyObjectByType<Terrain>();
        body = GetComponent<Rigidbody>();
        stopTime = Time.time + durationSeconds;
    }

    private void Update()
    {
        if (Time.time < nextLogTime) return;
        nextLogTime = Time.time + intervalSeconds;
        if (Time.time > stopTime)
        {
            Debug.Log("[SwimDiag] window elapsed, disabling.");
            enabled = false;
            return;
        }

        var sb = new StringBuilder("[SwimDiag] ");
        var pos = transform.position;
        var halfHeight = capsule != null ? capsule.height * 0.5f : 0f;

        sb.Append($"capsuleY={pos.y:F3} feetY={pos.y - halfHeight:F3} ");
        if (capsule != null) sb.Append($"(h={capsule.height:F3} r={capsule.radius:F3} c={capsule.center}) ");

        // The clamp in ApplyMovementLimits cannot leave the capsule centre below the sampled height
        // unless something writes transform.position after it, or it never ran. XZ tells us whether
        // SampleHeight is even being asked about a point on the terrain; the Rigidbody tells us
        // whether physics is the thing doing the overwriting.
        sb.Append($"posXZ=({pos.x:F2},{pos.z:F2}) ");
        if (swim != null) sb.Append($"clearanceSetting={swim.TerrainClearance:F2} ");
        if (terrain != null)
        {
            var tSize = terrain.terrainData != null ? terrain.terrainData.size : Vector3.zero;
            var tOrigin = terrain.transform.position;
            bool onTerrain = pos.x >= tOrigin.x && pos.x <= tOrigin.x + tSize.x &&
                             pos.z >= tOrigin.z && pos.z <= tOrigin.z + tSize.z;
            sb.Append($"onTerrain={onTerrain} tOrigin=({tOrigin.x:F1},{tOrigin.y:F1},{tOrigin.z:F1}) tSize=({tSize.x:F0},{tSize.y:F0},{tSize.z:F0}) ");
        }
        sb.Append(body != null
            ? $"rb=YES kinematic={body.isKinematic} gravity={body.useGravity} rbVelY={body.linearVelocity.y:F3} "
            : "rb=none ");

        if (terrain != null)
        {
            var groundY = terrain.SampleHeight(pos) + terrain.transform.position.y;
            sb.Append($"terrainY={groundY:F3} distAboveGround={pos.y - halfHeight - groundY:F3} ");
        }

        // Reproduces SUPERCharacterAIO.cs:960. A downward ray whose origin is already below a
        // heightfield does not hit it, so this going false is the tell that the capsule is buried.
        sb.Append($"groundRay={Physics.Raycast(pos, Vector3.down, halfHeight + 0.25f, -1)} ");

        if (swim != null) sb.Append($"IsInWater={swim.IsInWater} ");

        if (animator != null)
        {
            // Where the model actually renders. SUPER's stickRendererToCapsuleBottom pins this to
            // the capsule bottom every Update, which is only correct if the FBX root is at the feet.
            sb.Append($"modelY={animator.transform.position.y:F3} ");

            var st = animator.GetCurrentAnimatorStateInfo(0);
            sb.Append($"state={NameOf(st.shortNameHash)} nt={st.normalizedTime % 1f:F2} ");

            var clips = animator.GetCurrentAnimatorClipInfo(0);
            if (clips.Length == 0)
            {
                sb.Append("clip=<none> ");
            }
            else
            {
                var clip = clips[0].clip;
                // The whole question: a Generic clip bound to a Humanoid avatar is silently
                // ignored, so the state machine looks perfect and nothing moves.
                sb.Append($"clip='{clip.name}' human={clip.isHumanMotion} len={clip.length:F2} ");
            }

            sb.Append($"InWater={GetBool("InWater")} Grounded={GetBool("Grounded")} Idle={GetBool("Idle")} ");
            sb.Append($"IsSwimming={GetBool("IsSwimming")} SwimSpeed={GetFloat("SwimSpeed"):F3} Vel={GetFloat("Vel"):F3} ");
            sb.Append($"avatarHuman={(animator.avatar != null ? animator.avatar.isHuman.ToString() : "<no avatar>")}");
        }

        Debug.Log(sb.ToString());
        LogCameras();
    }

    /// <summary>
    /// Two full-screen cameras at equal depth render in an undefined order, so "which camera am I
    /// actually looking through" is not answerable from the scene file. Log every enabled one with
    /// its pose relative to the player, and let the numbers say it.
    /// </summary>
    private void LogCameras()
    {
        var sb = new StringBuilder("[SwimDiag/cam] ");
        foreach (var cam in FindObjectsByType<Camera>(FindObjectsInactive.Exclude))
        {
            if (!cam.enabled) continue;
            var offset = cam.transform.position - transform.position;
            sb.Append($"'{cam.name}' depth={cam.depth} disp={cam.targetDisplay} " +
                      $"offsetFromPlayer={offset} dist={offset.magnitude:F2} " +
                      $"euler={cam.transform.eulerAngles} | ");
        }
        sb.Append($"Camera.main='{(Camera.main != null ? Camera.main.name : "<none>")}' ");
        sb.Append($"bodyYaw={transform.eulerAngles.y:F1}");
        Debug.Log(sb.ToString());
        LogFraming();
    }

    /// <summary>
    /// Settles "off-centre framing" vs "body facing the wrong way", which look similar in a
    /// screenshot and have completely different fixes. Viewport coordinates are 0..1 with
    /// (0.5, 0.5) dead centre, so the numbers answer it directly instead of by eye.
    /// </summary>
    private void LogFraming()
    {
        var super = GetComponent<SUPERCharacterAIO>();
        var cam = super != null ? super.playerCamera : Camera.main;
        if (cam == null) return;

        // The point SUPER actually orbits around (SUPERCharacterAIO.cs:636).
        var headPos = transform.position + Vector3.up * (super != null ? super.standingEyeHeight : 0.8f);
        var rootVp = cam.WorldToViewportPoint(transform.position);
        var headVp = cam.WorldToViewportPoint(headPos);

        var sb = new StringBuilder("[SwimDiag/frame] ");
        sb.Append($"rootVP=({rootVp.x:F3},{rootVp.y:F3}) headVP=({headVp.x:F3},{headVp.y:F3}) ");

        // Where the player's head actually renders — the humanoid rig, not SUPER's assumed offset.
        if (animator != null && animator.isHuman)
        {
            var headBone = animator.GetBoneTransform(HumanBodyBones.Head);
            if (headBone != null)
            {
                var boneVp = cam.WorldToViewportPoint(headBone.position);
                sb.Append($"headBoneVP=({boneVp.x:F3},{boneVp.y:F3}) headBoneY={headBone.position.y:F3} ");
            }

            // The whole question for the framing complaint: does the *rendered* body sit on its own
            // GameObject, or has unbaked root motion slid it sideways? The transform can be dead
            // centre while the mesh is a metre off, and only this number tells them apart.
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips != null)
            {
                var d = hips.position - animator.transform.position;
                sb.Append($"hipsOffsetFromModelRoot=({d.x:F3},{d.y:F3},{d.z:F3}) horiz={new Vector2(d.x, d.z).magnitude:F3} ");
            }

            var m = animator.transform.position - transform.position;
            sb.Append($"modelRootOffsetFromCapsule=({m.x:F3},{m.y:F3},{m.z:F3}) ");
        }

        var camYaw = cam.transform.eulerAngles.y;
        var bodyYaw = transform.eulerAngles.y;
        sb.Append($"camYaw={camYaw:F1} bodyYaw={bodyYaw:F1} delta={Mathf.DeltaAngle(camYaw, bodyYaw):F1} ");
        if (super != null)
            sb.Append($"rotateToCamFwd={super.rotateCharacterToCameraForward} isIdle={super.isIdle} " +
                      $"eyeH={super.standingEyeHeight} fov={cam.fieldOfView} aspect={cam.aspect:F2}");

        Debug.Log(sb.ToString());
    }

    private static readonly string[] StateNames =
        { "Idle", "Moving", "Moving_Fast", "AirBorn", "Swimming", "Slide" };

    private static string NameOf(int shortNameHash)
    {
        foreach (var n in StateNames)
        {
            if (Animator.StringToHash(n) == shortNameHash) return n;
        }
        return $"<unknown {shortNameHash}>";
    }

    private string GetBool(string name) => Has(name) ? animator.GetBool(name).ToString() : "-";
    private float GetFloat(string name) => Has(name) ? animator.GetFloat(name) : float.NaN;

    private bool Has(string name)
    {
        foreach (var p in animator.parameters)
        {
            if (p.name == name) return true;
        }
        return false;
    }
}
