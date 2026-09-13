using UnityEngine;

/// <summary>
/// ROBOT　操作模式（上帝视角）下的机器人控制器。
///   · 移动：WASD 固定世界轴 —— W→X+ S→X- A→Z+ D→Z-；速度与 RobotController 一致，Shift 加速。
///   · 领域：按 E 开 / 关电子领域（IFieldEmitter）。
///   · 充电：靠近充电桩（纯距离，无需对准）按 F 充电，绕开被冻结的 Interactor。
///   · 死亡：机器人死亡中冻结输入（不移动 / 不开领域 / 不充电），仅保留重力。
///
/// 交互提示：操作模式下 Interactor 被冻结，无法提供焦点。本组件把每帧算出的
/// "当前贴近的充电桩"通过 NearestDock 暴露出去，InteractionPrompt 会在没有
/// 任何 Interactor 持有控制权时回退读取它 —— 判定条件与按 F 充电完全一致，
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

    [Header("开领域")]
    [Tooltip("开 / 关领域的键；留 None 则跟随 RobotController 的 fieldKey")]
    public KeyCode fieldKeyOverride = KeyCode.None;

    [Header("充电桩（操作模式下按此键充电）")]
    public KeyCode chargeKey = KeyCode.F;
    [Tooltip("多近算\"在充电桩旁\"（纯距离，上帝视角无需对准）。\n交互提示也用这个范围，两者永远一致")]
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
    [Tooltip("当前贴近的充电桩（= 提示 UI 会显示的那个）")]
    [SerializeField] private string nearestDockReadout = "(无)";
    [SerializeField] private bool chargingReadout;

    private CharacterController controller;
    private RobotController robotController;
    private IFieldEmitter fieldEmitter;
    private CharacterDeathHandler deathHandler;
    private EnergySystem energySystem;
    private float verticalVelocity;
    private ChargingDock chargingFrom;   // 当前正在从哪个充电桩充电
    private ChargingDock nearestDock;    // 本帧算出的最近充电桩（供提示 UI 读）

    /// <summary>
    /// 当前贴近、按 F 即可充电的充电桩（没有则 null）。
    /// 本组件被禁用或机器人死亡中时恒为 null，提示不会残留。
    /// </summary>
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
    }

    private void OnEnable() { verticalVelocity = 0f; }

    private void OnDisable()
    {
        verticalVelocity = 0f;
        chargingFrom = null;
        nearestDock = null;              // 退出操作模式时清掉，避免提示卡住
        nearestDockReadout = "(无)";
        chargingReadout = false;
    }

    private void Update()
    {
        // 死亡中：冻结输入（不移动 / 不开领域 / 不充电），仅施加重力
        if (deathHandler != null && deathHandler.IsDying)
        {
            chargingFrom = null;
            nearestDock = null;          // 死亡中不提示
            nearestDockReadout = "(死亡中)";
            chargingReadout = false;
            if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            verticalVelocity += gravity * Time.deltaTime;
            controller.Move(Vector3.up * verticalVelocity * Time.deltaTime);
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

        if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
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
        // 每帧算一次，同时供"按 F 充电"和"交互提示"使用 —— 单一数据源，两者不可能不同步
        nearestDock = FindNearestDock();

        // 按 F 且旁边有充电桩 → 认定为充电目标
        if (nearestDock != null && Input.GetKeyDown(chargeKey))
            chargingFrom = nearestDock;

        if (chargingFrom != null)
        {
            // 走开了 / 没电池 / 已充满 → 停止
            if (nearestDock != chargingFrom || energySystem == null) chargingFrom = null;
            else if (!chargingFrom.ChargeDirect(energySystem)) chargingFrom = null;
        }

        nearestDockReadout = nearestDock != null ? nearestDock.name : "(无)";
        chargingReadout = chargingFrom != null;
    }

    // 纯距离找最近的、机器人可用的充电桩（无需相机对准）
    private ChargingDock FindNearestDock()
    {
        var list = InteractableBase.All;
        ChargingDock best = null;
        float bestD = chargeRange;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] is ChargingDock dock && dock.CanBeUsedBy(InteractorType.Robot))
            {
                float d = Vector3.Distance(transform.position, dock.InteractTransform.position);
                if (d <= bestD) { bestD = d; best = dock; }
            }
        }
        return best;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, chargeRange);
    }
}