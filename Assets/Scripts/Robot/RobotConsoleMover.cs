using UnityEngine;

/// <summary>
/// ROBOT　操作模式（上帝视角）下的机器人控制器。
///   · 移动：WASD 固定世界轴 —— W→X+ S→X- A→Z+ D→Z-；速度与 RobotController 一致，Shift 加速。
///   · 跳跃：Input Manager 的 "Jump"（默认空格），跳跃高度 / 是否允许跳跃取自 RobotController，
///          同样带土狼时间与跳跃缓冲，手感与非操作模式完全一致。
///   · 领域：按 E 开 / 关电子领域（IFieldEmitter）。
///   · 交互：靠近任何机器人可用的交互物（纯距离，无需对准）按 F —— 绕开被冻结的 Interactor：
///       充电桩走 ChargeDirect 持续充电；其它（如终点 LevelGoal）走 OnInteract 一次性触发。
///   · 死亡：机器人死亡中冻结输入（不移动 / 不开领域 / 不充电），仅保留重力。
///
/// 交互提示：操作模式下 Interactor 被冻结，无法提供焦点。本组件把每帧算出的
/// "当前贴近的交互物"通过 NearestInteractable 暴露出去，InteractionPrompt 会在没有
/// 任何 Interactor 持有控制权时回退读取它 —— 判定条件与按 F 完全一致，
/// 提示亮起 = 这一刻按 F 一定有效。
///
/// 平时禁用；由 OperationModeController 进入操作模式时启用、退出时禁用。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class RobotConsoleMover : MonoBehaviour
{
    [Header("移动（速度默认取同物体 RobotController，保持一致）")]
    public KeyCode runKey = KeyCode.LeftShift;
    [Tooltip("找不到 RobotController 时的备用走速")]
    public float fallbackWalkSpeed = 4f;
    [Tooltip("找不到 RobotController 时的备用跑速")]
    public float fallbackRunSpeed = 6f;

    [Header("跳跃（参数取自同物体 RobotController，保持一致）")]
    [Tooltip("操作模式下是否允许跳跃。再叠加 RobotController.canJump 一起判定")]
    public bool allowJump = true;
    [Tooltip("找不到 RobotController 时的备用跳跃高度")]
    public float fallbackJumpHeight = 1.2f;
    [Tooltip("离开地面后仍允许起跳的宽限时间（土狼时间）")]
    public float coyoteTime = 0.1f;
    [Tooltip("落地前提前按跳的缓冲时间")]
    public float jumpBufferTime = 0.1f;

    [Header("开领域")]
    [Tooltip("开 / 关领域的键；留 None 则跟随 RobotController 的 fieldKey")]
    public KeyCode fieldKeyOverride = KeyCode.None;

    [Header("交互（操作模式下按此键：充电桩=充电，其它=触发）")]
    public KeyCode chargeKey = KeyCode.F;
    [Tooltip("多近算\"在交互物旁\"（纯距离，上帝视角无需对准）。\n交互提示也用这个范围，两者永远一致")]
    public float chargeRange = 3f;

    [Header("朝向鼠标")]
    [Tooltip("操作模式下机器人始终面朝鼠标（只转 Y 轴）")]
    public bool faceMouse = true;
    [Tooltip("转向速度（度/秒）；0 或负 = 瞬间对准")]
    public float turnSpeed = 720f;
    [Tooltip("求鼠标落点用的相机；留空取 Camera.main")]
    public Camera aimCamera;

    [Header("重力（保持贴地）")]
    public float gravity = -25f;

    [Header("调试（运行时只读）")]
    [Tooltip("当前贴近的交互物（= 提示 UI 会显示的那个）")]
    [SerializeField] private string nearestDockReadout = "(无)";
    [SerializeField] private bool chargingReadout;

    private CharacterController controller;
    private RobotController robotController;
    private IFieldEmitter fieldEmitter;
    private CharacterDeathHandler deathHandler;
    private EnergySystem energySystem;
    private float verticalVelocity;
    private float lastGroundedTime = -99f;
    private float lastJumpPressedTime = -99f;
    private Interactor robotInteractor;         // 本机器人自己的 Interactor（冻结中，只借它的身份传给 OnInteract）
    private ChargingDock chargingFrom;          // 当前正在从哪个充电桩充电
    private InteractableBase nearestInteractable;   // 本帧算出的最近交互物（供提示 UI 读）
    private InteractableBase focused;               // 已对其调用过 OnFocusEnter 的非充电桩交互物
    private ChargingDock nearestDock => nearestInteractable as ChargingDock;

    /// <summary>
    /// 当前贴近、按 F 即可交互的物体（没有则 null）。充电桩 / 终点 / 任何机器人可用的 InteractableBase。
    /// 本组件被禁用或机器人死亡中时恒为 null，提示不会残留。
    /// </summary>
    public InteractableBase NearestInteractable => isActiveAndEnabled ? nearestInteractable : null;

    /// <summary>兼容旧调用：当前贴近的充电桩（不是充电桩则 null）</summary>
    public ChargingDock NearestDock => isActiveAndEnabled ? nearestDock : null;

    /// <summary>是否正在充电中</summary>
    public bool IsCharging => chargingFrom != null;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        robotController = GetComponent<RobotController>();
        fieldEmitter = GetComponent<IFieldEmitter>();
        deathHandler = GetComponent<CharacterDeathHandler>();
        energySystem = GetComponent<EnergySystem>();
        robotInteractor = GetComponent<Interactor>();
    }

    private void OnEnable()
    {
        verticalVelocity = 0f;
        lastGroundedTime = -99f;
        lastJumpPressedTime = -99f;
    }

    private void OnDisable()
    {
        verticalVelocity = 0f;
        chargingFrom = null;
        ClearFocus();
        nearestInteractable = null;      // 退出操作模式时清掉，避免提示卡住
        nearestDockReadout = "(无)";
        chargingReadout = false;
    }

    private void Update()
    {
        // 死亡中：冻结输入，并且【不再自己施加重力】。
        // 死亡 / 传送由 CharacterDeathHandler 独占（与 PassiveFall 的约定一致）；
        // 若这里继续累积 verticalVelocity，复活传送后会带着几十 m/s 的下坠速度落地，
        // 可能一帧内穿透复活平台再次坠落，表现成"死了一次之后就再也死不掉"。
        if (deathHandler != null && deathHandler.IsDying)
        {
            chargingFrom = null;
            ClearFocus();
            nearestInteractable = null;  // 死亡中不提示
            nearestDockReadout = "(死亡中)";
            chargingReadout = false;
            verticalVelocity = 0f;
            lastJumpPressedTime = -99f;
            return;
        }

        // —— 移动：速度与 RobotController 一致，Shift 加速 ——
        float walk = robotController != null ? robotController.walkSpeed : fallbackWalkSpeed;
        float run  = robotController != null ? robotController.runSpeed  : fallbackRunSpeed;
        float speed = Input.GetKey(runKey) ? run : walk;

        float v = Input.GetAxisRaw("Vertical");    // W=+1, S=-1
        float h = Input.GetAxisRaw("Horizontal");  // D=+1, A=-1
        Vector3 dir = new Vector3(v, 0f, -h);      // W/S → X，A/D → Z（A 为 +Z 所以取 -h）
        if (dir.sqrMagnitude > 1f) dir.Normalize();

        // —— 地面检测：与 CharacterMotorBase 同一套（有 groundCheck 用球检，否则退回 CC） ——
        bool grounded = CheckGrounded();
        if (grounded) lastGroundedTime = Time.time;

        // —— 跳跃输入：沿用 Input Manager 的 "Jump"，与非操作模式同一个键 ——
        if (CanJumpNow && Input.GetButtonDown("Jump")) lastJumpPressedTime = Time.time;

        // —— 重力 + 跳跃（土狼时间 / 跳跃缓冲，手感对齐 CharacterMotorBase） ——
        if (grounded && verticalVelocity < 0f) verticalVelocity = -2f;

        bool coyoteOk = Time.time - lastGroundedTime <= coyoteTime;
        bool jumpBuffered = Time.time - lastJumpPressedTime <= jumpBufferTime;
        if (coyoteOk && jumpBuffered)
        {
            float jumpHeight = robotController != null ? robotController.jumpHeight : fallbackJumpHeight;
            verticalVelocity = Mathf.Sqrt(-2f * gravity * jumpHeight);
            lastJumpPressedTime = -99f;
            lastGroundedTime = -99f;
        }

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = dir * speed + Vector3.up * verticalVelocity;
        controller.Move(velocity * Time.deltaTime);

        // —— 开 / 关领域：E ——
        KeyCode fk = fieldKeyOverride != KeyCode.None
            ? fieldKeyOverride
            : (robotController != null ? robotController.fieldKey : KeyCode.E);
        if (Input.GetKeyDown(fk)) fieldEmitter?.ToggleField();

        // —— 朝向鼠标（只转 Y 轴） ——
        if (faceMouse) FaceMouse();

        // —— 充电桩：附近有充电桩时按 F 充电（纯距离判定） ——
        HandleCharging();
    }

    /// 是否允许跳跃：本组件开关 + RobotController.canJump 双重判定
    private bool CanJumpNow => allowJump && (robotController == null || robotController.canJump);

    /// 地面检测。RobotController 配了 groundCheck 就用它，保证和非操作模式判定一致
    private bool CheckGrounded()
    {
        if (robotController != null && robotController.groundCheck != null)
            return Physics.CheckSphere(
                robotController.groundCheck.position,
                robotController.groundCheckRadius,
                robotController.groundMask,
                QueryTriggerInteraction.Ignore);
        return controller.isGrounded;
    }

    // 求鼠标在机器人所在水平面上的落点，机器人朝那个点（只转 Y）
    private void FaceMouse()
    {
        Camera c = aimCamera != null ? aimCamera : Camera.main;
        if (c == null) return;

        Ray ray = c.ScreenPointToRay(Input.mousePosition);

        // 与机器人所在水平面（法线朝上、过机器人）求交
        Plane plane = new Plane(Vector3.up, transform.position);
        if (!plane.Raycast(ray, out float enter)) return;

        Vector3 hit = ray.GetPoint(enter);
        Vector3 look = hit - transform.position;
        look.y = 0f;
        if (look.sqrMagnitude < 0.0001f) return;   // 鼠标几乎压在自己身上，跳过

        Quaternion target = Quaternion.LookRotation(look.normalized, Vector3.up);
        transform.rotation = turnSpeed > 0f
            ? Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime)
            : target;
    }

    private void HandleCharging()
    {
        // 每帧算一次，同时供"按 F"和"交互提示"使用 —— 单一数据源，两者不可能不同步
        nearestInteractable = FindNearestInteractable();
        var dock = nearestDock;

        // —— 非充电桩的交互物（终点等）：维护焦点，让 IsFocused / 子类逻辑和正常模式一致 ——
        var other = dock == null ? nearestInteractable : null;
        if (other != focused)
        {
            ClearFocus();
            if (other != null && robotInteractor != null)
            {
                other.OnFocusEnter(robotInteractor);
                focused = other;
            }
        }

        if (Input.GetKeyDown(chargeKey))
        {
            if (dock != null)
                chargingFrom = dock;                       // 充电桩：进入持续充电
            else if (focused != null && robotInteractor != null)
                focused.OnInteract(robotInteractor);      // 其它：一次性触发（LevelGoal → 胜利）
        }

        if (chargingFrom != null)
        {
            // 走开了 / 没电池 / 已充满 → 停止
            if (dock != chargingFrom || energySystem == null) chargingFrom = null;
            else if (!chargingFrom.ChargeDirect(energySystem)) chargingFrom = null;
        }

        nearestDockReadout = nearestInteractable != null ? nearestInteractable.name : "(无)";
        chargingReadout = chargingFrom != null;
    }

    private void ClearFocus()
    {
        if (focused != null && robotInteractor != null) focused.OnFocusExit(robotInteractor);
        focused = null;
    }

    // 纯距离找最近的、机器人可用的交互物（无需相机对准）
    private InteractableBase FindNearestInteractable()
    {
        var list = InteractableBase.All;
        InteractableBase best = null;
        float bestD = chargeRange;
        for (int i = 0; i < list.Count; i++)
        {
            var it = list[i];
            if (it == null || !it.CanBeUsedBy(InteractorType.Robot)) continue;
            float d = Vector3.Distance(transform.position, it.InteractTransform.position);
            if (d <= bestD) { bestD = d; best = it; }
        }
        return best;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, chargeRange);
    }
}