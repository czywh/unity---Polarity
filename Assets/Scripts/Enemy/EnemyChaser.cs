using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Enemy chase AI: uses A* over GridSystem's walkable grid cells to chase a target.
/// Multiple targets allowed (Player / Robot); each time it picks the [nearest target whose cell is walkable / reachable].
/// Moves cell by cell along the path. Recomputes periodically when bridges / targets move.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class EnemyChaser : MonoBehaviour
{
    [System.Flags]
    public enum TargetMask { None = 0, Player = 1, Robot = 2 }

[Header("Energy (moving drains energy; no energy = stop chasing; recharges inside electric field)")]
[Tooltip("Enemy's energy component (empty = use EnergySystem on the same object)")]
    public EnergySystem energy;
[Tooltip("Energy drained per second while moving")]
    public float drainPerSecond = 8f;
[Tooltip("Energy recharged per second while inside an electric field (Robot's E field covering the enemy -> recharge)")]
    public float rechargePerSecond = 20f;
[Tooltip("Whether recharging inside an electric field is allowed")]
    public bool rechargeInField = true;

[Header("Chase Targets (multiple allowed)")]
    public TargetMask targets = TargetMask.Player | TargetMask.Robot;
[Tooltip("Player Transform (used when chasing Player)")]
    public Transform player;
[Tooltip("Robot Transform (used when chasing Robot)")]
    public Transform robot;

[Header("Patrol (visits each patrol point in order; add as many as needed)")]
[Tooltip("Patrol points (looped in order). Empty = no patrol, stand by at start point")]
    public Transform[] patrolPoints;
[Tooltip("How long to wait at each patrol point (seconds)")]
    public float patrolWait = 5f;
[Tooltip("Distance threshold for reaching a patrol point")]
    public float patrolArriveDistance = 1.5f;

[Header("Fallback Wait Points (retreat to the nearest reachable one when start point is unreachable)")]
[Tooltip("When the start point is blocked (e.g. an electric bridge dropped) and can't be reached, the enemy retreats to the [nearest reachable point] among these and waits")]
    public Transform[] fallbackWaitPoints;

[Header("Idle Facing (face the direction with the most walkable cells)")]
[Tooltip("When idle, face the direction with the densest walkable cells; off = keep start/current facing")]
    public bool faceOpenDirection = true;
[Tooltip("Scan range (cells): side length centered on self. 5 = 5x5")]
    public int facingScanCells = 5;
[Tooltip("Scan visualization: draw scan range and facing in Scene + Game view")]
    public bool drawFacingScan = false;
    public Color scanColor = new Color(0.3f, 1f, 0.5f, 0.9f);

[Header("Chase Detection (sector in facing direction; chase any target that enters it)")]
[Tooltip("Sector radius (world units)")]
    public float viewRadius = 8f;
[Tooltip("Total sector angle (degrees)")]
    [Range(0f, 360f)] public float viewAngle = 90f;
[Tooltip("Line-of-sight occlusion layers: if set, checks whether the target is blocked by walls (empty = no occlusion check)")]
    public LayerMask viewObstacles;

[Header("Sector Visualization (visible in Scene + Game view)")]
    public bool drawViewFan = true;
    public Color fanColor = new Color(1f, 0.35f, 0.2f, 0.22f);

[Header("Pathfinding")]
[Tooltip("Grid; empty = use GridSystem.Instance")]
    public GridSystem grid;
    public bool allowDiagonal = true;
[Tooltip("Interval in seconds between path recalculations")]
    public float repathInterval = 0.4f;

[Header("Movement")]
    public float moveSpeed = 3.5f;
    public float turnSpeed = 720f;
[Tooltip("Lower bound of speed multiplier when slowed by external effects (lasers etc.). 0.33 = can slow to at most 1/3 of base speed")]
    public float minSpeedMultiplier = 0.1f;
[Tooltip("Distance threshold for reaching a path point")]
    public float arriveThreshold = 0.15f;
    public float gravity = -25f;

[Header("Attack (catch target -> delayed target death, then return to start/stand by)")]
[Tooltip("Kill the target after catching up to it")]
    public bool killOnReach = true;
[Tooltip("How close to the target counts as caught")]
    public float killDistance = 1.2f;
[Tooltip("How long to stay close after catching before the target dies (warning window; escaping cancels it)")]
    public float killDelay = 0.5f;
[Tooltip("This close to the start point counts as \"home\"")]
    public float homeArriveDistance = 0.6f;
[Tooltip("After killing the target, how long to wait in place before returning to start")]
    public float postKillPause = 0.5f;

[Header("Debug")]
    public bool drawPath = true;
[SerializeField] private string currentTargetName = "(none)";

    private CharacterController controller;
    private EnemyDeath selfDeath;
    private Material fanMat;
    private float verticalVelocity;
    private List<Vector2Int> path;
    private int pathIndex;
    private float nextRepath;
    private Transform currentTarget;
private float reachTimer;         // Stay-close timer (0 -> killDelay)
private Vector3 homePosition;     // Start point
private Quaternion homeRotation;  // Start facing (restored after returning home)
private float pauseTimer;         // Post-kill pause timer

// Chase state machine
    public enum ChaseState { Idle, Patrolling, PatrolWaiting, Chasing, Waiting, Returning }
[Header("Debug (runtime, read-only)")]
    [SerializeField] private ChaseState state = ChaseState.Idle;
[Tooltip("Current movement speed multiplier: 1 = normal, drops to 1/3 while hit by a laser")]
    [SerializeField] private float speedMultiplierReadout = 1f;
    public ChaseState State => state;
private Transform lockedTarget;   // Locked target (locked on entering sector, chased to the end)
private int patrolIndex;          // Current patrol point index
private float patrolTimer;        // Patrol point wait timer
private Vector3 returnGoal;       // Current return goal (start point or fallback point)
private bool returnIsHome = true; // Whether the return goal is the start point
private bool hasPower = true;     // Whether currently powered (no power = no chasing, no moving)

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        selfDeath = GetComponent<EnemyDeath>();
        if (energy == null) energy = GetComponent<EnergySystem>();
homePosition = transform.position;   // Record start point
homeRotation = transform.rotation;   // Record start facing
    }

    private void Start()
    {
        if (grid == null) grid = GridSystem.Instance;
    }

    private void Update()
    {
        if (grid == null) grid = GridSystem.Instance;
        if (grid == null) return;

// Recharge inside electric field (Robot's E field covering the enemy -> recharge)
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

// Locked target stays close for killDelay -> target dies -> wrap up (requires power)
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
EnterWaiting();   // Pause in place after kill, then wrap up
                }
            }
            else reachTimer = 0f;
        }
        else reachTimer = 0f;
    }

// Whether the enemy is inside any electric field (precise check via colliders)
    private bool InAnyField()
    {
        var mgr = ElectricFieldManager.Instance;
        if (mgr == null) return false;
        return controller != null ? mgr.IsColliderInAnyField(controller)
                                   : mgr.IsInsideAnyField(transform.position);
    }

// State progression: Idle finds via sector -> lock; Chasing chases relentlessly; Waiting pauses after kill; Returning goes back to start -> Idle
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
// Reached current patrol point -> wait
                Transform pt = CurrentPatrolPoint();
                if (pt != null && Flat(transform.position, pt.position) <= patrolArriveDistance)
                    EnterPatrolWait();
                break;

            case ChaseState.PatrolWaiting:
                if (DetectAndLock()) break;
                patrolTimer -= Time.deltaTime;
                if (patrolTimer <= 0f)
                {
// Head to the next patrol point (unreachable ones are skipped automatically in Repath)
                    patrolIndex = (patrolIndex + 1) % Mathf.Max(1, patrolPoints.Length);
                    state = ChaseState.Patrolling;
                    nextRepath = 0f;
                }
                break;

            case ChaseState.Chasing:
// Once locked, ignore the sector and chase to the end. Target lost (destroyed/dying) -> pause then wrap up.
// For other ways to stop, call StopChasing().
                if (lockedTarget == null || IsDying(lockedTarget))
                    EnterWaiting();
                break;

            case ChaseState.Waiting:
// After kill / loss, pause in place for postKillPause seconds, then wrap up (back to patrol or start)
                pauseTimer -= Time.deltaTime;
                if (pauseTimer <= 0f) AfterWaiting();
                break;

            case ChaseState.Returning:
                if (DetectAndLock()) break;
                if (Flat(transform.position, returnGoal) <= homeArriveDistance)
                {
// Reached return goal: stop moving, then turn
                    path = null;
                    bool aligned = returnIsHome
? RotateToHomeRotation()          // Back at start -> turn back to the game-start facing
: RotateToOpenDirection();         // At a fallback point -> face the direction with the most walkable cells
                    if (aligned) state = ChaseState.Idle;
                }
                break;
        }
    }

// Lock on and switch to Chasing when the sector spots a target; returns whether one was found
    private bool DetectAndLock()
    {
if (!hasPower) return false;   // No power, no chasing
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

// Wrap-up after pause: has patrol points -> back to patrol; otherwise -> return to start
    private void AfterWaiting()
    {
        if (HasPatrol()) { state = ChaseState.Patrolling; nextRepath = 0f; }
        else BeginReturning();
    }

// Smoothly turn back to start facing; returns true once aligned
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

// Enter in-place pause after kill / losing target
    private void EnterWaiting()
    {
        lockedTarget = null;
        currentTarget = null;
        reachTimer = 0f;
        pauseTimer = postKillPause;
        state = ChaseState.Waiting;
        path = null;
    }

/// <summary>Unified external "stop chasing" entry point. Any future mechanic (timeout / stunned / target enters safe zone...) should call this -> drop current target, return to patrol/start.</summary>
// --------------------- External slowdown (lasers etc.) ---------------------

    private float slowMultiplier = 1f;
    private float slowUntil = -1f;

/// <summary>Current movement speed multiplier: 1 = normal, &lt; 1 while slowed. Restores automatically on expiry.</summary>
    public float CurrentSpeedMultiplier => Time.time <= slowUntil ? slowMultiplier : 1f;

    /// <summary>
/// Applies a slowdown. Must be [called continuously] to sustain it: duration is "how long it lasts after leaving the source",
/// not a one-off total duration. The laser calls this every FixedUpdate, so the enemy recovers naturally once out of the beam.
    ///
/// With multiple simultaneous sources, the strongest (smallest multiplier) wins, no multiplicative stacking -- otherwise three lasers would nearly freeze the enemy.
    /// </summary>
    public void ApplySlow(float multiplier, float duration)
    {
        multiplier = Mathf.Clamp(multiplier, minSpeedMultiplier, 1f);

if (Time.time > slowUntil) slowMultiplier = multiplier;              // Previous one expired, start fresh
else slowMultiplier = Mathf.Min(slowMultiplier, multiplier);         // Still slowed, take the stronger one

        slowUntil = Mathf.Max(slowUntil, Time.time + duration);
    }

/// <summary>Immediately clear the slowdown (for respawn, teleport, etc.)</summary>
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

// Caught target dies; if the target is Robot, the enemy gains energy equal to Robot's current energy
    private void KillTarget(Transform target)
    {
        var dh = target.GetComponentInParent<CharacterDeathHandler>();
        if (dh != null && !dh.IsDying) dh.Die();

// Target identity: Robot -> recharge; Player -> not handled yet (reserved for extension: could add "catching Player = game over")
        var id = target.GetComponentInParent<CharacterId>();
        if (id != null && id.characterType == CharacterType.Robot)
        {
            var robotEnergy = target.GetComponentInParent<EnergySystem>();
            if (robotEnergy != null && energy != null)
energy.Recharge(robotEnergy.CurrentEnergy);   // Enemy energy += Robot's current energy
        }
        else if (id != null && id.characterType == CharacterType.Player)
        {
// TODO: Could add a "caught Player" check here later (e.g. game over). Not done for now.
            OnCaughtPlayer(target);
        }
    }

/// Extension point for catching Player (currently empty). Hook up game over etc. later.
    private void OnCaughtPlayer(Transform player) { }

// Compute path by state
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
// Starting from the current patrol point, find the next "reachable" one; if the player's changes cut it off, skip to the one after
if (!TryPathToPatrol(startCell)) EnterPatrolWait();   // None reachable -> wait in place, retry next round
                break;

default:   // Idle / PatrolWaiting / Waiting -> don't move
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

// Return path decision: (1) start point first; (2) start unreachable -> nearest reachable fallback point; (3) none reachable -> stay put
    private void ResolveReturnPath(Vector2Int startCell)
    {
// (1) Already within start point range -> home
        if (Flat(transform.position, homePosition) <= homeArriveDistance)
        {
            returnGoal = homePosition; returnIsHome = true;
currentTargetName = "(at start)"; path = null; return;
        }

// (1) Start point reachable -> go back to start
        List<Vector2Int> homePath = TryPath(startCell, homePosition);
        if (homePath != null)
        {
            returnGoal = homePosition; returnIsHome = true;
currentTargetName = "(returning to start)"; path = homePath; pathIndex = 0; return;
        }

// (2) Start unreachable -> nearest reachable fallback point
        if (TryNearestFallback(startCell))
        {
            returnIsHome = false;
currentTargetName = "(retreating to fallback)"; return;
        }

// (3) None reachable -> wait in place, facing the direction with the most walkable cells
        returnGoal = transform.position; returnIsHome = false;
currentTargetName = "(no retreat path)"; path = null;
    }

// Grid path to a world point (returns null if unreachable)
    private List<Vector2Int> TryPath(Vector2Int startCell, Vector3 worldGoal)
    {
        Vector2Int goal = grid.WorldToCell(worldGoal);
        if (!grid.IsWalkable(goal)) goal = NearestWalkable(goal);
        if (startCell.x < 0 || goal.x < 0) return null;
        return AStarPathfinder.FindPath(grid, startCell, goal, allowDiagonal);
    }

// Pick the nearest reachable fallback point, set returnGoal / path; returns true if found
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
if (d >= bestD) continue;   // Keep only closer ones

// Already within this fallback point's range -> pick it directly (no path needed)
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
path = bestPath;      // null when already on the point (arrived)
            pathIndex = 0;
        }
        return found;
    }

// Smoothly turn toward the direction with "the most walkable cells around"; returns true once aligned
    private bool RotateToOpenDirection()
    {
        if (!faceOpenDirection) return true;

        Vector3 dir = ComputeOpenDirection();
if (dir.sqrMagnitude < 1e-4f) return true;   // No clear open direction around -> done

        Quaternion target = Quaternion.LookRotation(dir, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
        return Quaternion.Angle(transform.rotation, target) <= 0.5f;
    }

// Scan walkable cells in the surrounding facingScanCells x facingScanCells area; return the "densest walkable" direction
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
sum += w.normalized;   // Each walkable cell contributes a unit direction -> the sum points toward the densest open area
            }

        sum.y = 0f;
        return sum.sqrMagnitude < 1e-4f ? Vector3.zero : sum.normalized;
    }

// Try patrol points in order from patrolIndex, settle on the first reachable one (skipping cut-off ones); returns false if none reachable
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
patrolIndex = idx;               // Jump to this reachable point
                path = p; pathIndex = 0;
currentTargetName = $"(patrol->{pt.name})";
                return true;
            }
        }
return false;   // None reachable
    }

    private static float Flat(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

// Sector detection of targets (only for "spotting" in non-chase states, not for continuous chase checks)
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
if (!grid.IsWalkable(tCell) && NearestWalkable(tCell).x < 0) return;   // Target must be reachable

        float d = (t.position - transform.position).sqrMagnitude;
        if (d < bestD) { bestD = d; best = t; }
    }

/// Whether a world point lies within the enemy's facing sector (radius + angle + optional line-of-sight occlusion)
    public bool InViewFan(Vector3 worldPos)
    {
        Vector3 to = worldPos - transform.position;
        to.y = 0f;
        float dist = to.magnitude;
        if (dist > viewRadius || dist < 0.001f) return false;

        Vector3 fwd = transform.forward; fwd.y = 0f;
        if (Vector3.Angle(fwd, to) > viewAngle * 0.5f) return false;

// Line-of-sight occlusion (optional)
        if (viewObstacles.value != 0 &&
            Physics.Raycast(transform.position, to.normalized, dist - 0.1f, viewObstacles, QueryTriggerInteraction.Ignore))
            return false;

        return true;
    }

// Move cell by cell along the path
    private void FollowPath()
    {
        Vector3 move = Vector3.zero;
        speedMultiplierReadout = CurrentSpeedMultiplier;

// Only move when powered; moving drains energy
        if (hasPower && path != null && pathIndex < path.Count)
        {
            Vector3 targetPos = grid.CellToWorld(path[pathIndex]);
            Vector3 flatPos = new Vector3(transform.position.x, targetPos.y, transform.position.z);

            if (Vector3.Distance(flatPos, targetPos) <= arriveThreshold)
            {
pathIndex++;   // Reached point, next cell
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
energy.Drain(drainPerSecond * Time.deltaTime);   // Only chasing drains energy (patrol/return don't)
                }
            }
        }

// Gravity
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

// Find the nearest walkable cell within a small range (fallback when target/self stands off-grid); returns (-1,-1) if none
    private Vector2Int NearestWalkable(Vector2Int c)
    {
        if (grid.IsWalkable(c)) return c;
for (int r = 1; r <= 3; r++)   // Expand outward up to 3 cells
        {
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                {
if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue; // Check ring only
                    Vector2Int n = new Vector2Int(c.x + dx, c.y + dy);
                    if (grid.IsWalkable(n)) return n;
                }
        }
        return new Vector2Int(-1, -1);
    }

    private void OnDrawGizmos()
    {
// Path
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

// Sector (Scene view)
        if (drawViewFan) DrawFanGizmo();

// Patrol points + connecting lines
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

// Fallback wait points
        if (fallbackWaitPoints != null)
        {
            Gizmos.color = new Color(1f, 0.7f, 0.2f, 0.9f);
            foreach (var fp in fallbackWaitPoints)
                if (fp != null) { Gizmos.DrawWireSphere(fp.position, 0.45f); Gizmos.DrawLine(transform.position, fp.position); }
        }

// Idle facing scan range + computed open direction
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

// -- Draw sector at runtime in Game view (GL fill) --
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