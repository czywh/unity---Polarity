using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 敌人追踪 AI：在 GridSystem 的可走格子上用 A* 追踪目标。
/// 目标可多选（Player / Robot），每次挑【最近且其所在格可走 / 可达】的目标追。
/// 沿格子路径逐格移动。桥 / 目标移动时定期重算。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class EnemyChaser : MonoBehaviour
{
    [System.Flags]
    public enum TargetMask { None = 0, Player = 1, Robot = 2 }

    [Header("电量（移动耗电 · 没电停追 · 领域内充电）")]
    [Tooltip("敌人的电量组件（留空取同物体的 EnergySystem）")]
    public EnergySystem energy;
    [Tooltip("移动时每秒耗电")]
    public float drainPerSecond = 8f;
    [Tooltip("在电子领域内时每秒充电（Robot 开 E 罩住敌人 → 充电）")]
    public float rechargePerSecond = 20f;
    [Tooltip("是否允许在电子领域内充电")]
    public bool rechargeInField = true;

    [Header("追踪目标（可多选）")]
    public TargetMask targets = TargetMask.Player | TargetMask.Robot;
    [Tooltip("玩家 Transform（追 Player 时用）")]
    public Transform player;
    [Tooltip("机器人 Transform（追 Robot 时用）")]
    public Transform robot;

    [Header("巡逻（依次走各巡逻点，可添加）")]
    [Tooltip("巡逻点（按顺序循环）。留空 = 不巡逻，回起点待命")]
    public Transform[] patrolPoints;
    [Tooltip("到达每个巡逻点后等待多久（秒）")]
    public float patrolWait = 5f;
    [Tooltip("到达巡逻点的距离阈值")]
    public float patrolArriveDistance = 1.5f;

    [Header("备用等待点（起点不可达时退到最近的可达备用点）")]
    [Tooltip("起点被电子桥落下等原因阻断、无法抵达时，敌人退到这些点里【最近的可达点】等待")]
    public Transform[] fallbackWaitPoints;

    [Header("站定朝向（面向周围可走格最多的方向）")]
    [Tooltip("待命时朝向周围可走格最密的方向；关掉则保持起始/当前朝向")]
    public bool faceOpenDirection = true;
    [Tooltip("扫描范围（格）：以自身为中心的边长。5 = 5×5")]
    public int facingScanCells = 5;
    [Tooltip("扫描可视化：Scene + Game 视图画出扫描范围与朝向")]
    public bool drawFacingScan = false;
    public Color scanColor = new Color(0.3f, 1f, 0.5f, 0.9f);

    [Header("追击检测（面朝方向的扇形，范围内出现目标即追）")]
    [Tooltip("扇形半径（世界单位）")]
    public float viewRadius = 8f;
    [Tooltip("扇形总张角（度）")]
    [Range(0f, 360f)] public float viewAngle = 90f;
    [Tooltip("视线遮挡层：设了就检测目标是否被墙挡住（留空=不检测遮挡）")]
    public LayerMask viewObstacles;

    [Header("扇形可视化（Scene + Game 视图可见）")]
    public bool drawViewFan = true;
    public Color fanColor = new Color(1f, 0.35f, 0.2f, 0.22f);

    [Header("寻路")]
    [Tooltip("网格；留空取 GridSystem.Instance")]
    public GridSystem grid;
    public bool allowDiagonal = true;
    [Tooltip("每隔多少秒重算一次路径")]
    public float repathInterval = 0.4f;

    [Header("移动")]
    public float moveSpeed = 3.5f;
    public float turnSpeed = 720f;
    [Tooltip("被外部效果（激光等）减速时，速度倍率的下限。0.33 = 最慢只能降到原速 1/3")]
    public float minSpeedMultiplier = 0.1f;
    [Tooltip("到达一个路径点的距离阈值")]
    public float arriveThreshold = 0.15f;
    public float gravity = -25f;

    [Header("攻击（追上目标 → 延迟触发目标死亡，然后返回起点/待命）")]
    [Tooltip("追到目标身边后触发目标死亡")]
    public bool killOnReach = true;
    [Tooltip("离目标多近算追上")]
    public float killDistance = 1.2f;
    [Tooltip("追到后持续贴近多久触发目标死亡（预警窗口，期间逃出则取消）")]
    public float killDelay = 0.5f;
    [Tooltip("回到起点这么近算\"到家\"")]
    public float homeArriveDistance = 0.6f;
    [Tooltip("追到目标致死后，原地等待多久再开始返回起点")]
    public float postKillPause = 0.5f;

    [Header("调试")]
    public bool drawPath = true;
    [SerializeField] private string currentTargetName = "(无)";

    private CharacterController controller;
    private EnemyDeath selfDeath;
    private Material fanMat;
    private float verticalVelocity;
    private List<Vector2Int> path;
    private int pathIndex;
    private float nextRepath;
    private Transform currentTarget;
    private float reachTimer;         // 持续贴近计时（0→killDelay）
    private Vector3 homePosition;     // 起始点
    private Quaternion homeRotation;  // 起始朝向（回家后恢复）
    private float pauseTimer;         // 致死后原地停顿计时

    // 追击状态机
    public enum ChaseState { Idle, Patrolling, PatrolWaiting, Chasing, Waiting, Returning }
    [Header("调试（运行时只读）")]
    [SerializeField] private ChaseState state = ChaseState.Idle;
    [Tooltip("当前移动速度倍率：1 = 正常，被激光照射时降到 1/3")]
    [SerializeField] private float speedMultiplierReadout = 1f;
    public ChaseState State => state;
    private Transform lockedTarget;   // 锁定目标（进入扇形后锁定，死追到底）
    private int patrolIndex;          // 当前巡逻目标点索引
    private float patrolTimer;        // 巡逻点等待计时
    private Vector3 returnGoal;       // 当前返回目标（起点或备用点）
    private bool returnIsHome = true; // 返回目标是不是起点
    private bool hasPower = true;     // 当前是否有电（无电则不追、不动）

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        selfDeath = GetComponent<EnemyDeath>();
        if (energy == null) energy = GetComponent<EnergySystem>();
        homePosition = transform.position;   // 记录起始点
        homeRotation = transform.rotation;   // 记录起始朝向
    }

    private void Start()
    {
        if (grid == null) grid = GridSystem.Instance;
    }

    private void Update()
    {
        if (grid == null) grid = GridSystem.Instance;
        if (grid == null) return;

        // 领域内充电（Robot 开 E 领域罩住敌人 → 充电）
        if (energy != null && rechargeInField && InAnyField())
            energy.Recharge(rechargePerSecond * Time.deltaTime);

        hasPower = (energy == null) || energy.HasEnergy;

        UpdateState();

        if (Time.time >= nextRepath)
        {
            nextRepath = Time.time + repathInterval;
            Repath();
        }

        FollowPath();

        // 锁定目标追到身边持续 killDelay → 目标死亡 → 收尾（需有电）
        if (hasPower && state == ChaseState.Chasing && killOnReach && currentTarget != null)
        {
            float d = Vector3.Distance(transform.position, currentTarget.position);
            if (d <= killDistance)
            {
                reachTimer += Time.deltaTime;
                if (reachTimer >= killDelay)
                {
                    KillTarget(currentTarget);
                    reachTimer = 0f;
                    EnterWaiting();   // 致死后原地停顿，再收尾
                }
            }
            else reachTimer = 0f;
        }
        else reachTimer = 0f;
    }

    // 敌人是否在任意电子领域内（用碰撞体精确判定）
    private bool InAnyField()
    {
        var mgr = ElectricFieldManager.Instance;
        if (mgr == null) return false;
        return controller != null ? mgr.IsColliderInAnyField(controller)
                                   : mgr.IsInsideAnyField(transform.position);
    }

    // 状态推进：Idle 靠扇形发现→锁定；Chasing 死追；Waiting 致死后停顿；Returning 回起点→Idle
    private void UpdateState()
    {
        switch (state)
        {
            case ChaseState.Idle:
                if (DetectAndLock()) break;
                if (HasPatrol()) { state = ChaseState.Patrolling; nextRepath = 0f; }
                break;

            case ChaseState.Patrolling:
                if (DetectAndLock()) break;
                // 到达当前巡逻点 → 等待
                Transform pt = CurrentPatrolPoint();
                if (pt != null && Flat(transform.position, pt.position) <= patrolArriveDistance)
                    EnterPatrolWait();
                break;

            case ChaseState.PatrolWaiting:
                if (DetectAndLock()) break;
                patrolTimer -= Time.deltaTime;
                if (patrolTimer <= 0f)
                {
                    // 前往下一个巡逻点（不可达的会在 Repath 里自动跳过）
                    patrolIndex = (patrolIndex + 1) % Mathf.Max(1, patrolPoints.Length);
                    state = ChaseState.Patrolling;
                    nextRepath = 0f;
                }
                break;

            case ChaseState.Chasing:
                // 锁定后不管扇形，死追到底。目标丢失（销毁/死亡中）→ 停顿后收尾。
                // 其余停止方式请调 StopChasing()。
                if (lockedTarget == null || IsDying(lockedTarget))
                    EnterWaiting();
                break;

            case ChaseState.Waiting:
                // 致死 / 丢失后原地停顿 postKillPause 秒，再收尾（回巡逻或回起点）
                pauseTimer -= Time.deltaTime;
                if (pauseTimer <= 0f) AfterWaiting();
                break;

            case ChaseState.Returning:
                if (DetectAndLock()) break;
                if (Flat(transform.position, returnGoal) <= homeArriveDistance)
                {
                    // 到达返回目标：停止移动，再转向
                    path = null;
                    bool aligned = returnIsHome
                        ? RotateToHomeRotation()          // 回到起点 → 转回游戏开始朝向
                        : RotateToOpenDirection();         // 停在备用点 → 朝可走格最多的方向
                    if (aligned) state = ChaseState.Idle;
                }
                break;
        }
    }

    // 扇形发现目标即锁定并转 Chasing；返回是否发现
    private bool DetectAndLock()
    {
        if (!hasPower) return false;   // 没电不追
        Transform t = DetectInFan();
        if (t == null) return false;
        lockedTarget = t;
        state = ChaseState.Chasing;
        nextRepath = 0f;
        return true;
    }

    private bool HasPatrol() => patrolPoints != null && patrolPoints.Length > 0;
    private Transform CurrentPatrolPoint() =>
        (HasPatrol() && patrolIndex >= 0 && patrolIndex < patrolPoints.Length) ? patrolPoints[patrolIndex] : null;

    private void EnterPatrolWait()
    {
        patrolTimer = patrolWait;
        state = ChaseState.PatrolWaiting;
        path = null;
    }

    // 停顿结束后的收尾：有巡逻点 → 回巡逻；否则 → 返回起点
    private void AfterWaiting()
    {
        if (HasPatrol()) { state = ChaseState.Patrolling; nextRepath = 0f; }
        else BeginReturning();
    }

    // 平滑转回起始朝向，转到位返回 true
    private bool RotateToHomeRotation()
    {
        transform.rotation = Quaternion.RotateTowards(transform.rotation, homeRotation, turnSpeed * Time.deltaTime);
        return Quaternion.Angle(transform.rotation, homeRotation) <= 0.5f;
    }

    private void BeginReturning()
    {
        lockedTarget = null;
        currentTarget = null;
        reachTimer = 0f;
        state = ChaseState.Returning;
        nextRepath = 0f;
    }

    // 致死 / 丢失目标后进入原地停顿
    private void EnterWaiting()
    {
        lockedTarget = null;
        currentTarget = null;
        reachTimer = 0f;
        pauseTimer = postKillPause;
        state = ChaseState.Waiting;
        path = null;
    }

    /// <summary>外部统一"停止追击"入口。以后任何机制（超时 / 被击晕 / 目标进安全区…）都调它 → 放弃当前目标，回巡逻/起点。</summary>
    // ───────────────────── 外部减速（激光等） ─────────────────────

    private float slowMultiplier = 1f;
    private float slowUntil = -1f;

    /// <summary>当前移动速度倍率：1 = 正常，减速生效时 &lt; 1。到期自动恢复。</summary>
    public float CurrentSpeedMultiplier => Time.time <= slowUntil ? slowMultiplier : 1f;

    /// <summary>
    /// 施加减速。需要【持续调用】来维持效果：duration 是"离开效果源后还持续多久"，
    /// 而不是一次性的总时长。激光每个 FixedUpdate 调一次，敌人一离开光束就自然恢复。
    ///
    /// 多个来源同时减速时取最强的那个（倍率最小），不叠乘 —— 否则三条激光会让敌人几乎静止。
    /// </summary>
    public void ApplySlow(float multiplier, float duration)
    {
        multiplier = Mathf.Clamp(multiplier, minSpeedMultiplier, 1f);

        if (Time.time > slowUntil) slowMultiplier = multiplier;              // 上一次已过期，重新开始
        else slowMultiplier = Mathf.Min(slowMultiplier, multiplier);         // 仍在减速中，取更强的

        slowUntil = Mathf.Max(slowUntil, Time.time + duration);
    }

    /// <summary>立即解除减速（复活、传送等场合用）</summary>
    public void ClearSlow()
    {
        slowMultiplier = 1f;
        slowUntil = -1f;
    }

    public void StopChasing()
    {
        if (state == ChaseState.Chasing) EnterWaiting();
    }

    private static bool IsDying(Transform t)
    {
        var dh = t.GetComponentInParent<CharacterDeathHandler>();
        return dh != null && dh.IsDying;
    }

    // 抓到目标致死；若目标是 Robot，敌人补充等于 Robot 当前电量的电
    private void KillTarget(Transform target)
    {
        var dh = target.GetComponentInParent<CharacterDeathHandler>();
        if (dh != null && !dh.IsDying) dh.Die();

        // 目标身份：Robot → 补电；Player → 暂不处理（预留扩展：以后可加"抓到 Player 游戏结束"）
        var id = target.GetComponentInParent<CharacterId>();
        if (id != null && id.characterType == CharacterType.Robot)
        {
            var robotEnergy = target.GetComponentInParent<EnergySystem>();
            if (robotEnergy != null && energy != null)
                energy.Recharge(robotEnergy.CurrentEnergy);   // 敌人电量 += Robot 当前电量
        }
        else if (id != null && id.characterType == CharacterType.Player)
        {
            // TODO: 之后可在此加"抓到 Player"的判定（如游戏结束）。目前不做。
            OnCaughtPlayer(target);
        }
    }

    /// 抓到 Player 时的扩展点（目前留空）。以后接游戏结束等判定。
    private void OnCaughtPlayer(Transform player) { }

    // 按状态求路径
    private void Repath()
    {
        Vector2Int startCell = grid.WorldToCell(transform.position);
        if (!grid.IsWalkable(startCell)) startCell = NearestWalkable(startCell);

        switch (state)
        {
            case ChaseState.Chasing:
                if (lockedTarget == null) { path = null; return; }
                currentTarget = lockedTarget;
                currentTargetName = lockedTarget.name;
                PathTo(startCell, lockedTarget.position);
                break;

            case ChaseState.Returning:
                currentTarget = null;
                ResolveReturnPath(startCell);
                break;

            case ChaseState.Patrolling:
                currentTarget = null;
                // 从当前巡逻点起依次找一个"可达"的点前往；下一个被玩家改动断了就跳到再下一个
                if (!TryPathToPatrol(startCell)) EnterPatrolWait();   // 都走不通 → 原地等，下轮再试
                break;

            default:   // Idle / PatrolWaiting / Waiting → 不移动
                path = null;
                break;
        }
    }

    private void PathTo(Vector2Int startCell, Vector3 worldGoal)
    {
        Vector2Int goal = grid.WorldToCell(worldGoal);
        if (!grid.IsWalkable(goal)) goal = NearestWalkable(goal);
        if (startCell.x < 0 || goal.x < 0) { path = null; return; }
        path = AStarPathfinder.FindPath(grid, startCell, goal, allowDiagonal);
        pathIndex = 0;
    }

    // 返回路径决策：① 起点优先；② 起点不可达 → 最近可达备用点；③ 都不可达 → 就地
    private void ResolveReturnPath(Vector2Int startCell)
    {
        // ① 已在起点范围内 → 到家
        if (Flat(transform.position, homePosition) <= homeArriveDistance)
        {
            returnGoal = homePosition; returnIsHome = true;
            currentTargetName = "(到起点)"; path = null; return;
        }

        // ① 起点可达 → 回起点
        List<Vector2Int> homePath = TryPath(startCell, homePosition);
        if (homePath != null)
        {
            returnGoal = homePosition; returnIsHome = true;
            currentTargetName = "(返回起点)"; path = homePath; pathIndex = 0; return;
        }

        // ② 起点不可达 → 最近可达备用点
        if (TryNearestFallback(startCell))
        {
            returnIsHome = false;
            currentTargetName = "(退到备用点)"; return;
        }

        // ③ 都不可达 → 就地等待，朝可走格最多方向
        returnGoal = transform.position; returnIsHome = false;
        currentTargetName = "(无路可退)"; path = null;
    }

    // 求到某世界点的格子路径（不可达返回 null）
    private List<Vector2Int> TryPath(Vector2Int startCell, Vector3 worldGoal)
    {
        Vector2Int goal = grid.WorldToCell(worldGoal);
        if (!grid.IsWalkable(goal)) goal = NearestWalkable(goal);
        if (startCell.x < 0 || goal.x < 0) return null;
        return AStarPathfinder.FindPath(grid, startCell, goal, allowDiagonal);
    }

    // 选最近的可达备用点，设 returnGoal / path；找到返回 true
    private bool TryNearestFallback(Vector2Int startCell)
    {
        if (fallbackWaitPoints == null || fallbackWaitPoints.Length == 0) return false;

        float bestD = float.MaxValue;
        List<Vector2Int> bestPath = null;
        Vector3 bestPos = default;
        bool found = false;

        for (int i = 0; i < fallbackWaitPoints.Length; i++)
        {
            var fp = fallbackWaitPoints[i];
            if (fp == null) continue;

            float d = Flat(transform.position, fp.position);
            if (d >= bestD) continue;   // 只保留更近的

            // 已在该备用点范围内 → 直接选它（无需路径）
            if (d <= homeArriveDistance)
            {
                bestD = d; bestPath = null; bestPos = fp.position; found = true; continue;
            }

            var p = TryPath(startCell, fp.position);
            if (p != null) { bestD = d; bestPath = p; bestPos = fp.position; found = true; }
        }

        if (found)
        {
            returnGoal = bestPos;
            path = bestPath;      // 已在点上时为 null（到达）
            pathIndex = 0;
        }
        return found;
    }

    // 平滑转向"周围可走格最多"的方向，转到位返回 true
    private bool RotateToOpenDirection()
    {
        if (!faceOpenDirection) return true;

        Vector3 dir = ComputeOpenDirection();
        if (dir.sqrMagnitude < 1e-4f) return true;   // 周围没有明显通路方向 → 直接完成

        Quaternion target = Quaternion.LookRotation(dir, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
        return Quaternion.Angle(transform.rotation, target) <= 0.5f;
    }

    // 扫描周围 facingScanCells×facingScanCells 的可走格，返回"可走格最密"的方向
    private Vector3 ComputeOpenDirection()
    {
        if (grid == null) return Vector3.zero;
        Vector2Int c = grid.WorldToCell(transform.position);
        int half = Mathf.Max(1, facingScanCells / 2);
        Vector3 self = transform.position;
        Vector3 sum = Vector3.zero;

        for (int dx = -half; dx <= half; dx++)
            for (int dz = -half; dz <= half; dz++)
            {
                if (dx == 0 && dz == 0) continue;
                Vector2Int n = new Vector2Int(c.x + dx, c.y + dz);
                if (!grid.IsWalkable(n)) continue;
                Vector3 w = grid.CellToWorld(n) - self; w.y = 0f;
                if (w.sqrMagnitude < 1e-4f) continue;
                sum += w.normalized;   // 每个可走格贡献一个单位方向 → 合向量指向通路最密处
            }

        sum.y = 0f;
        return sum.sqrMagnitude < 1e-4f ? Vector3.zero : sum.normalized;
    }

    // 从 patrolIndex 起依次尝试各巡逻点，落到第一个可达的（跳过被断路的）；全不可达返回 false
    private bool TryPathToPatrol(Vector2Int startCell)
    {
        if (!HasPatrol() || startCell.x < 0) return false;

        for (int i = 0; i < patrolPoints.Length; i++)
        {
            int idx = (patrolIndex + i) % patrolPoints.Length;
            var pt = patrolPoints[idx];
            if (pt == null) continue;

            Vector2Int goal = grid.WorldToCell(pt.position);
            if (!grid.IsWalkable(goal)) goal = NearestWalkable(goal);
            if (goal.x < 0) continue;

            var p = AStarPathfinder.FindPath(grid, startCell, goal, allowDiagonal);
            if (p != null)
            {
                patrolIndex = idx;               // 跳到这个可达点
                path = p; pathIndex = 0;
                currentTargetName = $"(巡逻→{pt.name})";
                return true;
            }
        }
        return false;   // 全走不通
    }

    private static float Flat(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // 扇形发现目标（仅用于非追击状态的"发现"，不用于持续追击判定）
    private Transform DetectInFan()
    {
        Transform best = null;
        float bestD = float.MaxValue;
        if ((targets & TargetMask.Player) != 0 && player != null) ConsiderFan(player, ref best, ref bestD);
        if ((targets & TargetMask.Robot) != 0 && robot != null) ConsiderFan(robot, ref best, ref bestD);
        return best;
    }

    private void ConsiderFan(Transform t, ref Transform best, ref float bestD)
    {
        if (IsDying(t)) return;
        if (!InViewFan(t.position)) return;

        Vector2Int tCell = grid.WorldToCell(t.position);
        if (!grid.IsWalkable(tCell) && NearestWalkable(tCell).x < 0) return;   // 目标要能到达

        float d = (t.position - transform.position).sqrMagnitude;
        if (d < bestD) { bestD = d; best = t; }
    }

    /// 某世界点是否落在敌人面朝的扇形内（半径 + 张角 + 可选视线遮挡）
    public bool InViewFan(Vector3 worldPos)
    {
        Vector3 to = worldPos - transform.position;
        to.y = 0f;
        float dist = to.magnitude;
        if (dist > viewRadius || dist < 0.001f) return false;

        Vector3 fwd = transform.forward; fwd.y = 0f;
        if (Vector3.Angle(fwd, to) > viewAngle * 0.5f) return false;

        // 视线遮挡（可选）
        if (viewObstacles.value != 0 &&
            Physics.Raycast(transform.position, to.normalized, dist - 0.1f, viewObstacles, QueryTriggerInteraction.Ignore))
            return false;

        return true;
    }

    // 沿路径逐格移动
    private void FollowPath()
    {
        Vector3 move = Vector3.zero;
        speedMultiplierReadout = CurrentSpeedMultiplier;

        // 有电才移动；移动时耗电
        if (hasPower && path != null && pathIndex < path.Count)
        {
            Vector3 targetPos = grid.CellToWorld(path[pathIndex]);
            Vector3 flatPos = new Vector3(transform.position.x, targetPos.y, transform.position.z);

            if (Vector3.Distance(flatPos, targetPos) <= arriveThreshold)
            {
                pathIndex++;   // 到点，下一格
            }
            else
            {
                Vector3 dir = (targetPos - flatPos); dir.y = 0f;
                if (dir.sqrMagnitude > 0.0001f)
                {
                    dir.Normalize();
                    move = dir * moveSpeed * CurrentSpeedMultiplier;
                    FaceDir(dir);
                    if (energy != null && state == ChaseState.Chasing)
                        energy.Drain(drainPerSecond * Time.deltaTime);   // 仅追逐耗电（巡逻/返回不耗）
                }
            }
        }

        // 重力
        if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = move + Vector3.up * verticalVelocity;
        controller.Move(velocity * Time.deltaTime);
    }

    private void FaceDir(Vector3 dir)
    {
        Quaternion target = Quaternion.LookRotation(dir, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
    }

    // 在小范围内找最近的可走格（目标/自己站在格外时兜底）；找不到返回 (-1,-1)
    private Vector2Int NearestWalkable(Vector2Int c)
    {
        if (grid.IsWalkable(c)) return c;
        for (int r = 1; r <= 3; r++)   // 向外扩 3 格找
        {
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                {
                    if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue; // 只查环
                    Vector2Int n = new Vector2Int(c.x + dx, c.y + dy);
                    if (grid.IsWalkable(n)) return n;
                }
        }
        return new Vector2Int(-1, -1);
    }

    private void OnDrawGizmos()
    {
        // 路径
        if (drawPath && grid != null && path != null)
        {
            Gizmos.color = Color.magenta;
            Vector3 prev = transform.position;
            for (int i = pathIndex; i < path.Count; i++)
            {
                Vector3 p = grid.CellToWorld(path[i]);
                Gizmos.DrawLine(prev, p);
                Gizmos.DrawWireCube(p, Vector3.one * grid.cellSize * 0.3f);
                prev = p;
            }
        }

        // 扇形（Scene 视图）
        if (drawViewFan) DrawFanGizmo();

        // 巡逻点 + 连线
        if (patrolPoints != null && patrolPoints.Length > 0)
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.9f);
            for (int i = 0; i < patrolPoints.Length; i++)
            {
                var a = patrolPoints[i];
                if (a == null) continue;
                Gizmos.DrawWireSphere(a.position, 0.4f);
                var b = patrolPoints[(i + 1) % patrolPoints.Length];
                if (b != null) Gizmos.DrawLine(a.position, b.position);
            }
        }

        // 备用等待点
        if (fallbackWaitPoints != null)
        {
            Gizmos.color = new Color(1f, 0.7f, 0.2f, 0.9f);
            foreach (var fp in fallbackWaitPoints)
                if (fp != null) { Gizmos.DrawWireSphere(fp.position, 0.45f); Gizmos.DrawLine(transform.position, fp.position); }
        }

        // 站定朝向扫描范围 + 计算出的开阔方向
        if (drawFacingScan && grid != null)
        {
            int half = Mathf.Max(1, facingScanCells / 2);
            float side = (half * 2 + 1) * grid.cellSize;
            Gizmos.color = scanColor;
            Vector3 c = transform.position; c.y = grid.GridY + 0.05f;
            Gizmos.DrawWireCube(c, new Vector3(side, 0.02f, side));

            Vector3 dir = Application.isPlaying ? ComputeOpenDirection() : Vector3.zero;
            if (dir.sqrMagnitude > 1e-4f)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(c, c + dir * (side * 0.5f));
            }
        }
    }

    private void DrawFanGizmo()
    {
        Vector3 fwd = transform.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) return;
        fwd.Normalize();
        float half = viewAngle * 0.5f;
        Vector3 origin = transform.position;

        Gizmos.color = new Color(fanColor.r, fanColor.g, fanColor.b, 0.9f);
        Vector3 left = Quaternion.Euler(0, -half, 0) * fwd;
        Vector3 right = Quaternion.Euler(0, half, 0) * fwd;
        Gizmos.DrawLine(origin, origin + left * viewRadius);
        Gizmos.DrawLine(origin, origin + right * viewRadius);

        int seg = 24;
        Vector3 prev = origin + left * viewRadius;
        for (int i = 1; i <= seg; i++)
        {
            float a = -half + viewAngle * (i / (float)seg);
            Vector3 p = origin + (Quaternion.Euler(0, a, 0) * fwd) * viewRadius;
            Gizmos.DrawLine(prev, p);
            prev = p;
        }
    }

    // —— Game 视图运行时画扇形（GL 填充）——
    private void EnsureFanMat()
    {
        if (fanMat != null) return;
        fanMat = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
        fanMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        fanMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        fanMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        fanMat.SetInt("_ZWrite", 0);
    }

    private void OnRenderObject()
    {
        if (!drawViewFan || !Application.isPlaying || viewRadius <= 0f) return;

        Vector3 fwd = transform.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) return;
        fwd.Normalize();

        EnsureFanMat();
        fanMat.SetPass(0);

        float half = viewAngle * 0.5f;
        int seg = Mathf.Max(2, Mathf.CeilToInt(viewAngle / 6f));
        Vector3 c = transform.position + Vector3.up * 0.05f;

        GL.PushMatrix();
        GL.Begin(GL.TRIANGLES);
        GL.Color(fanColor);
        for (int i = 0; i < seg; i++)
        {
            float a0 = -half + viewAngle * (i / (float)seg);
            float a1 = -half + viewAngle * ((i + 1) / (float)seg);
            Vector3 d0 = Quaternion.Euler(0, a0, 0) * fwd;
            Vector3 d1 = Quaternion.Euler(0, a1, 0) * fwd;
            GL.Vertex(c);
            GL.Vertex(c + d0 * viewRadius);
            GL.Vertex(c + d1 * viewRadius);
        }
        GL.End();
        GL.PopMatrix();
    }
}