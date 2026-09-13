using UnityEngine;

/// <summary>
/// 寻路网格：在世界 XZ 平面铺格子，用体积检测（OverlapBox）判定每格可走性 —— 
/// 只有格子中心的竖直检测盒碰到 bridge 层的【实心碰撞体】(忽略 Trigger) 才可走。
///
/// 更新时机：事件驱动 + 3 秒兜底。任何会改变桥位置的事件调 RequestRescan()，
/// 下一帧统一扫一次；平时不扫，省性能。适配 bridge 上下 / 左右移动。
/// </summary>
[ExecuteAlways]
public class GridSystem : MonoBehaviour
{
    [Header("网格定义（世界 XZ 平面）")]
    [Tooltip("网格最小角的世界坐标（X 最小、Z 最小的那个角）")]
    public Vector3 origin = new Vector3(-109.5f, 2.17f, -149.66f);
    [Tooltip("每个格子的边长（世界单位）")]
    public float cellSize = 2f;
    [Tooltip("列数：X 方向格子数")]
    public int cols = 45;
    [Tooltip("行数：Z 方向格子数")]
    public int rows = 55;

    [Header("可走性检测（OverlapBox）")]
    [Tooltip("bridge 所在层（只检测这些层）")]
    public LayerMask walkableMask;
    [Tooltip("检测盒高度：罩住 bridge 上下移动的行程 + 余量")]
    public float checkHeight = 2f;
    [Tooltip("检测盒竖直中心相对格心的偏移（正=往上抬）")]
    public float checkYOffset = 0f;
    [Tooltip("检测盒 XZ 占格子的比例（略小于 1 避免蹭到隔壁格）")]
    [Range(0.1f, 1f)] public float cellFillRatio = 0.9f;
    [Tooltip("移动期间的高频扫描间隔（秒）")]
    public float activeRescanInterval = 0.2f;
    [Tooltip("兜底轮询间隔（秒），静止时用它兜住漏掉的事件")]
    public float fallbackRescanInterval = 0.6f;
    [Tooltip("收到移动通知后，再维持多久算\"移动活跃期\"（秒），期间用高频扫描")]
    public float activeLinger = 0.4f;

    [Header("可视化")]
    public bool drawGrid = true;
    [Tooltip("画网格线")]
    public Color lineColor = new Color(0.2f, 0.9f, 1f, 0.35f);
    [Tooltip("可走格填充色（运行时按扫描结果）")]
    public Color walkableColor = new Color(0.2f, 1f, 0.3f, 0.35f);
    [Tooltip("不可走格填充色")]
    public Color blockedColor = new Color(1f, 0.2f, 0.2f, 0.25f);
    [Tooltip("画格心检测盒（方便调 checkHeight / checkYOffset 让盒子罩住桥）")]
    public bool drawCheckBoxes = false;
    public Color checkBoxColor = new Color(1f, 0.9f, 0.2f, 0.9f);

    [Header("运行时可视化（Game 视图也可见）")]
    [Tooltip("运行时在 Game 视图画可走/不可走格子（调试用，正式发布关掉）")]
    public bool runtimeDebugDraw = false;

    private Material glMat;

    // 每格是否可走
    public bool[,] Walkable { get; private set; }

    /// 场景单例（方便移动组件通知）。假定场景只有一个网格。
    public static GridSystem Instance { get; private set; }

    private bool needsRescan;
    private float nextScan;         // 下次扫描时间
    private float activeUntil;      // 移动活跃期结束时间

    // 网格尺寸
    public float Width => cols * cellSize;
    public float Depth => rows * cellSize;
    public float GridY => origin.y;

    // —— 坐标映射 ——
    public Vector2Int WorldToCell(Vector3 world)
    {
        int x = Mathf.FloorToInt((world.x - origin.x) / cellSize);
        int z = Mathf.FloorToInt((world.z - origin.z) / cellSize);
        return new Vector2Int(x, z);
    }
    public Vector3 CellToWorld(int x, int z) => new Vector3(
        origin.x + (x + 0.5f) * cellSize, GridY, origin.z + (z + 0.5f) * cellSize);
    public Vector3 CellToWorld(Vector2Int c) => CellToWorld(c.x, c.y);
    public bool InBounds(int x, int z) => x >= 0 && x < cols && z >= 0 && z < rows;
    public bool InBounds(Vector2Int c) => InBounds(c.x, c.y);
    public bool IsWalkable(int x, int z) => InBounds(x, z) && Walkable != null && Walkable[x, z];
    public bool IsWalkable(Vector2Int c) => IsWalkable(c.x, c.y);

    // —— 更新调度 ——
    /// 立即（下一帧）重扫一次。适合"一次性"事件（进出操作模式、点击、充满等）。
    public void RequestRescan() => needsRescan = true;

    /// 移动中每帧调用：进入"移动活跃期"，期间按 activeRescanInterval 高频扫描；
    /// 停止调用 activeLinger 秒后自动回落到 3s 兜底。
    public void NotifyMoving()
    {
        activeUntil = Time.time + activeLinger;
    }

    /// 当前是否处于移动活跃期
    public bool IsActive => Time.time <= activeUntil;

    private void OnEnable()
    {
        Instance = this;
        EnsureArray();
        Rescan();
        nextScan = Time.time + fallbackRescanInterval;
    }

    private void OnDisable()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        // 一次性请求：下一帧立即扫
        if (needsRescan)
        {
            needsRescan = false;
            Rescan();
            nextScan = Time.time + (IsActive ? activeRescanInterval : fallbackRescanInterval);
            return;
        }

        if (!Application.isPlaying) return;

        // 移动活跃期用高频间隔，否则用兜底间隔
        if (Time.time >= nextScan)
        {
            Rescan();
            nextScan = Time.time + (IsActive ? activeRescanInterval : fallbackRescanInterval);
        }
    }

    private void EnsureArray()
    {
        if (Walkable == null || Walkable.GetLength(0) != cols || Walkable.GetLength(1) != rows)
            Walkable = new bool[Mathf.Max(1, cols), Mathf.Max(1, rows)];
    }

    /// 立即重扫全部格子可走性（体积检测）
    public void Rescan()
    {
        EnsureArray();

        float half = cellSize * cellFillRatio * 0.5f;
        Vector3 halfExtents = new Vector3(half, checkHeight * 0.5f, half);

        for (int x = 0; x < cols; x++)
        {
            for (int z = 0; z < rows; z++)
            {
                Vector3 center = CellToWorld(x, z);
                center.y += checkYOffset;
                // 只认实心碰撞体（QueryTriggerInteraction.Ignore）：没充电变 Trigger 的桥不算可走
                bool hit = Physics.CheckBox(center, halfExtents, Quaternion.identity,
                                            walkableMask, QueryTriggerInteraction.Ignore);
                Walkable[x, z] = hit;
            }
        }
    }

    // —— 运行时在 Game 视图画格子（GL 即时绘制）——
    private void EnsureGLMat()
    {
        if (glMat != null) return;
        Shader s = Shader.Find("Hidden/Internal-Colored");
        glMat = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
        glMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        glMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        glMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        glMat.SetInt("_ZWrite", 0);
    }

    private void OnRenderObject()
    {
        if (!runtimeDebugDraw || !Application.isPlaying) return;
        if (Walkable == null || Walkable.GetLength(0) != cols || Walkable.GetLength(1) != rows) return;

        EnsureGLMat();
        glMat.SetPass(0);

        GL.PushMatrix();
        GL.Begin(GL.QUADS);
        float inset = cellSize * 0.45f;
        for (int x = 0; x < cols; x++)
        {
            for (int z = 0; z < rows; z++)
            {
                GL.Color(Walkable[x, z] ? walkableColor : blockedColor);
                Vector3 c = CellToWorld(x, z);
                c.y += 0.02f;
                GL.Vertex(c + new Vector3(-inset, 0, -inset));
                GL.Vertex(c + new Vector3(-inset, 0,  inset));
                GL.Vertex(c + new Vector3( inset, 0,  inset));
                GL.Vertex(c + new Vector3( inset, 0, -inset));
            }
        }
        GL.End();
        GL.PopMatrix();
    }

    private void OnDrawGizmos()
    {
        if (!drawGrid) return;
        float y = GridY;

        // 网格线
        Gizmos.color = lineColor;
        for (int x = 0; x <= cols; x++)
            Gizmos.DrawLine(new Vector3(origin.x + x * cellSize, y, origin.z),
                            new Vector3(origin.x + x * cellSize, y, origin.z + Depth));
        for (int z = 0; z <= rows; z++)
            Gizmos.DrawLine(new Vector3(origin.x, y, origin.z + z * cellSize),
                            new Vector3(origin.x + Width, y, origin.z + z * cellSize));

        // 外框
        Gizmos.color = Color.white;
        Vector3 o = origin;
        Gizmos.DrawLine(o, o + new Vector3(Width, 0, 0));
        Gizmos.DrawLine(o + new Vector3(Width, 0, 0), o + new Vector3(Width, 0, Depth));
        Gizmos.DrawLine(o + new Vector3(Width, 0, Depth), o + new Vector3(0, 0, Depth));
        Gizmos.DrawLine(o + new Vector3(0, 0, Depth), o);

        // 可走 / 不可走 填充（有扫描结果才画）
        if (Walkable != null && Walkable.GetLength(0) == cols && Walkable.GetLength(1) == rows)
        {
            Vector3 quad = new Vector3(cellSize * 0.9f, 0.01f, cellSize * 0.9f);
            for (int x = 0; x < cols; x++)
                for (int z = 0; z < rows; z++)
                {
                    Gizmos.color = Walkable[x, z] ? walkableColor : blockedColor;
                    Gizmos.DrawCube(CellToWorld(x, z), quad);
                }
        }

        // 检测盒可视化（调高度用）
        if (drawCheckBoxes)
        {
            Gizmos.color = checkBoxColor;
            Vector3 size = new Vector3(cellSize * cellFillRatio, checkHeight, cellSize * cellFillRatio);
            for (int x = 0; x < cols; x++)
                for (int z = 0; z < rows; z++)
                {
                    Vector3 center = CellToWorld(x, z);
                    center.y += checkYOffset;
                    Gizmos.DrawWireCube(center, size);
                }
        }
    }
}