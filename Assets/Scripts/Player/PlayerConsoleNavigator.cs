using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// PLAYER "Right-click pathfinding" in Operation Mode: in Operation Mode WASD controls the robot, while the player character
/// moves by right-clicking the scene, using A* over GridSystem's walkable cells to reach [the walkable cell nearest the click].
///
/// Flow:
///   - Right click -> mouse ray first hits the bridge layer (grid.walkableMask); if it misses, intersect with the grid plane to get the click point;
///   - If the clicked cell is not walkable, pick the walkable cell [closest in world distance to the click point] within searchRadius cells as the goal;
///   - Start = the player's cell (same nearest fallback if not walkable); AStarPathfinder.FindPath computes the path;
///   - Move cell by cell. Only drives horizontal motion -- vertical gravity is handled by PassiveFall (PlayerController is frozen in Operation Mode,
///     so PassiveFall takes over), and riding moving bridges is handled by PlatformRider; they don't conflict.
///   - If a bridge moves / disconnects mid-walk: re-path automatically when the next cell becomes unwalkable; stop if the goal becomes unwalkable too.
///
/// Self-contained: it checks by itself whether Operation Mode is active (reads OperationModeController.InOperationMode),
/// so OperationModeController needs no changes and this component needs no manual toggling. Stops automatically when leaving Operation Mode.
///
/// Attach to the Player root (same object as CharacterController / PlayerController).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerConsoleNavigator : MonoBehaviour
{
    [Header("References (auto-found if empty)")]
    [Tooltip("Grid; uses GridSystem.Instance if empty")]
    public GridSystem grid;
    [Tooltip("Operation Mode manager; auto-found if empty")]
    public OperationModeController operationMode;
    [Tooltip("Camera used for raycasts; uses Camera.main if empty (the orthographic camera in Operation Mode)")]
    public Camera rayCamera;

    [Header("Input")]
    [Tooltip("Mouse button: 0 left / 1 right / 2 middle")]
    public int mouseButton = 1;

    [Header("Pathfinding")]
    [Tooltip("When the clicked cell is not walkable, radius (in cells) to search outward for the nearest walkable cell")]
    public int searchRadius = 4;
    [Tooltip("Allow diagonal movement (8 directions)")]
    public bool allowDiagonal = true;
    [Tooltip("Distance to count as reaching a cell center (world units, XZ only)")]
    public float arriveThreshold = 0.15f;
    [Tooltip("How long (seconds) without clear progress counts as stuck; being stuck triggers one re-path")]
    public float stuckTime = 0.8f;

    [Header("Movement")]
    [Tooltip("Walk speed; if <=0, reads PlayerController.walkSpeed on the same object")]
    public float moveSpeedOverride = 0f;
    [Tooltip("Turn speed (degrees/sec)")]
    public float turnSpeed = 720f;

    [Header("Goal Marker (optional)")]
    [Tooltip("An object in the scene (e.g. a glowing ring) placed on the goal cell while pathfinding and hidden on arrival. Leave empty to show nothing")]
    public Transform destinationMarker;
    [Tooltip("Marker height above the cell center, to avoid Z-fighting with the bridge surface")]
    public float markerYOffset = 0.05f;

    [Header("Debug")]
    public bool verboseLog = false;
    public bool drawPathGizmos = true;
    [SerializeField] private bool navigatingReadout;
    [SerializeField] private string goalReadout = "(none)";

    // -- Public read-only: for PlayerAnimator to drive the walk animation --
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

        // Not in Operation Mode / dying / teleporting -> don't take over, and clear any leftover path
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

    // --------------------------------------------------------------------
    //  Click -> goal cell
    // --------------------------------------------------------------------

    private void HandleClick()
    {
        Camera c = rayCamera != null ? rayCamera : Camera.main;
        if (c == null) return;

        if (!TryGetClickPoint(c.ScreenPointToRay(Input.mousePosition), out Vector3 clickPoint))
        {
            Log("Click did not land on the grid");
            return;
        }

        Vector2Int goal = NearestWalkable(grid.WorldToCell(clickPoint), clickPoint);
        if (goal.x < 0)
        {
            Log($"Click point {clickPoint}: no walkable cell within {searchRadius} cells");
            return;
        }

        if (!RequestPath(goal))
            Log($"No path to cell {goal} (bridge may not be connected)");
    }

    /// Hit the bridge layer first (most accurate on the bridge surface); if it misses, fall back to the grid's horizontal plane
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

    /// Within searchRadius cells, find the walkable cell closest to referencePoint [in world distance]; returns (-1,-1) if none
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

    // --------------------------------------------------------------------
    //  Pathfinding / following
    // --------------------------------------------------------------------

    private bool RequestPath(Vector2Int goal)
    {
        Vector2Int start = NearestWalkable(grid.WorldToCell(transform.position), transform.position);
        if (start.x < 0) { Log("No walkable cell near the player's feet"); Stop(); return false; }

        List<Vector2Int> p = start == goal
            ? new List<Vector2Int> { goal }   // Already on the goal cell: just walk to the cell center
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
        Log($"Path: {start} -> {goal}, {p.Count} cells");
        return true;
    }

    private void FollowPath()
    {
        if (!IsNavigating) return;

        // Bridge moved / lost power so the next cell is unwalkable -> re-path; stop if even the goal is unwalkable
        if (!grid.IsWalkable(path[pathIndex]))
        {
            if (!grid.IsWalkable(goalCell) || !RequestPath(goalCell))
            {
                Log("Path cut off, stopping");
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
            if (!IsNavigating) { Log("Arrived"); Stop(); }
            return;
        }

        // Stuck detection: no progress toward the next cell for a while -> re-path once
        if (dist < lastDistToNext - 0.05f) { lastDistToNext = dist; lastProgressTime = Time.time; }
        else if (Time.time - lastProgressTime > stuckTime)
        {
            Log("Stuck, re-pathing");
            if (!RequestPath(goalCell)) { Stop(); return; }
        }

        Vector3 dir = to / dist;
        float step = Mathf.Min(MoveSpeed * Time.deltaTime, dist);   // Don't overshoot the cell center
        controller.Move(dir * step);                                // Horizontal only; gravity is left to PassiveFall
        PlanarSpeed = Time.deltaTime > 0f ? step / Time.deltaTime : 0f;

        Quaternion look = Quaternion.LookRotation(dir, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);

        if (destinationMarker != null)
            destinationMarker.position = goalWorldForMarker + Vector3.up * markerYOffset;
    }

    /// Cancel current pathfinding
    public void Stop()
    {
        path = null;
        pathIndex = 0;
        PlanarSpeed = 0f;
        navigatingReadout = false;
        goalReadout = "(none)";
        SetMarker(false);
    }

    // --------------------------------------------------------------------
    //  Misc
    // --------------------------------------------------------------------

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
        if (verboseLog) Debug.Log($"[RightClickNav] {msg}", this);
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
