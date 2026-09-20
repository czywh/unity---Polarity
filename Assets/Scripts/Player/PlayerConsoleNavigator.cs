using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// PLAYER　操作模式下的"右键寻路"：WASD 在操作模式里控制机器人，玩家角色则通过
/// 右键点击场景，沿 GridSystem 的可走格用 A* 自动走到【离点击处最近的可走格】。
///
/// 流程：
///   · 右键 → 鼠标射线先打 bridge 层（grid.walkableMask），打不中再与网格平面求交，得到点击点；
///   · 点击点所在格若不可走，在 searchRadius 格内找【世界距离离点击点最近】的可走格作为终点；
///   · 起点 = 玩家所在格（不可走时同样就近兜底）；AStarPathfinder.FindPath 求路径；
///   · 逐格移动。只驱动水平方向 —— 竖直重力由 PassiveFall 负责（操作模式下 PlayerController 被冻结，
///     PassiveFall 正好接管），桥移动带人由 PlatformRider 负责，互不打架。
///   · 桥在走的过程中移动 / 断开：下一格变不可走就自动重算路径；终点也不可走了就停下。
///
/// 自给自足：自己判断当前是否处于操作模式（读 OperationModeController.InOperationMode），
/// 不需要改 OperationModeController，也不需要手动开关本组件。退出操作模式时自动停下。
///
/// 挂在 Player 根物体上（与 CharacterController / PlayerController 同物体）。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerConsoleNavigator : MonoBehaviour
{
    [Header("引用（留空自动查找）")]
    [Tooltip("网格；留空取 GridSystem.Instance")]
    public GridSystem grid;
    [Tooltip("操作模式管理器；留空自动查找")]
    public OperationModeController operationMode;
    [Tooltip("射线用的相机；留空取 Camera.main（操作模式下就是那台正交相机）")]
    public Camera rayCamera;

    [Header("输入")]
    [Tooltip("鼠标键：0 左键 / 1 右键 / 2 中键")]
    public int mouseButton = 1;

    [Header("寻路")]
    [Tooltip("点击处不可走时，向外找最近可走格的半径（格）")]
    public int searchRadius = 4;
    [Tooltip("是否允许斜向移动（八方向）")]
    public bool allowDiagonal = true;
    [Tooltip("到达某格中心的判定距离（世界单位，只比较 XZ）")]
    public float arriveThreshold = 0.15f;
    [Tooltip("多久没有明显前进算卡住（秒），卡住会重算一次路径")]
    public float stuckTime = 0.8f;

    [Header("移动")]
    [Tooltip("走速；≤0 时读取同物体 PlayerController.walkSpeed")]
    public float moveSpeedOverride = 0f;
    [Tooltip("转向速度（度/秒）")]
    public float turnSpeed = 720f;

    [Header("目标标记（可选）")]
    [Tooltip("场景里的一个物体（如发光圆环），寻路时摆到终点格上，到达后隐藏。留空不显示")]
    public Transform destinationMarker;
    [Tooltip("标记相对格心的抬高量，避免和桥面 Z-fighting")]
    public float markerYOffset = 0.05f;

    [Header("调试")]
    public bool verboseLog = false;
    public bool drawPathGizmos = true;
    [SerializeField] private bool navigatingReadout;
    [SerializeField] private string goalReadout = "(无)";

    // —— 对外只读：供 PlayerAnimator 驱动走路动画 ——
    public bool IsNavigating => path != null && pathIndex < path.Count;
    public float PlanarSpeed { get; private set; }

    private CharacterController controller;
    private PlayerController playerController;
    private CharacterDeathHandler death;

    private List<Vector2Int> path;
    private int pathIndex;
    private Vector2Int goalCell;
    private Vector3 goalWorldForMarker;
    private float lastProgressTime;
    private float lastDistToNext = float.MaxValue;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        playerController = GetComponent<PlayerController>();
        death = GetComponent<CharacterDeathHandler>();
    }

    private void Start()
    {
        if (grid == null) grid = GridSystem.Instance;
        if (operationMode == null) operationMode = FindFirstObjectByType<OperationModeController>();
        SetMarker(false);
    }

    private void OnDisable() => Stop();

    private float MoveSpeed =>
        moveSpeedOverride > 0f ? moveSpeedOverride
        : (playerController != null ? playerController.walkSpeed : 4f);

    private bool InOperationMode => operationMode != null && operationMode.InOperationMode;

    private void Update()
    {
        PlanarSpeed = 0f;
        if (grid == null) grid = GridSystem.Instance;

        // 不在操作模式 / 死亡中 / 正在传送 → 不接管，并清掉残留路径
        bool dying = death != null && death.IsDying;
        if (!InOperationMode || dying || !controller.enabled || grid == null)
        {
            if (path != null) Stop();
            return;
        }

        if (Input.GetMouseButtonDown(mouseButton) && !IsPointerOverUI())
            HandleClick();

        FollowPath();

        navigatingReadout = IsNavigating;
    }

    // ────────────────────────────────────────────────────────────────────
    //  点击 → 终点格
    // ────────────────────────────────────────────────────────────────────

    private void HandleClick()
    {
        Camera c = rayCamera != null ? rayCamera : Camera.main;
        if (c == null) return;

        if (!TryGetClickPoint(c.ScreenPointToRay(Input.mousePosition), out Vector3 clickPoint))
        {
            Log("点击没落在网格上");
            return;
        }

        Vector2Int goal = NearestWalkable(grid.WorldToCell(clickPoint), clickPoint);
        if (goal.x < 0)
        {
            Log($"点击点 {clickPoint} 附近 {searchRadius} 格内没有可走格");
            return;
        }

        if (!RequestPath(goal))
            Log($"到格子 {goal} 无路可达（可能桥没接上）");
    }

    /// 先打 bridge 层（点在桥面上最准），打不中再用网格所在水平面兜底
    private bool TryGetClickPoint(Ray ray, out Vector3 point)
    {
        if (Physics.Raycast(ray, out RaycastHit hit, 5000f, grid.walkableMask, QueryTriggerInteraction.Ignore))
        {
            point = hit.point;
            return true;
        }

        Plane plane = new Plane(Vector3.up, new Vector3(0f, grid.GridY, 0f));
        if (plane.Raycast(ray, out float enter))
        {
            point = ray.GetPoint(enter);
            return true;
        }

        point = default;
        return false;
    }

    /// 在 searchRadius 格内，找离 referencePoint【世界距离】最近的可走格；找不到返回 (-1,-1)
    private Vector2Int NearestWalkable(Vector2Int center, Vector3 referencePoint)
    {
        if (grid.IsWalkable(center)) return center;

        Vector2 refXZ = new Vector2(referencePoint.x, referencePoint.z);
        Vector2Int best = new Vector2Int(-1, -1);
        float bestSqr = float.MaxValue;

        for (int dx = -searchRadius; dx <= searchRadius; dx++)
            for (int dz = -searchRadius; dz <= searchRadius; dz++)
            {
                Vector2Int c = new Vector2Int(center.x + dx, center.y + dz);
                if (!grid.IsWalkable(c)) continue;
                Vector3 w = grid.CellToWorld(c);
                float sqr = (new Vector2(w.x, w.z) - refXZ).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = c; }
            }
        return best;
    }

    // ────────────────────────────────────────────────────────────────────
    //  寻路 / 跟随
    // ────────────────────────────────────────────────────────────────────

    private bool RequestPath(Vector2Int goal)
    {
        Vector2Int start = NearestWalkable(grid.WorldToCell(transform.position), transform.position);
        if (start.x < 0) { Log("玩家脚下附近没有可走格"); Stop(); return false; }

        List<Vector2Int> p = start == goal
            ? new List<Vector2Int> { goal }   // 已在终点格：走到格心即可
            : AStarPathfinder.FindPath(grid, start, goal, allowDiagonal);

        if (p == null || p.Count == 0) { Stop(); return false; }

        path = p;
        pathIndex = 0;
        goalCell = goal;
        goalWorldForMarker = grid.CellToWorld(goal);
        lastProgressTime = Time.time;
        lastDistToNext = float.MaxValue;
        goalReadout = goal.ToString();
        SetMarker(true);
        Log($"寻路：{start} → {goal}，共 {p.Count} 格");
        return true;
    }

    private void FollowPath()
    {
        if (!IsNavigating) return;

        // 桥移动 / 断电导致下一格不可走 → 重算；终点都不可走了就停
        if (!grid.IsWalkable(path[pathIndex]))
        {
            if (!grid.IsWalkable(goalCell) || !RequestPath(goalCell))
            {
                Log("路径被切断，停止");
                Stop();
                return;
            }
        }

        Vector3 target = grid.CellToWorld(path[pathIndex]);
        Vector3 to = target - transform.position;
        to.y = 0f;
        float dist = to.magnitude;

        if (dist <= arriveThreshold)
        {
            pathIndex++;
            lastDistToNext = float.MaxValue;
            lastProgressTime = Time.time;
            if (!IsNavigating) { Log("到达"); Stop(); }
            return;
        }

        // 卡住检测：一段时间内离下一格没有变近 → 重算一次
        if (dist < lastDistToNext - 0.05f) { lastDistToNext = dist; lastProgressTime = Time.time; }
        else if (Time.time - lastProgressTime > stuckTime)
        {
            Log("卡住，重算路径");
            if (!RequestPath(goalCell)) { Stop(); return; }
        }

        Vector3 dir = to / dist;
        float step = Mathf.Min(MoveSpeed * Time.deltaTime, dist);   // 不冲过格心
        controller.Move(dir * step);                                // 只动水平；重力交给 PassiveFall
        PlanarSpeed = Time.deltaTime > 0f ? step / Time.deltaTime : 0f;

        Quaternion look = Quaternion.LookRotation(dir, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);

        if (destinationMarker != null)
            destinationMarker.position = goalWorldForMarker + Vector3.up * markerYOffset;
    }

    /// 取消当前寻路
    public void Stop()
    {
        path = null;
        pathIndex = 0;
        PlanarSpeed = 0f;
        navigatingReadout = false;
        goalReadout = "(无)";
        SetMarker(false);
    }

    // ────────────────────────────────────────────────────────────────────
    //  杂项
    // ────────────────────────────────────────────────────────────────────

    private void SetMarker(bool on)
    {
        if (destinationMarker == null) return;
        if (on) destinationMarker.position = goalWorldForMarker + Vector3.up * markerYOffset;
        destinationMarker.gameObject.SetActive(on);
    }

    private static bool IsPointerOverUI()
        => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    private void Log(string msg)
    {
        if (verboseLog) Debug.Log($"[右键寻路] {msg}", this);
    }

    private void OnDrawGizmos()
    {
        if (!drawPathGizmos || grid == null || path == null) return;
        Gizmos.color = Color.cyan;
        Vector3 prev = transform.position;
        for (int i = pathIndex; i < path.Count; i++)
        {
            Vector3 p = grid.CellToWorld(path[i]);
            p.y = transform.position.y;
            Gizmos.DrawLine(prev, p);
            Gizmos.DrawWireSphere(p, 0.2f);
            prev = p;
        }
    }
}
