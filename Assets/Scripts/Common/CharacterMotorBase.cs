using UnityEngine;

/// <summary>
/// 角色移动内核基类：玩家与机器人共用的"怎么动"。
///
/// 包含：相机相对移动 + 平滑转向到移动方向 + 重力 + 土狼时间/跳跃缓冲 + 统一地面检测，
/// 以及一组对外只读状态（供动画 / 视觉表现读取）。
///
/// 子类只需：① 在各自 Header 下声明速度/跳跃参数，并用抽象属性把它们喂进来；
///          ② 需要额外输入（如机器人能力键）时重写 HandleExtraInput()。
/// 控制权交接：由 CharacterSwitcher 启用 / 禁用子类组件。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public abstract class CharacterMotorBase : MonoBehaviour
{
    [Header("转向")]
    public float turnSmoothTime = 0.1f;

    [Header("重力 / 跳跃手感")]
    public float gravity = -25f;
    [Tooltip("离开地面后仍允许起跳的宽限时间（土狼时间）")]
    public float coyoteTime = 0.1f;
    [Tooltip("落地前提前按跳的缓冲时间")]
    public float jumpBufferTime = 0.1f;

    [Header("地面检测")]
    [Tooltip("脚底的空物体；留空则退回用 CharacterController.isGrounded")]
    public Transform groundCheck;
    public float groundCheckRadius = 0.2f;
    public LayerMask groundMask = ~0;

    [Header("相机")]
    [Tooltip("决定移动方向的相机；留空自动用 Camera.main")]
    public Transform cameraTransform;

    [Header("运行时锁")]
    [Tooltip("交互 / 过场时置 true：冻结水平移动，但仍受重力贴地")]
    public bool motionLocked = false;

    [Header("幽灵态（进入影响重力的电子领域时免重力悬浮）")]
    [Tooltip("悬浮时上升 / 下降速度")]
    public float phantomVerticalSpeed = 4f;
    [Tooltip("上升键")]
    public KeyCode phantomUpKey = KeyCode.Space;
    [Tooltip("下降键")]
    public KeyCode phantomDownKey = KeyCode.LeftControl;

    // 子类决定是否具备幽灵态能力（玩家 true，机器人 false）
    protected virtual bool CanPhantom => false;
    // 当前是否处于幽灵态（在影响重力的领域内）
    public bool InPhantom { get; private set; }

    // —— 子类提供的参数（基类只读取，不持有具体字段）——
    protected abstract float WalkSpeed { get; }
    protected abstract float RunSpeed { get; }
    protected abstract float JumpHeight { get; }
    protected abstract bool CanRunNow { get; }   // 是否允许奔跑（玩家恒 true，机器人看 canRun）
    protected abstract bool CanJumpNow { get; }  // 是否允许跳跃

    // —— 对外只读状态 ——
    public bool IsGrounded { get; private set; }
    public bool IsRunning { get; private set; }
    public bool IsMoving { get; private set; }
    public float PlanarSpeed { get; private set; }
    public float MaxSpeed => RunSpeed;
    public float VerticalVelocity => verticalVelocity;

    /// 起跳成功的那一帧触发（供动画播放跳跃、音效等订阅）
    public event System.Action Jumped;

    protected CharacterController controller;
    private float turnSmoothVelocity;
    private float verticalVelocity;
    private float lastGroundedTime = -99f;
    private float lastJumpPressedTime = -99f;

    protected virtual void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    protected virtual void Start()
    {
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    protected virtual void Update()
    {
        UpdateGrounded();

        // 幽灵态判定：具备能力 且 处于"影响重力的领域"内
        InPhantom = CanPhantom
            && ElectricFieldManager.Instance != null
            && ElectricFieldManager.Instance.IsInsidePhantomField(transform.position);

        // —— 输入集中读取：之后接 InputManager 只改这一段 ——
        float inputX = Input.GetAxisRaw("Horizontal");
        float inputZ = Input.GetAxisRaw("Vertical");
        bool runHeld = CanRunNow && Input.GetKey(KeyCode.LeftShift);
        if (CanJumpNow && Input.GetButtonDown("Jump")) lastJumpPressedTime = Time.time;

        Vector3 planar;
        if (motionLocked)
        {
            planar = Vector3.zero;
            IsRunning = false;
        }
        else
        {
            planar = MoveHorizontal(inputX, inputZ, runHeld);
        }

        ApplyGravityAndJump();

        Vector3 velocity = planar + Vector3.up * verticalVelocity;
        controller.Move(velocity * Time.deltaTime);

        PlanarSpeed = new Vector3(planar.x, 0f, planar.z).magnitude;
        IsMoving = PlanarSpeed > 0.05f;

        // 子类专属输入（机器人能力键等）。基类不关心具体内容。
        HandleExtraInput();
    }

    /// 相机相对方向 + 平滑转向到移动方向，返回这一帧的水平速度向量
    private Vector3 MoveHorizontal(float inputX, float inputZ, bool runHeld)
    {
        Vector3 input = new Vector3(inputX, 0f, inputZ);
        if (input.sqrMagnitude < 0.01f)
        {
            IsRunning = false;
            return Vector3.zero;
        }
        input.Normalize();

        float camYaw = cameraTransform != null ? cameraTransform.eulerAngles.y : 0f;
        float targetAngle = Mathf.Atan2(input.x, input.z) * Mathf.Rad2Deg + camYaw;

        float angle = Mathf.SmoothDampAngle(
            transform.eulerAngles.y, targetAngle, ref turnSmoothVelocity, turnSmoothTime);
        transform.rotation = Quaternion.Euler(0f, angle, 0f);

        IsRunning = runHeld;
        float speed = runHeld ? RunSpeed : WalkSpeed;

        Vector3 moveDir = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
        return moveDir * speed;
    }

    private void ApplyGravityAndJump()
    {
        // 幽灵态：免重力，Space 上升 / Ctrl 下降，直接控速
        if (InPhantom)
        {
            float v = 0f;
            if (Input.GetKey(phantomUpKey)) v += phantomVerticalSpeed;
            if (Input.GetKey(phantomDownKey)) v -= phantomVerticalSpeed;
            verticalVelocity = v;
            lastJumpPressedTime = -99f;   // 清掉跳跃缓冲，避免退出幽灵态时误跳
            return;
        }

        if (IsGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        bool coyoteOk = Time.time - lastGroundedTime <= coyoteTime;
        bool jumpBuffered = Time.time - lastJumpPressedTime <= jumpBufferTime;
        if (coyoteOk && jumpBuffered)
        {
            verticalVelocity = Mathf.Sqrt(-2f * gravity * JumpHeight);
            lastJumpPressedTime = -99f;
            lastGroundedTime = -99f;
            Jumped?.Invoke();
        }

        verticalVelocity += gravity * Time.deltaTime;
    }

    private void UpdateGrounded()
    {
        if (groundCheck != null)
            IsGrounded = Physics.CheckSphere(
                groundCheck.position, groundCheckRadius, groundMask, QueryTriggerInteraction.Ignore);
        else
            IsGrounded = controller.isGrounded;

        if (IsGrounded) lastGroundedTime = Time.time;
    }

    /// 子类重写以处理自己的额外输入（默认什么都不做）
    protected virtual void HandleExtraInput() { }

    /// 切走（禁用）时清理速度，避免切回来还带着旧的下坠速度
    protected virtual void OnDisable()
    {
        verticalVelocity = 0f;
        PlanarSpeed = 0f;   // 切走时归零，避免动画卡在行走
        IsMoving = false;
        IsRunning = false;
    }

    protected virtual void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
}