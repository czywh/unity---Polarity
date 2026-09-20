using UnityEngine;

/// <summary>
/// PLAYER-06　动画驱动：把移动内核（CharacterMotorBase）的实际状态喂给 Animator。
///
/// 设计要点：完全不读输入——身体怎么动，动画就怎么播，数据只有一个来源，
/// 从根上避免"动画和移动对不上"。
///
/// 对应的 Animator Controller 参数（用 BuildPlayerAnimatorController 生成的那套）：
///   Speed   (float)  ← 实际水平速度归一化 0~1，驱动 Idle/Walk/Run 混合树
///   Grounded(bool)   ← 是否在地面
///   Jump    (trigger)← 起跳事件触发一次
///
/// Animator 可以在本物体或子物体（模型）上，留空会自动向下查找。
///
/// 两个坑的处理：
///   · Grounded 不能只读 motor.IsGrounded —— 角色被切走 / 死亡时 motor 是禁用的，值会停在最后一帧；
///     此时改读 CharacterController.isGrounded（PassiveFall 仍在 Move，它是准的）。否则掉死时
///     Jump 状态永远等不到 Grounded，跳跃动画循环到天荒地老。
///   · 死亡 / 复活时强制回到 Locomotion 并清掉 Jump 触发器，不让上一条命的动画状态漏到下一条命。
/// </summary>
public class PlayerAnimator : MonoBehaviour
{
    [Header("引用（留空自动获取）")]
    [SerializeField] private CharacterMotorBase motor;
    [SerializeField] private Animator animator;

    [Header("调参")]
    [Tooltip("Speed 参数的平滑时间，越大过渡越柔")]
    [SerializeField] private float speedDampTime = 0.1f;

    [Header("状态复位")]
    [Tooltip("死亡 / 复活时强制回到的状态名（Base Layer 里的）")]
    [SerializeField] private string locomotionStateName = "Locomotion";

    private static readonly int SpeedId = Animator.StringToHash("Speed");
    private static readonly int GroundedId = Animator.StringToHash("Grounded");
    private static readonly int JumpId = Animator.StringToHash("Jump");

    private CharacterController controller;
    private CharacterDeathHandler death;
    private int locomotionHash;

    private void Reset()
    {
        motor = GetComponent<CharacterMotorBase>();
        animator = GetComponentInChildren<Animator>();
    }

    private void Awake()
    {
        if (motor == null) motor = GetComponent<CharacterMotorBase>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        if (motor == null) Debug.LogError("PlayerAnimator: 未找到 CharacterMotorBase", this);
        if (animator == null) Debug.LogError("PlayerAnimator: 未找到 Animator", this);

        controller = GetComponent<CharacterController>();
        death = GetComponent<CharacterDeathHandler>();
        locomotionHash = Animator.StringToHash(locomotionStateName);
    }

    private void OnEnable()
    {
        if (motor != null) motor.Jumped += OnJumped;
        if (death != null)
        {
            death.onDeath?.AddListener(ResetToLocomotion);
            death.onRespawn?.AddListener(ResetToLocomotion);
        }
    }

    private void OnDisable()
    {
        if (motor != null) motor.Jumped -= OnJumped;
        if (death != null)
        {
            death.onDeath?.RemoveListener(ResetToLocomotion);
            death.onRespawn?.RemoveListener(ResetToLocomotion);
        }
    }

    /// 死亡 / 复活：清触发器、标记落地、强制回 Locomotion。上一条命的 Jump 状态不能漏进下一条命
    private void ResetToLocomotion()
    {
        if (animator == null || !animator.isActiveAndEnabled) return;
        animator.ResetTrigger(JumpId);
        animator.SetBool(GroundedId, true);
        animator.SetFloat(SpeedId, 0f);
        if (animator.HasState(0, locomotionHash)) animator.Play(locomotionHash, 0, 0f);
    }

    /// 落地判定：motor 启用时用它（含 groundCheck 球检）；被切走 / 死亡中 motor 禁用，改读 CC
    private bool IsGroundedNow()
    {
        if (motor != null && motor.enabled) return motor.IsGrounded;
        return controller != null && controller.isGrounded;
    }

    private void Update()
    {
        if (motor == null || animator == null) return;

        // 实际水平速度 → 0~1（walkSpeed 落在中段、runSpeed 到 1）
        float normalized = motor.MaxSpeed > 0f
            ? Mathf.Clamp01(motor.PlanarSpeed / motor.MaxSpeed)
            : 0f;

        animator.SetFloat(SpeedId, normalized, speedDampTime, Time.deltaTime);
        animator.SetBool(GroundedId, IsGroundedNow());
    }

    // 基类起跳事件 → 播放跳跃（死亡中不响应）
    private void OnJumped()
    {
        if (animator == null) return;
        if (death != null && death.IsDying) return;
        animator.SetTrigger(JumpId);
    }
}