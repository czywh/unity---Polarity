using UnityEngine;

/// <summary>
/// PROP-02　可移动充电桩：继承 ChargingDock（保留充电能力），
/// 额外支持「玩家聚焦 + 按交互键」在两个节点 A / B 之间平移。
///
/// 交互区分：
///   · 玩家聚焦 + 按交互键 → 在 A↔B 间切换并平移（充电桩位移谜题）
///   · 机器人聚焦           → 照常充电（继承自 ChargingDock）
///
/// 默认 access = Both，这样玩家能聚焦它来移动、机器人能聚焦它来充电。
/// </summary>
public class ChargingDockMovable : ChargingDock
{
    [Header("移动节点（玩家按交互键在两点间切换）")]
    [Tooltip("节点 A（空物体，摆在第一个位置）")]
    public Transform waypointA;
    [Tooltip("节点 B（空物体，摆在第二个位置）")]
    public Transform waypointB;
    public float moveSpeed = 3f;
    [Tooltip("移动途中是否锁定，不响应再次按键（避免半路反向）")]
    public bool lockWhileMoving = true;
    [Tooltip("开局是否吸附到 A 点")]
    public bool snapToAOnStart = true;

    [Header("调试（运行时只读）")]
    [SerializeField] private int targetIndex;   // 0 = A，1 = B
    [SerializeField] private bool moving;

    private Vector3 PointA => waypointA != null ? waypointA.position : transform.position;
    private Vector3 PointB => waypointB != null ? waypointB.position : transform.position;
    private Vector3 TargetPos => targetIndex == 0 ? PointA : PointB;

    protected override void Reset()
    {
        // 可移动充电桩：玩家移动 + 机器人充电，都要能聚焦
        access = InteractAccess.Both;
    }

    private void Start()
    {
        if (snapToAOnStart && waypointA != null)
            transform.position = PointA;
        targetIndex = 0;
    }

    protected override void Update()
    {
        base.Update();   // 保留充电逻辑（机器人聚焦时充电）

        // 平移到当前目标节点
        Vector3 dest = TargetPos;
        if (Vector3.Distance(transform.position, dest) > 0.001f)
        {
            transform.position = Vector3.MoveTowards(transform.position, dest, moveSpeed * Time.deltaTime);
            moving = true;
        }
        else
        {
            moving = false;
        }
    }

    public override void OnInteract(Interactor interactor)
    {
        // 玩家按键 → 切换目标节点并平移
        if (interactor != null && interactor.type == InteractorType.Player)
        {
            if (lockWhileMoving && moving) return;   // 移动中忽略
            targetIndex = 1 - targetIndex;           // 0 <-> 1
            return;
        }

        // 其它（机器人）→ 交给父类（按键充电模式时有用）
        base.OnInteract(interactor);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 a = waypointA != null ? waypointA.position : transform.position;
        Vector3 b = waypointB != null ? waypointB.position : transform.position;
        Gizmos.color = Color.green; Gizmos.DrawWireSphere(a, 0.3f);
        Gizmos.color = Color.cyan;  Gizmos.DrawWireSphere(b, 0.3f);
        Gizmos.color = Color.yellow; Gizmos.DrawLine(a, b);
    }
}