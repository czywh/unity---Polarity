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
/// </summary>
public class PlayerAnimator : MonoBehaviour
{
    [Header("引用（留空自动获取）")]
    [SerializeField] private CharacterMotorBase motor;
    [SerializeField] private Animator animator;

    [Header("调参")]
    [Tooltip("Speed 参数的平滑时间，越大过渡越柔")]
    [SerializeField] private float speedDampTime = 0.1f;

    private static readonly int SpeedId = Animator.StringToHash("Speed");
    private static readonly int GroundedId = Animator.StringToHash("Grounded");
    private static readonly int JumpId = Animator.StringToHash("Jump");

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
        if (motor == null || animator == null) return;

        // 实际水平速度 → 0~1（walkSpeed 落在中段、runSpeed 到 1）
        float normalized = motor.MaxSpeed > 0f
            ? Mathf.Clamp01(motor.PlanarSpeed / motor.MaxSpeed)
            : 0f;

        animator.SetFloat(SpeedId, normalized, speedDampTime, Time.deltaTime);
        animator.SetBool(GroundedId, motor.IsGrounded);
    }

    // 基类起跳事件 → 播放跳跃
    private void OnJumped()
    {
        if (animator != null) animator.SetTrigger(JumpId);
    }
}