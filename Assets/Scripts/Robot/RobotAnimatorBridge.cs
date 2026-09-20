using UnityEngine;

/// <summary>
/// ROBOT Animation bridge: feeds the robot's [actual] motion state to the Animator on the child object (Rob13 model).
///
/// Design (same idea as PlayerAnimator): never reads input, only looks at CharacterController.velocity.
/// So it works automatically in both normal mode (RobotController) and Operation Mode (RobotConsoleMover) --
/// it doesn't matter who calls controller.Move(); the animation plays however the body moves.
///
/// Maps to Rob13.controller parameters (bundled with the Droll Robots package):
///   Speed (float)  ← horizontal speed normalized 0~1 (1 = run speed)
///   run   (float)  ← whether in the running range 0/1 (smoothed)
///   Side  (float)  ← always 0 (this game has no strafe animation needs)
///   Jump  (trigger)← fired once on takeoff (subscribes to CharacterMotorBase.Jumped)
/// Only writes parameters that actually exist in the animator, so swapping models won't spam "Parameter does not exist".
///
/// Compatibility guard: the Droll Robots Rob13 prefab originally ships with CharacterController + Rob13Ctrl + Root Motion,
/// all three of which conflict with this game's CharacterController movement system. The prefab has been cleaned; this is a runtime safety net
/// so the robot won't break if that package is ever re-imported.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public class RobotAnimatorBridge : MonoBehaviour
{
    [Header("References (leave empty to auto-find in children)")]
    [SerializeField] private Animator animator;
    [Tooltip("Used to get runSpeed for normalization; leave empty to use the RobotController on the same object")]
    [SerializeField] private RobotController robotController;

    [Header("Tuning")]
    [Tooltip("Smoothing time for the Speed parameter; larger = softer transitions")]
    public float speedDampTime = 0.1f;
    [Tooltip("Horizontal speed above this fraction counts as running (0.5 = midpoint between walk and run speed)")]
    [Range(0f, 1f)] public float runThreshold = 0.5f;
    [Tooltip("Transition time for the run parameter from 0 to 1")]
    public float runDampTime = 0.15f;
    [Tooltip("Fallback run speed when no RobotController is found")]
    public float fallbackRunSpeed = 6f;
    [Tooltip("Normalize the Speed parameter by [walk speed]: reaches 1 when walking; walk/run is distinguished by the run parameter.\n" +
             "Rob13's 2D blend tree expects Speed to be 0 or 1 (the original author fed Input.GetAxis); normalizing by run speed leaves walking at 0.67, blending with idle into slow motion")]
    public bool normalizeByWalkSpeed = true;
    [Tooltip("Value for the animator's speedMultiplier parameter. Rob13.controller defaults to 2 (all animations at double speed); the original author's script forces it back to 1 every frame")]
    public float animationSpeedMultiplier = 1f;

    [Header("Animator Parameter Names (match Rob13.controller)")]
    public string speedParam = "Speed";
    public string runParam = "run";
    public string sideParam = "Side";
    public string jumpParam = "Jump";
    public string speedMultiplierParam = "speedMultiplier";

    private CharacterController controller;
    private CharacterMotorBase motor;
    private int speedId, runId, sideId, jumpId, speedMulId;
    private bool hasSpeed, hasRun, hasSide, hasJump, hasSpeedMul;
    private float runValue;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (robotController == null) robotController = GetComponent<RobotController>();
        motor = GetComponent<CharacterMotorBase>();

        // Animator is on the child (model); exclude one on the root (this game has none there, but just in case)
        if (animator == null)
        {
            foreach (var a in GetComponentsInChildren<Animator>(true))
                if (a.gameObject != gameObject) { animator = a; break; }
        }
        if (animator == null)
        {
            Debug.LogWarning("[RobotAnimatorBridge] No Animator found on children; animation bridge disabled", this);
            enabled = false;
            return;
        }

        // -- Compatibility guard: remove conflicting components possibly left over from the Droll Robots prefab --
        GuardAgainstVendorController();

        // -- Only write parameters that actually exist --
        speedId = Animator.StringToHash(speedParam);
        runId   = Animator.StringToHash(runParam);
        sideId  = Animator.StringToHash(sideParam);
        jumpId  = Animator.StringToHash(jumpParam);
        speedMulId = Animator.StringToHash(speedMultiplierParam);
        foreach (var p in animator.parameters)
        {
            if (p.nameHash == speedMulId && p.type == AnimatorControllerParameterType.Float) hasSpeedMul = true;
            if (p.nameHash == speedId && p.type == AnimatorControllerParameterType.Float) hasSpeed = true;
            if (p.nameHash == runId   && p.type == AnimatorControllerParameterType.Float) hasRun = true;
            if (p.nameHash == sideId  && p.type == AnimatorControllerParameterType.Float) hasSide = true;
            if (p.nameHash == jumpId  && p.type == AnimatorControllerParameterType.Trigger) hasJump = true;
        }
        if (!hasSpeed)
            Debug.LogWarning($"[RobotAnimatorBridge] Animator has no Float parameter \"{speedParam}\"; robot will have no walk/run animation", this);
    }

    private void OnEnable()
    {
        if (motor != null) motor.Jumped += OnJumped;
    }

    private void OnDisable()
    {
        if (motor != null) motor.Jumped -= OnJumped;
    }

    private void Update()
    {
        if (animator == null) return;

        // Actual horizontal speed (regardless of who is Moving this CharacterController)
        Vector3 v = controller.velocity;
        float planar = new Vector3(v.x, 0f, v.z).magnitude;

        float runSpeed  = robotController != null ? robotController.runSpeed  : fallbackRunSpeed;
        float walkSpeed = robotController != null ? robotController.walkSpeed : runSpeed * 0.66f;
        if (runSpeed <= 0f) runSpeed = fallbackRunSpeed;

        float speedRef = normalizeByWalkSpeed ? Mathf.Max(0.01f, walkSpeed) : runSpeed;
        float normalized = Mathf.Clamp01(planar / speedRef);
        if (hasSpeed) animator.SetFloat(speedId, normalized, speedDampTime, Time.deltaTime);
        if (hasSpeedMul) animator.SetFloat(speedMulId, animationSpeedMultiplier);

        // Walk / run split: past the threshold between walk~run counts as running
        float runLine = Mathf.Lerp(walkSpeed, runSpeed, runThreshold);
        float targetRun = planar > runLine ? 1f : 0f;
        runValue = runDampTime > 0f
            ? Mathf.MoveTowards(runValue, targetRun, Time.deltaTime / runDampTime)
            : targetRun;
        if (hasRun) animator.SetFloat(runId, runValue);

        if (hasSide) animator.SetFloat(sideId, 0f);
    }

    private void OnJumped()
    {
        if (animator != null && hasJump) animator.SetTrigger(jumpId);
    }

    /// Clean up things shipped with the Droll Robots prefab that conflict with this game (prefab already cleaned manually; this is a safety net)
    private void GuardAgainstVendorController()
    {
        // 1) Root Motion makes the model move on its own, detaching from the root CharacterController
        if (animator.applyRootMotion)
        {
            animator.applyRootMotion = false;
            Debug.Log("[RobotAnimatorBridge] Disabled Apply Root Motion on the child Animator", animator);
        }

        // 2) A second CharacterController on a child would push against the root one
        foreach (var cc in GetComponentsInChildren<CharacterController>(true))
        {
            if (cc == controller) continue;
            cc.enabled = false;
            Debug.LogWarning($"[RobotAnimatorBridge] Extra CharacterController on child {cc.name}, disabled. Consider removing it from the prefab", cc);
        }

        // 3) The package's Rob13Ctrl also reads WASD / rotates / binds number keys, conflicting with this game's controller
        foreach (var mb in GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null || mb == this) continue;
            string tn = mb.GetType().Name;
            if (tn == "Rob13Ctrl" || tn == "RobotLift")
            {
                mb.enabled = false;
                Debug.LogWarning($"[RobotAnimatorBridge] Child {mb.name} has {tn}, which conflicts with this game's controller, disabled. Consider removing it from the prefab", mb);
            }
        }
    }
}
