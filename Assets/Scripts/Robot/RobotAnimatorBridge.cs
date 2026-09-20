using UnityEngine;

/// <summary>
/// ROBOT　动画桥：把机器人【实际】的运动状态喂给子物体上的 Animator（Rob13 模型）。
///
/// 设计要点（和 PlayerAnimator 同一思路）：完全不读输入，只看 CharacterController.velocity。
/// 因此普通模式（RobotController）和操作模式（RobotConsoleMover）都自动生效 ——
/// 谁在调 controller.Move() 无所谓，身体怎么动动画就怎么播。
///
/// 对应 Rob13.controller 的参数（Droll Robots 包自带）：
///   Speed (float)  ← 水平速度归一化 0~1（1 = 跑速）
///   run   (float)  ← 是否处于奔跑段 0/1（平滑）
///   Side  (float)  ← 恒 0（本作没有横向 strafe 动画需求）
///   Jump  (trigger)← 起跳时触发一次（订阅 CharacterMotorBase.Jumped）
/// 只会写动画机里真实存在的参数，换别的模型不会刷 "Parameter does not exist"。
///
/// 兼容性守卫：Droll Robots 的 Rob13 预制体原本自带 CharacterController + Rob13Ctrl + Root Motion，
/// 这三样和本作的 CharacterController 移动体系冲突。预制体已经清理过；这里再做一次运行时兜底，
/// 万一以后重新导入了那个包，也不会把机器人搞坏。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public class RobotAnimatorBridge : MonoBehaviour
{
    [Header("引用（留空自动向下查找）")]
    [SerializeField] private Animator animator;
    [Tooltip("用于取 runSpeed 做归一化；留空自动取同物体 RobotController")]
    [SerializeField] private RobotController robotController;

    [Header("调参")]
    [Tooltip("Speed 参数的平滑时间，越大过渡越柔")]
    public float speedDampTime = 0.1f;
    [Tooltip("水平速度超过 walkSpeed 的这个比例就算奔跑（0.5 = 走跑速度中点）")]
    [Range(0f, 1f)] public float runThreshold = 0.5f;
    [Tooltip("run 参数从 0 到 1 的过渡时间")]
    public float runDampTime = 0.15f;
    [Tooltip("找不到 RobotController 时的备用跑速")]
    public float fallbackRunSpeed = 6f;
    [Tooltip("Speed 参数按【走速】归一化：走路时就到 1，走/跑的区别交给 run 参数。\n" +
             "Rob13 的 2D 混合树期望 Speed 是 0 或 1（原作者喂的是 Input.GetAxis），按跑速归一化会让走路停在 0.67，和待机混成慢动作")]
    public bool normalizeByWalkSpeed = true;
    [Tooltip("动画机的 speedMultiplier 参数值。Rob13.controller 里默认是 2（所有动画双速播），原作者脚本每帧压回 1")]
    public float animationSpeedMultiplier = 1f;

    [Header("动画机参数名（对应 Rob13.controller）")]
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

        // Animator 在子物体（模型）上；排除掉挂在根上的（本作根上没有，但保险）
        if (animator == null)
        {
            foreach (var a in GetComponentsInChildren<Animator>(true))
                if (a.gameObject != gameObject) { animator = a; break; }
        }
        if (animator == null)
        {
            Debug.LogWarning("[RobotAnimatorBridge] 子物体上没找到 Animator，动画桥不工作", this);
            enabled = false;
            return;
        }

        // —— 兼容性守卫：清掉 Droll Robots 预制体可能残留的冲突组件 ——
        GuardAgainstVendorController();

        // —— 只写真实存在的参数 ——
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
            Debug.LogWarning($"[RobotAnimatorBridge] Animator 里没有 Float 参数 \"{speedParam}\"，机器人不会有走/跑动画", this);
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

        // 实际水平速度（无论是谁在 Move 这个 CharacterController）
        Vector3 v = controller.velocity;
        float planar = new Vector3(v.x, 0f, v.z).magnitude;

        float runSpeed  = robotController != null ? robotController.runSpeed  : fallbackRunSpeed;
        float walkSpeed = robotController != null ? robotController.walkSpeed : runSpeed * 0.66f;
        if (runSpeed <= 0f) runSpeed = fallbackRunSpeed;

        float speedRef = normalizeByWalkSpeed ? Mathf.Max(0.01f, walkSpeed) : runSpeed;
        float normalized = Mathf.Clamp01(planar / speedRef);
        if (hasSpeed) animator.SetFloat(speedId, normalized, speedDampTime, Time.deltaTime);
        if (hasSpeedMul) animator.SetFloat(speedMulId, animationSpeedMultiplier);

        // 走 / 跑分段：速度过了 walk~run 之间的阈值就算跑
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

    /// 清理 Droll Robots 预制体自带、与本作冲突的东西（预制体已手动清过，这里是兜底）
    private void GuardAgainstVendorController()
    {
        // 1) Root Motion 会让模型自己走，脱离根上的 CharacterController
        if (animator.applyRootMotion)
        {
            animator.applyRootMotion = false;
            Debug.Log("[RobotAnimatorBridge] 已关闭子物体 Animator 的 Apply Root Motion", animator);
        }

        // 2) 子物体上的第二个 CharacterController 会和根上的互相推
        foreach (var cc in GetComponentsInChildren<CharacterController>(true))
        {
            if (cc == controller) continue;
            cc.enabled = false;
            Debug.LogWarning($"[RobotAnimatorBridge] 子物体 {cc.name} 上有多余的 CharacterController，已禁用。建议从预制体删掉", cc);
        }

        // 3) 包自带的 Rob13Ctrl 也读 WASD / 转朝向 / 绑数字键，和本作控制器冲突
        foreach (var mb in GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null || mb == this) continue;
            string tn = mb.GetType().Name;
            if (tn == "Rob13Ctrl" || tn == "RobotLift")
            {
                mb.enabled = false;
                Debug.LogWarning($"[RobotAnimatorBridge] 子物体 {mb.name} 上的 {tn} 与本作控制器冲突，已禁用。建议从预制体删掉", mb);
            }
        }
    }
}
