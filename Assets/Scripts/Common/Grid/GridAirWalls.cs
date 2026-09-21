using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// GRID  Invisible "air walls" along the edge of the walkable area, so characters cannot walk / be pushed off a bridge.
///
/// How it works:
///   - Reads GridSystem.Walkable (the same scan the A* pathfinding uses). For every walkable cell, each of its 4 sides
///     that borders a NON-walkable cell (or the grid boundary) gets a thin BoxCollider along that edge.
///   - Sides shared by two walkable cells get nothing, so when a bridge is moved into place and two areas connect, the
///     walls between them disappear automatically and only the outer edge remains.
///   - Rebuilds whenever the walkability array changes (GridSystem rescans while bridges move), using a pool of colliders
///     so nothing is allocated at runtime after warm-up.
///   - Wall height follows the actual floor: a ray is cast down at the cell centre onto the bridge layer to find the
///     surface, and the wall stands from a little below it to wallHeight above it (bridges at different heights are fine).
///
/// Layer: walls are put on "Ignore Raycast" (layer 2) by default, so Physics.Raycast calls that use the default mask
/// never see them; LaserBarrier / Hovl_LaserDemo explicitly skip that layer too, so lasers pass straight through.
/// Scripts that raycast with an explicit "Everything" mask should exclude layer 2 (the scene's camera occlusion, aim and
/// missile obstacle masks are already set that way).
///
/// Attach next to GridSystem (same object is fine). Nothing to configure; tune the wall shape in the Inspector.
/// </summary>
[DefaultExecutionOrder(50)]   // after GridSystem (0) has rescanned this frame
[DisallowMultipleComponent]
public class GridAirWalls : MonoBehaviour
{
    [Header("Grid")]
    [Tooltip("Grid to read walkability from. Empty = GridSystem.Instance")]
    public GridSystem grid;

    [Header("Wall shape")]
    [Tooltip("How high the wall stands above the floor (world units)")]
    public float wallHeight = 3f;
    [Tooltip("How far the wall extends below the floor surface, so nothing slips under the edge")]
    public float wallSink = 0.5f;
    [Tooltip("Wall thickness. The wall is centred on the cell edge, so half of it sits inside the walkable cell")]
    public float wallThickness = 0.2f;
    [Tooltip("Extra length added at both ends of every segment so walls overlap at corners and leave no gap")]
    public float cornerOverlap = 0.1f;

    [Header("Floor height")]
    [Tooltip("Cast a ray down at the cell centre (on the grid's walkable layers) to find the real floor height. Off = use the grid plane (GridY)")]
    public bool snapToFloor = true;
    [Tooltip("Ray starts this far above the grid plane")]
    public float floorProbeUp = 4f;
    [Tooltip("Ray reaches this far below the grid plane")]
    public float floorProbeDown = 6f;

    [Header("Physics")]
    [Tooltip("Layer for the wall colliders. Default 2 = Ignore Raycast (invisible to default-mask raycasts and to lasers)")]
    public int wallLayer = 2;
    [Tooltip("Optional physic material (e.g. zero friction so characters slide along the wall instead of sticking)")]
    public PhysicMaterial wallMaterial;
    [Tooltip("Also wall the outer boundary of the grid (walkable cells on the last row / column)")]
    public bool wallGridBorder = true;

    [Header("Visual (optional)")]
    [Tooltip("Prefab drawn on every wall segment (e.g. Prefab/AirWallFX = the WallFX_00 mesh + WallFX_A_03 material). Its mesh is measured and\nscaled so it covers exactly one segment: local Y = height, local Z = length, local +X = the visible front face. Empty = invisible walls")]
    public GameObject wallVisualPrefab;
    [Tooltip("Show / hide the visuals without removing the colliders")]
    public bool showVisuals = true;
    [Tooltip("Visual height as a fraction of Wall Height (1 = same height as the collider)")]
    public float visualHeightScale = 1f;
    [Tooltip("Extra length added to each visual segment (world units) so neighbouring panels overlap slightly")]
    public float visualLengthPadding = 0.02f;
    [Tooltip("Move the visual this far INTO the walkable cell, away from the edge (avoids z-fighting with the bridge rim)")]
    public float visualInset = 0.02f;
    [Tooltip("Lift / lower the visual relative to the floor")]
    public float visualYOffset = 0f;
    [Tooltip("Flip the panel so its front face points outward instead of into the walkable cell (the WallFX shader is single-sided)")]
    public bool visualFaceOutward = false;

    [Header("Visual ring animation (WallFX shader)")]
    [Tooltip("On: the values below override the material's own Ring Speed / Ring Loop / Ring Curve on every wall panel (via MaterialPropertyBlock, the material asset is not modified).\nOff: the material's own settings are used")]
    public bool overrideRingSettings = true;
    [Tooltip("Ring animation speed. 1 = one cycle per second, 0.2 = one cycle per 5 seconds")]
    [Range(0.01f, 5f)] public float ringSpeed = 0.25f;
    [Tooltip("Repeat the ring animation forever (off = play once after load)")]
    public bool ringLoop = true;
    [Tooltip("Easing exponent: 0.2 = fast start then slow (pack default), 1 = linear, >1 = slow start then fast")]
    [Range(0.05f, 3f)] public float ringCurve = 0.2f;

    [Header("Debug")]
    public bool drawGizmos = true;
    public Color gizmoColor = new Color(1f, 0.55f, 0.1f, 0.9f);
    public bool verboseLog = false;
    [SerializeField] private int activeWallReadout;
    [SerializeField] private int rebuildCountReadout;

    /// Number of wall segments currently active
    public int ActiveWalls => active.Count;

    // Edge key -> collider
    private readonly Dictionary<int, BoxCollider> active = new Dictionary<int, BoxCollider>();
    private readonly Stack<BoxCollider> pool = new Stack<BoxCollider>();
    private readonly List<int> scratchKeys = new List<int>();
    private readonly HashSet<int> wanted = new HashSet<int>();
    private bool[,] last;                 // copy of the walkability array from the last rebuild
    private readonly Dictionary<BoxCollider, Renderer> visuals = new Dictionary<BoxCollider, Renderer>();
    private Bounds visualMeshBounds;      // local bounds of the visual prefab's mesh (measured once)
    private bool visualMeasured;
    private MaterialPropertyBlock mpb;
    private static readonly int RingSpeedId = Shader.PropertyToID("_SpeedAnimatedMode");
    private static readonly int RingLoopId = Shader.PropertyToID("_RingLoop");
    private static readonly int RingCurveId = Shader.PropertyToID("_RingCurve");
    private float[,] floorY;              // per-cell floor height cache (valid for one rebuild)
    private Transform wallRoot;

    // 4 sides: +X, -X, +Z, -Z
    private static readonly Vector2Int[] Dirs = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };

    private void OnEnable()
    {
        if (grid == null) grid = GridSystem.Instance;
        last = null;   // force a rebuild
    }

    private void OnDisable()
    {
        foreach (var kv in active) if (kv.Value != null) kv.Value.gameObject.SetActive(false);
        foreach (var kv in active) if (kv.Value != null) pool.Push(kv.Value);
        active.Clear();
        last = null;
    }

    private void Update()
    {
        if (grid == null) { grid = GridSystem.Instance; if (grid == null) return; }
        var w = grid.Walkable;
        if (w == null) return;

        if (Changed(w)) Rebuild(w);
        activeWallReadout = active.Count;

        if (ringDirty) { ApplyRingSettings(); ringDirty = false; }
    }

    private bool ringDirty = true;

#if UNITY_EDITOR
    private void OnValidate()
    {
        ringDirty = true;
        last = null;   // re-place visuals with the new padding / inset / height values
    }
#endif

    /// Force a rebuild on the next frame (e.g. after teleporting a bridge)
    public void MarkDirty() => last = null;

    // Compare the current walkability array with the copy from the last rebuild (cheap: a few thousand bools)
    private bool Changed(bool[,] w)
    {
        int cols = w.GetLength(0), rows = w.GetLength(1);
        if (last == null || last.GetLength(0) != cols || last.GetLength(1) != rows)
        {
            last = new bool[cols, rows];
            System.Array.Copy(w, last, w.Length);
            return true;
        }
        bool changed = false;
        for (int x = 0; x < cols && !changed; x++)
            for (int z = 0; z < rows; z++)
                if (w[x, z] != last[x, z]) { changed = true; break; }
        if (changed) System.Array.Copy(w, last, w.Length);
        return changed;
    }

    private void Rebuild(bool[,] w)
    {
        int cols = w.GetLength(0), rows = w.GetLength(1);
        if (wallRoot == null)
        {
            var go = new GameObject("AirWalls");
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(transform, false);
            wallRoot = go.transform;
        }
        if (floorY == null || floorY.GetLength(0) != cols || floorY.GetLength(1) != rows) floorY = new float[cols, rows];
        for (int x = 0; x < cols; x++) for (int z = 0; z < rows; z++) floorY[x, z] = float.NaN;

        wanted.Clear();
        float cs = grid.cellSize;
        float len = cs + cornerOverlap * 2f;
        float totalH = wallHeight + wallSink;

        for (int x = 0; x < cols; x++)
        {
            for (int z = 0; z < rows; z++)
            {
                if (!w[x, z]) continue;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + Dirs[d].x, nz = z + Dirs[d].y;
                    bool inside = nx >= 0 && nx < cols && nz >= 0 && nz < rows;
                    bool open = inside ? w[nx, nz] : !wallGridBorder;   // outside the grid counts as a drop unless disabled
                    if (open) continue;

                    int key = ((x * rows) + z) * 4 + d;
                    wanted.Add(key);

                    if (!active.TryGetValue(key, out BoxCollider bc) || bc == null)
                    {
                        bc = pool.Count > 0 ? pool.Pop() : CreateWall();
                        bc.gameObject.SetActive(true);
                        active[key] = bc;
                    }

                    // Placement: centred on the shared edge, long axis perpendicular to the direction
                    Vector3 c = grid.CellToWorld(x, z);
                    float fy = FloorY(x, z, c);
                    Vector3 pos = c + new Vector3(Dirs[d].x, 0f, Dirs[d].y) * (cs * 0.5f);
                    pos.y = fy - wallSink + totalH * 0.5f;
                    bool alongZ = Dirs[d].x != 0;   // wall on an X side runs along Z
                    Vector3 size = alongZ ? new Vector3(wallThickness, totalH, len) : new Vector3(len, totalH, wallThickness);

                    var t = bc.transform;
                    if (t.position != pos) t.position = pos;
                    if (bc.size != size) bc.size = size;
                    bc.gameObject.layer = wallLayer;
                    bc.sharedMaterial = wallMaterial;

                    PlaceVisual(bc, pos, fy, Dirs[d], alongZ, cs);
                }
            }
        }

        // Retire walls that are no longer on an edge
        scratchKeys.Clear();
        foreach (var kv in active) if (!wanted.Contains(kv.Key)) scratchKeys.Add(kv.Key);
        for (int i = 0; i < scratchKeys.Count; i++)
        {
            var bc = active[scratchKeys[i]];
            active.Remove(scratchKeys[i]);
            if (bc == null) continue;
            bc.gameObject.SetActive(false);
            pool.Push(bc);
        }

        rebuildCountReadout++;
        if (verboseLog) Debug.Log($"[GridAirWalls] rebuilt: {active.Count} wall segments", this);
    }

    // -- Visual panel per segment --
    private void PlaceVisual(BoxCollider bc, Vector3 edgeCentre, float floorY, Vector2Int dir, bool alongZ, float cs)
    {
        if (wallVisualPrefab == null || !showVisuals)
        {
            if (visuals.TryGetValue(bc, out Renderer old) && old != null) old.gameObject.SetActive(false);
            return;
        }

        if (!visuals.TryGetValue(bc, out Renderer r) || r == null)
        {
            var go = Instantiate(wallVisualPrefab, bc.transform);
            go.name = "AirWallFX";
            go.hideFlags = HideFlags.DontSave;
            go.layer = wallLayer;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);   // visuals never collide
            r = go.GetComponentInChildren<Renderer>();
            visuals[bc] = r;
            if (!visualMeasured) MeasureVisual(go);
            ringDirty = true;
        }
        if (r == null) return;
        r.gameObject.SetActive(true);

        // Mesh local axes: Y = height, Z = length, +X = front. Scale it to (wallHeight * visualHeightScale) x (cellSize + padding)
        Vector3 bsize = visualMeshBounds.size;
        float sy = bsize.y > 1e-4f ? (wallHeight * visualHeightScale) / bsize.y : 1f;
        float sz = bsize.z > 1e-4f ? (cs + visualLengthPadding) / bsize.z : 1f;
        float sx = Mathf.Max(sy, sz);                // thin axis: same scale as the panel so the shader's push/curvature keeps its proportions

        // Front (+X of the mesh) faces into the walkable cell (= -dir) unless flipped
        Vector3 outward = new Vector3(dir.x, 0f, dir.y);
        Vector3 front = visualFaceOutward ? outward : -outward;
        // local X -> front, local Y -> up, local Z -> front x up  (LookRotation gives X = up x forward = front)
        Quaternion rot = Quaternion.LookRotation(Vector3.Cross(front, Vector3.up), Vector3.up);

        Vector3 scale = new Vector3(sx, sy, sz);
        // Where the mesh's bounds centre should end up: at the edge, moved into the cell by visualInset, bottom on the floor
        Vector3 target = edgeCentre - outward * visualInset;
        target.y = floorY + visualYOffset + wallHeight * visualHeightScale * 0.5f;
        Vector3 centreOffset = rot * Vector3.Scale(visualMeshBounds.center, scale);

        Transform t = r.transform;                                   // walk up to the instantiated prefab root
        while (t.parent != null && t.parent != bc.transform) t = t.parent;
        t.SetPositionAndRotation(target - centreOffset, rot);
        t.localScale = scale;
    }

    private void MeasureVisual(GameObject instance)
    {
        var mf = instance.GetComponentInChildren<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            visualMeshBounds = mf.sharedMesh.bounds;
            visualMeasured = true;
            if (verboseLog) Debug.Log($"[GridAirWalls] visual mesh bounds: centre {visualMeshBounds.center} size {visualMeshBounds.size}", this);
        }
        else
        {
            visualMeshBounds = new Bounds(Vector3.zero, Vector3.one);
            visualMeasured = true;
        }
    }

    /// Push the ring animation settings to every visual panel (MaterialPropertyBlock: the material asset stays untouched)
    private void ApplyRingSettings()
    {
        if (mpb == null) mpb = new MaterialPropertyBlock();
        foreach (var kv in visuals)
        {
            var r = kv.Value;
            if (r == null) continue;
            if (!overrideRingSettings) { r.SetPropertyBlock(null); continue; }
            r.GetPropertyBlock(mpb);
            mpb.SetFloat(RingSpeedId, ringSpeed);
            mpb.SetFloat(RingLoopId, ringLoop ? 1f : 0f);
            mpb.SetFloat(RingCurveId, ringCurve);
            r.SetPropertyBlock(mpb);
        }
    }

    private BoxCollider CreateWall()
    {
        var go = new GameObject("AirWall");
        go.hideFlags = HideFlags.DontSave;
        go.layer = wallLayer;
        go.transform.SetParent(wallRoot, false);
        var bc = go.AddComponent<BoxCollider>();
        bc.sharedMaterial = wallMaterial;
        return bc;
    }

    // Floor height at a cell: ray down onto the walkable layers, cached per rebuild; falls back to the grid plane
    private float FloorY(int x, int z, Vector3 cellCentre)
    {
        if (!snapToFloor) return grid.GridY;
        float cached = floorY[x, z];
        if (!float.IsNaN(cached)) return cached;

        float y = grid.GridY;
        Vector3 from = new Vector3(cellCentre.x, grid.GridY + floorProbeUp, cellCentre.z);
        if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, floorProbeUp + floorProbeDown, grid.walkableMask, QueryTriggerInteraction.Ignore))
            y = hit.point.y;
        floorY[x, z] = y;
        return y;
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;
        GridSystem g = grid != null ? grid : GridSystem.Instance;
        if (g == null || g.Walkable == null) return;

        Gizmos.color = gizmoColor;
        if (Application.isPlaying && active.Count > 0)
        {
            foreach (var kv in active)
            {
                if (kv.Value == null) continue;
                Gizmos.matrix = kv.Value.transform.localToWorldMatrix;
                Gizmos.DrawWireCube(kv.Value.center, kv.Value.size);
            }
            Gizmos.matrix = Matrix4x4.identity;
            return;
        }

        // Edit mode preview: the edges that would get walls, drawn on the grid plane
        var w = g.Walkable;
        int cols = w.GetLength(0), rows = w.GetLength(1);
        float cs = g.cellSize, h = cs * 0.5f;
        for (int x = 0; x < cols; x++)
            for (int z = 0; z < rows; z++)
            {
                if (!w[x, z]) continue;
                Vector3 c = g.CellToWorld(x, z);
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + Dirs[d].x, nz = z + Dirs[d].y;
                    bool inside = nx >= 0 && nx < cols && nz >= 0 && nz < rows;
                    bool open = inside ? w[nx, nz] : !wallGridBorder;
                    if (open) continue;
                    Vector3 mid = c + new Vector3(Dirs[d].x, 0f, Dirs[d].y) * h;
                    Vector3 along = Dirs[d].x != 0 ? new Vector3(0f, 0f, h) : new Vector3(h, 0f, 0f);
                    Gizmos.DrawLine(mid - along, mid + along);
                    Gizmos.DrawLine(mid, mid + Vector3.up * wallHeight);
                }
            }
    }
}
