using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// LEVEL  Two-way portal. Put one on each of two Energy walls and drag each into the other's linkedPortal.
/// Characters (player / robot / enemy, anything with a CharacterController or Rigidbody) that pass through A appear at B, and vice versa.
///
/// How it works:
///   - This object needs a [Trigger] collider (the script auto-sets the first Collider to isTrigger at runtime);
///   - Entering the trigger -> teleport to the other portal's exit point (exitPoint; if empty, other portal position + its forward x exitOffset);
///   - Two exit modes (exitMode): KeepDirection = facing unchanged, come out the far side of B and keep walking (default, suits two walls facing the same way);
///     MirrorThroughPortal = enter A's front, exit B's front; facing rotates by the portals' relative angle ("Portal" style);
///   - After teleporting, the target gets a short cooldown so it is not immediately sent back when landing in the other portal (ping-pong);
///   - CharacterController must be disabled then re-enabled around the teleport (same as CharacterDeathHandler.TeleportTo);
///   - When the main character teleports, the camera snaps over (OrbitFollowCamera.SetTarget(target, instant:true)), otherwise it flies across;
///     in Operation Mode the camera is fixed at the console view (OrbitFollowCamera disabled), so the camera is left alone;
///   - Teleporting cancels any ongoing right-click pathfinding (PlayerConsoleNavigator.Stop), so the character does not walk back through the portal along the old path.
///
/// Power (requirePower, on by default): the portal starts OFF -- the energy wall mesh is hidden and nothing is teleported.
///   It turns on only while an electric field (the robot's field or one opened by an Energy Relay) touches the wall's
///   trigger collider (field sphere within powerMargin of the collider). When the field leaves, the wall switches off again
///   after powerLinger seconds. The pair is powered together (powerLinkedToo, default): a field at either wall opens both
///   ends and both walls become visible; uncheck it on both to make each end need its own field.
///
/// Enemies: by default they are NOT teleported, and while the wall is powered a solid "EnemyBlocker" collider inside the
///   trigger stops them (player / robot ignore that collider via Physics.IgnoreCollision, so only enemies are held back).
///
/// Setup: put a Portal on each wall and drag the other into Linked Portal; you can also fill in only one, the other is back-filled automatically.
/// </summary>
[DisallowMultipleComponent]
public class Portal : MonoBehaviour
{
    [Header("Link")]
    [Tooltip("The portal on the other side. Filling only one side is fine; the other side is linked back in Awake")]
    public Portal linkedPortal;

    [Header("Exit")]
    [Tooltip("Exit point (optional): an empty child placed where/facing the character should appear. If empty, uses this portal's position + forward x exitOffset")]
    public Transform exitPoint;
    [Tooltip("Without exitPoint: how far to push out from the portal center along forward (so the character does not spawn inside the trigger)")]
    public float exitOffset = 1.5f;
    public enum ExitMode
    {
        [InspectorName("Keep direction (pass through A, exit the far side of B, keep going)")] KeepDirection,
        [InspectorName("Mirror (enter A's front -> exit B's front, facing rotates with the portal)")] MirrorThroughPortal,
    }
    [Tooltip("KeepDirection: character world facing is unchanged, exits on B's \"direction of travel\" side; suits levels where both walls face the same way;\nMirrorThroughPortal: like \"Portal\", facing rotates by the relative angle of the two portals")]
    public ExitMode exitMode = ExitMode.KeepDirection;

    [Header("Filter")]
    [Tooltip("Which layers can be teleported; all by default")]
    public LayerMask affectedLayers = ~0;
    [Tooltip("Teleport the character that owns the collider: the nearest ancestor with a CharacterController / Rigidbody / CharacterDeathHandler.\nNever transform.root -- enemies are usually grouped under a level container, and teleporting that would drag the whole level along")]
    public bool useRootObject = true;

    [Header("Anti Ping-Pong")]
    [Tooltip("After teleporting, time during which no portal can teleport it again (seconds)")]
    public float cooldown = 0.5f;

    [Header("Camera")]
    [Tooltip("When the teleported object is the main camera's follow target, snap the camera. If empty, uses the OrbitFollowCamera on Main Camera")]
    public OrbitFollowCamera followCamera;

    [Header("Power (activated by electric fields)")]
    [Tooltip("Checked: the portal starts OFF (wall hidden, no teleport) and works only while an electric field -- the robot's or an Energy Relay's -- touches the wall")]
    public bool requirePower = true;
    [Tooltip("How close (world units) a field sphere must come to the wall's collider to count as 'next to' it. 0 = must overlap")]
    public float powerMargin = 0.5f;
    [Tooltip("Keep the portal on for this long after the last field leaves (seconds), so it does not flicker at the field edge")]
    public float powerLinger = 0.3f;
    [Tooltip("Objects shown only while powered. Empty = the child named 'Energy_wall' (emitters stay visible as a hint)")]
    public GameObject[] wallVisuals;
    [Tooltip("The two walls are a pair: a field on either wall powers BOTH ends (default). Off = each end needs its own field")]
    public bool powerLinkedToo = true;
    [Tooltip("Teleporting also requires the destination wall to be powered")]
    public bool requireLinkedPowered = false;
    [SerializeField] private bool poweredReadout;

    /// True when teleporting is allowed right now (always true when requirePower is off)
    public bool IsPowered { get; private set; }
    /// This wall's own field contact (ignores powerLinkedToo), used by the linked portal
    public bool HasOwnField { get; private set; }

    private Collider triggerCollider;
    private float lastFieldTime = -999f;

    [Header("Enemies")]
    [Tooltip("Off (default): enemies are never teleported. They are stopped by the wall instead (see below)")]
    public bool teleportEnemies = false;
    [Tooltip("Enemies are physically blocked by the wall: a solid collider is created inside the trigger that only enemies collide with (player / robot are told to ignore it)")]
    public bool blockEnemies = true;
    [Tooltip("Only block enemies while the portal is powered (the wall is visible). Off = always block")]
    public bool blockEnemiesOnlyWhenPowered = true;
    [Tooltip("Thickness of the blocking collider as a fraction of the trigger's depth. Must be < 1 so characters overlap the trigger before touching the blocker")]
    [Range(0.1f, 0.9f)] public float blockerThickness = 0.5f;

    [Header("Debug")]
    public bool verboseLog = false;

    private BoxCollider enemyBlocker;

    // Global cooldown table: object -> time it can be teleported again. Shared by both portals so ping-pong is prevented
    private static readonly Dictionary<Transform, float> cooldownUntil = new Dictionary<Transform, float>();

    private void Awake()
    {
        // Ensure trigger
        var col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
        {
            col.isTrigger = true;
            Log("Set collider to Trigger");
        }
        else if (col == null)
        {
            Debug.LogWarning($"[Portal] {name} has no Collider, the portal will never trigger. Add a BoxCollider and check Is Trigger", this);
        }

        // Auto back-fill the other side
        if (linkedPortal != null && linkedPortal.linkedPortal == null)
            linkedPortal.linkedPortal = this;

        if (followCamera == null && Camera.main != null)
            followCamera = Camera.main.GetComponent<OrbitFollowCamera>();

        triggerCollider = col;
        if (wallVisuals == null || wallVisuals.Length == 0)
        {
            var wall = transform.Find("Energy_wall");
            if (wall != null) wallVisuals = new[] { wall.gameObject };
        }

        if (blockEnemies) CreateEnemyBlocker();
    }

    // -- Enemy blocker: a solid box inside the trigger. Player / robot ignore it (Physics.IgnoreCollision), enemies hit it --
    private void CreateEnemyBlocker()
    {
        var box = triggerCollider as BoxCollider;
        var go = new GameObject("EnemyBlocker");
        go.layer = 2;   // Ignore Raycast: lasers, missiles, camera and default raycasts never see it
        go.transform.SetParent(transform, false);
        enemyBlocker = go.AddComponent<BoxCollider>();
        enemyBlocker.isTrigger = false;
        if (box != null)
        {
            enemyBlocker.center = box.center;
            enemyBlocker.size = new Vector3(box.size.x, box.size.y, box.size.z * blockerThickness);
        }
        else
        {
            enemyBlocker.size = new Vector3(2f, 2f, 0.5f);
        }

        // Everyone that is not an enemy passes through
        foreach (var cc in FindObjectsOfType<CharacterController>())
            if (!IsEnemy(cc.transform) && cc.enabled) Physics.IgnoreCollision(enemyBlocker, cc, true);
    }

    private static bool IsEnemy(Transform t)
    {
        return t != null && (t.GetComponentInParent<EnemyChaser>() != null || t.GetComponentInParent<EnemyDeath>() != null);
    }

    /// <summary>Re-apply "ignore" for a non-enemy character. Unity resets IgnoreCollision whenever a collider is disabled
    /// (teleport / respawn disable the CharacterController), so this is called again every time the character is in the trigger.</summary>
    private void LetThrough(Transform t)
    {
        // IgnoreCollision logs an error if either collider is disabled, so only call it when both are live
        if (enemyBlocker == null || !enemyBlocker.enabled || !enemyBlocker.gameObject.activeInHierarchy) return;
        foreach (var c in t.GetComponentsInChildren<Collider>())
            if (c != enemyBlocker && c.enabled && !c.isTrigger) Physics.IgnoreCollision(enemyBlocker, c, true);
    }

    private void Start()
    {
        // Apply the initial power state right away (hidden wall when unpowered)
        UpdatePower(true);
    }

    private void Update()
    {
        UpdatePower(false);

        if (enemyBlocker != null)
        {
            bool on = blockEnemies && (IsPowered || !blockEnemiesOnlyWhenPowered);
            if (enemyBlocker.enabled != on) enemyBlocker.enabled = on;
        }
    }

    // -- Power: electric field contact -> portal on / off, wall visuals shown / hidden --
    private void UpdatePower(bool force)
    {
        bool wasPowered = IsPowered;

        if (!requirePower)
        {
            HasOwnField = true;
            IsPowered = true;
        }
        else
        {
            var mgr = ElectricFieldManager.Instance;
            bool touching = mgr != null && (triggerCollider != null
                ? mgr.IsColliderInAnyField(triggerCollider, powerMargin)
                : mgr.IsInsideAnyField(transform.position));
            if (touching) lastFieldTime = Time.time;
            HasOwnField = touching || Time.time - lastFieldTime <= powerLinger;

            // One field opens both ends when either wall has powerLinkedToo checked
            bool linkedFeeds = linkedPortal != null
                               && (powerLinkedToo || linkedPortal.powerLinkedToo)
                               && linkedPortal.HasOwnField;
            IsPowered = HasOwnField || linkedFeeds;
        }

        poweredReadout = IsPowered;
        if (force || IsPowered != wasPowered)
        {
            SetVisuals(IsPowered);
            if (!force) Log(IsPowered ? "powered ON" : "powered OFF");
        }
    }

    private void SetVisuals(bool on)
    {
        if (wallVisuals == null) return;
        foreach (var go in wallVisuals)
            if (go != null && go.activeSelf != on) go.SetActive(on);
    }

    // OnTriggerStay (not Enter): a character already standing in the wall when it powers on still gets teleported
    private void OnTriggerStay(Collider other)
    {
        if (other == enemyBlocker) return;
        if ((affectedLayers.value & (1 << other.gameObject.layer)) == 0) return;

        Transform t = useRootObject ? FindTeleportTarget(other) : other.transform;
        if (t == null) return;

        // Enemies: never teleported (unless allowed); the blocker collider stops them
        if (IsEnemy(t))
        {
            if (!teleportEnemies) return;
        }
        else
        {
            LetThrough(t);   // player / robot: make sure the blocker ignores them (self-healing, see LetThrough)
        }

        if (linkedPortal == null) { Log("linkedPortal is null, ignoring"); return; }
        if (!IsPowered) return;
        if (requireLinkedPowered && !linkedPortal.IsPowered) return;

        // On cooldown (just came through from the other side)
        if (cooldownUntil.TryGetValue(t, out float until) && Time.time < until) return;

        // Do not teleport during death flow, to avoid fighting with the respawn teleport
        var death = t.GetComponent<CharacterDeathHandler>();
        if (death != null && death.IsDying) return;

        // Which side of the portal the character entered from (forward side = front), and its travel direction (toward the portal center)
        Vector3 toDoor = transform.position - t.position; toDoor.y = 0f;
        bool fromFront = Vector3.Dot(-toDoor, transform.forward) >= 0f;
        linkedPortal.Receive(t, this, fromFront, toDoor.normalized);
    }

    /// <summary>Place the object at this portal's exit. Called by the other portal. fromFront: entered the source portal from its front; travelDir: horizontal travel direction on entry</summary>
    public void Receive(Transform t, Portal from, bool fromFront, Vector3 travelDir)
    {
        Vector3 pos;
        Quaternion rot = t.rotation;

        if (exitPoint != null)
        {
            // Exit point specified: use it for position; in mirror mode, facing is based on it too
            pos = exitPoint.position;
            if (exitMode == ExitMode.MirrorThroughPortal)
                rot = FlattenYaw(exitPoint.rotation * Quaternion.Inverse(from.transform.rotation * Quaternion.Euler(0f, 180f, 0f)) * t.rotation);
        }
        else if (exitMode == ExitMode.KeepDirection)
        {
            // World facing unchanged; exit is on B's "direction of travel" side, so walking on naturally leaves the portal
            float side = Vector3.Dot(travelDir, transform.forward) >= 0f ? 1f : -1f;
            pos = transform.position + transform.forward * (exitOffset * side);
        }
        else
        {
            // Mirror: enter A front -> exit B front; enter A back -> exit B back.
            // Facing mapping: rotate 180 degrees (entry direction = opposite of exit direction), so walking in toward -A.forward -> walks out toward +B.forward
            float side = fromFront ? 1f : -1f;
            pos = transform.position + transform.forward * (exitOffset * side);
            Quaternion delta = transform.rotation * Quaternion.Euler(0f, 180f, 0f) * Quaternion.Inverse(from.transform.rotation);
            rot = FlattenYaw(delta * t.rotation);
        }

        // Height: keep the character's height offset relative to the portal, so portals on floors of different heights do not embed it in the ground
        pos.y = (exitPoint != null ? exitPoint.position.y : transform.position.y)
                + (t.position.y - from.transform.position.y);

        var cc = t.GetComponent<CharacterController>();

        // -- Cooldown: register before teleporting (so the other portal cannot catch it on landing) --
        cooldownUntil[t] = Time.time + cooldown;

        // -- Teleport (CharacterController must be disabled then re-enabled) --
        var rb = t.GetComponent<Rigidbody>();
        if (cc != null) cc.enabled = false;
        if (rb != null)
        {
            // Velocity direction also rotates with the portal
            Quaternion vDelta = rot * Quaternion.Inverse(t.rotation);
            rb.velocity = vDelta * rb.velocity;
            rb.angularVelocity = Vector3.zero;
        }
        t.SetPositionAndRotation(pos, rot);
        Physics.SyncTransforms();
        if (cc != null) cc.enabled = true;

        // -- Camera snap --
        // Only when the follow camera is [enabled]: in Operation Mode OrbitFollowCamera is disabled and the camera is fixed at the console view;
        // calling SetTarget(instant) then would yank the camera onto the character and break the console view.
        if (followCamera != null && followCamera.isActiveAndEnabled &&
            followCamera.target != null && followCamera.target.IsChildOf(t))
            followCamera.SetTarget(followCamera.target, instant: true);

        // -- Cancel ongoing right-click pathfinding: position jumped, the old path is meaningless, and following it would walk back through the portal --
        var nav = t.GetComponent<PlayerConsoleNavigator>();
        if (nav != null) nav.Stop();

        Log($"{t.name}: {from.name} → {name} @ {pos}");
    }

    /// <summary>
    /// Which transform to teleport for a given collider: the nearest ancestor that is actually a character
    /// (CharacterController / Rigidbody / CharacterDeathHandler). Returns null for scenery colliders that belong to no character.
    /// This used to be other.transform.root, which is wrong: enemies (and the player / robot / portals) usually sit under a
    /// shared level container, so teleporting "the root" moved the entire container -- portals, player and robot included.
    /// </summary>
    private static Transform FindTeleportTarget(Collider other)
    {
        if (other.attachedRigidbody != null) return other.attachedRigidbody.transform;
        var cc = other.GetComponentInParent<CharacterController>();
        if (cc != null) return cc.transform;
        var death = other.GetComponentInParent<CharacterDeathHandler>();
        if (death != null) return death.transform;
        return null;
    }

    // Keep only rotation around Y (characters should not be tilted by the portal)
    private static Quaternion FlattenYaw(Quaternion q)
    {
        Vector3 fwd = q * Vector3.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) return Quaternion.identity;
        return Quaternion.LookRotation(fwd.normalized, Vector3.up);
    }

    private void Log(string msg)
    {
        if (verboseLog) Debug.Log($"[Portal] {msg}", this);
    }

    private void OnDrawGizmos()
    {
        if (requirePower)
        {
            // Power state: green = on, grey = off
            Gizmos.color = Application.isPlaying && IsPowered ? new Color(0.3f, 1f, 0.4f, 0.9f) : new Color(0.5f, 0.5f, 0.5f, 0.6f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 1.5f, 0.2f);
        }
        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.9f);
        Vector3 exit = exitPoint != null ? exitPoint.position : transform.position + transform.forward * exitOffset;
        Gizmos.DrawWireSphere(exit, 0.3f);
        Gizmos.DrawLine(transform.position, exit);
        if (linkedPortal != null)
        {
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.6f);
            Gizmos.DrawLine(transform.position, linkedPortal.transform.position);
        }
    }
}
