using UnityEngine;

/// <summary>
/// Pathfinding grid: lays grid cells on the world XZ plane and uses volume checks (OverlapBox) to decide walkability per cell -- 
/// a cell is walkable only if the vertical check box at its center hits a [solid collider] on the bridge layer (Triggers ignored).
///
/// Update timing: event-driven + 3 s fallback. Any event that changes a bridge's position calls RequestRescan(),
/// and one scan runs next frame; no scanning otherwise, to save performance. Supports bridges moving up/down / left/right.
/// </summary>
[ExecuteAlways]
public class GridSystem : MonoBehaviour
{
    [Header("Grid Definition (world XZ plane)")]
    [Tooltip("World position of the grid's minimum corner (the corner with smallest X and Z)")]
    public Vector3 origin = new Vector3(-109.5f, 2.17f, -149.66f);
    [Tooltip("Side length of each grid cell (world units)")]
    public float cellSize = 2f;
    [Tooltip("Columns: number of cells along X")]
    public int cols = 45;
    [Tooltip("Rows: number of cells along Z")]
    public int rows = 55;

    [Header("Walkability Check (OverlapBox)")]
    [Tooltip("Layers the bridges are on (only these layers are checked)")]
    public LayerMask walkableMask;
    [Tooltip("Check box height: covers the bridge's vertical travel + margin")]
    public float checkHeight = 2f;
    [Tooltip("Vertical offset of the check box center from the cell center (positive = up)")]
    public float checkYOffset = 0f;
    [Tooltip("Fraction of the cell the check box covers in XZ (slightly under 1 to avoid touching neighbors)")]
    [Range(0.1f, 1f)] public float cellFillRatio = 0.9f;
    [Tooltip("High-frequency scan interval while moving (seconds)")]
    public float activeRescanInterval = 0.2f;
    [Tooltip("Fallback polling interval (seconds), used while idle to catch missed events")]
    public float fallbackRescanInterval = 0.6f;
    [Tooltip("After a move notification, how long the \"movement active period\" lasts (seconds); high-frequency scanning is used during it")]
    public float activeLinger = 0.4f;

    [Header("Visualization")]
    public bool drawGrid = true;
    [Tooltip("Draw grid lines")]
    public Color lineColor = new Color(0.2f, 0.9f, 1f, 0.35f);
    [Tooltip("Fill color for walkable cells (from scan results at runtime)")]
    public Color walkableColor = new Color(0.2f, 1f, 0.3f, 0.35f);
    [Tooltip("Fill color for unwalkable cells")]
    public Color blockedColor = new Color(1f, 0.2f, 0.2f, 0.25f);
    [Tooltip("Draw the cell-center check boxes (helps tune checkHeight / checkYOffset so the box covers the bridge)")]
    public bool drawCheckBoxes = false;
    public Color checkBoxColor = new Color(1f, 0.9f, 0.2f, 0.9f);

    [Header("Runtime Visualization (also visible in Game view)")]
    [Tooltip("Draw walkable/unwalkable cells in the Game view at runtime (debug only, turn off for release)")]
    public bool runtimeDebugDraw = false;

    private Material glMat;

    // Whether each cell is walkable
    public bool[,] Walkable { get; private set; }

    /// Scene singleton (so movers can notify easily). Assumes one grid per scene.
    public static GridSystem Instance { get; private set; }

    private bool needsRescan;
    private float nextScan;         // Next scan time
    private float activeUntil;      // End time of the movement active period

    // Grid size
    public float Width => cols * cellSize;
    public float Depth => rows * cellSize;
    public float GridY => origin.y;

    // -- Coordinate mapping --
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

    // -- Update scheduling --
    /// Rescan once immediately (next frame). For "one-off" events (entering/exiting Operation Mode, clicks, fully charged, etc.).
    public void RequestRescan() => needsRescan = true;

    /// Call every frame while moving: enters the "movement active period", scanning at activeRescanInterval;
    /// activeLinger seconds after calls stop, it falls back to the 3 s fallback.
    public void NotifyMoving()
    {
        activeUntil = Time.time + activeLinger;
    }

    /// Whether currently in the movement active period
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
        // One-off request: scan immediately next frame
        if (needsRescan)
        {
            needsRescan = false;
            Rescan();
            nextScan = Time.time + (IsActive ? activeRescanInterval : fallbackRescanInterval);
            return;
        }

        if (!Application.isPlaying) return;

        // High-frequency interval during the movement active period, fallback interval otherwise
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

    /// Immediately rescan walkability of all cells (volume check)
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
                // Only solid colliders count (QueryTriggerInteraction.Ignore): uncharged bridges that became Triggers are not walkable
                bool hit = Physics.CheckBox(center, halfExtents, Quaternion.identity,
                                            walkableMask, QueryTriggerInteraction.Ignore);
                Walkable[x, z] = hit;
            }
        }
    }

    // -- Draw cells in the Game view at runtime (GL immediate mode) --
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

        // Grid lines
        Gizmos.color = lineColor;
        for (int x = 0; x <= cols; x++)
            Gizmos.DrawLine(new Vector3(origin.x + x * cellSize, y, origin.z),
                            new Vector3(origin.x + x * cellSize, y, origin.z + Depth));
        for (int z = 0; z <= rows; z++)
            Gizmos.DrawLine(new Vector3(origin.x, y, origin.z + z * cellSize),
                            new Vector3(origin.x + Width, y, origin.z + z * cellSize));

        // Outer border
        Gizmos.color = Color.white;
        Vector3 o = origin;
        Gizmos.DrawLine(o, o + new Vector3(Width, 0, 0));
        Gizmos.DrawLine(o + new Vector3(Width, 0, 0), o + new Vector3(Width, 0, Depth));
        Gizmos.DrawLine(o + new Vector3(Width, 0, Depth), o + new Vector3(0, 0, Depth));
        Gizmos.DrawLine(o + new Vector3(0, 0, Depth), o);

        // Walkable / unwalkable fill (only drawn when scan results exist)
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

        // Check box visualization (for tuning height)
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