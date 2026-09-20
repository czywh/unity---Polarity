using UnityEngine;

/// <summary>
/// COMMON - Platform riding. Attach to the root object of player / robot (same object as the CharacterController).
///
/// Problem solved: CharacterController isn't carried by moving colliders. When clicking in Operation Mode moves a
/// ConsoleOperable platform (translate / rotate around pivot), characters standing on it effectively have "the floor pulled out",
/// so they stop in place, get pushed by the platform, or even fall straight into a death zone.
///
/// Approach: each frame, probe for the object underfoot, convert the character's current position into that object's [previous-frame]
/// space, then back to world space using [this frame's] transform; the difference is the platform's rigid motion this frame,
/// applied to the character via controller.Move().
///   - Only compensates platform motion, never cancels the character's own movement (local coords recomputed each frame, no cached old position);
///   - Follows both translation and rotation around a pivot;
///   - On static ground the delta is always 0, so zero side effects on normal terrain; safe to keep always enabled.
///
/// Execution order 200: ensures it runs after ConsoleOperable (platform movement) and the character controllers
/// (RobotController / RobotConsoleMover / PassiveFall, all default 0), so the
/// compensation uses this frame's final state, with no one-frame-lag jitter or foot sliding.
/// </summary>
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]   // Two copies would compensate platform motion twice
[RequireComponent(typeof(CharacterController))]
public class PlatformRider : MonoBehaviour
{
    [Header("Ground Probe")]
    [Tooltip("Downward probe distance. Slightly larger than CharacterController's Skin Width is enough; increase for levels with many steps")]
    public float groundProbeDistance = 0.35f;
    [Tooltip("Which layers count as a \"rideable floor\". Default is all; static ground yields zero delta, no need to exclude it")]
    public LayerMask groundMask = ~0;
    [Tooltip("Minimum y component of the contact normal. Below this it counts as a wall, not a floor, and isn't followed")]
    [Range(0f, 1f)] public float minGroundNormalY = 0.5f;

    [Header("Follow Settings")]
    [Tooltip("Follow platform translation, and displacement caused by rotation around the platform pivot")]
    public bool inheritPosition = true;
    [Tooltip("Follow the platform's Y-axis spin (character turns with it too).\nIn Operation Mode the robot's facing is mouse-controlled; enabling this makes them fight, so it's off by default")]
    public bool inheritYaw = false;

    [Header("Debug")]
    [Tooltip("When on, logs to the Console every time the platform underfoot changes")]
    public bool verboseLog = false;
    [Header("Debug (runtime, read-only)")]
    [SerializeField] private string standingOnReadout = "(none)";

    private CharacterController controller;
    private CharacterDeathHandler death;      // Optional

    private Transform platform;               // Object currently underfoot
    private Vector3 lastPlatformPos;          // That object's world position last frame
    private Quaternion lastPlatformRot;       // That object's world rotation last frame
    private readonly RaycastHit[] hits = new RaycastHit[8];

    /// <summary>Object currently underfoot (null when airborne). For external logic such as animation / audio.</summary>
    public Transform StandingOn => platform;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        death = GetComponent<CharacterDeathHandler>();
    }

    private void OnDisable() => Release();

    private void Update()
    {
        // During death / respawn teleport, position is owned by CharacterDeathHandler; stay out of it
        if ((death != null && death.IsDying) || !controller.enabled)
        {
            Release();
            return;
        }

        Transform found = ProbeGround();

        // Just stepped onto a new platform (or landed): only record the baseline, no usable delta this frame
        if (found != platform)
        {
            Attach(found);
            return;
        }
        if (platform == null) return;

        if (inheritPosition)
        {
            // Character position -> (platform's last-frame space) -> (platform's this-frame space) -> world space
            Matrix4x4 prev = Matrix4x4.TRS(lastPlatformPos, lastPlatformRot, Vector3.one);
            Matrix4x4 now = Matrix4x4.TRS(platform.position, platform.rotation, Vector3.one);

            Vector3 local = prev.inverse.MultiplyPoint3x4(transform.position);
            Vector3 delta = now.MultiplyPoint3x4(local) - transform.position;

            if (delta.sqrMagnitude > 1e-10f) controller.Move(delta);
        }

        if (inheritYaw)
        {
            float yaw = Mathf.DeltaAngle(lastPlatformRot.eulerAngles.y, platform.rotation.eulerAngles.y);
            if (Mathf.Abs(yaw) > 0.0001f) transform.Rotate(0f, yaw, 0f, Space.World);
        }

        CacheBasis();
    }

    /// Sphere-cast down from the capsule's lower hemisphere center to find the nearest object underfoot with a flat enough normal
    private Transform ProbeGround()
    {
        float sideScale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));
        float upScale = Mathf.Abs(transform.lossyScale.y);

        float radius = Mathf.Max(0.01f, controller.radius * sideScale * 0.95f);   // Slightly shrunk to avoid overlapping the ground at the start
        float halfHeight = Mathf.Max(controller.height * upScale * 0.5f, radius);

        Vector3 center = transform.TransformPoint(controller.center);
        Vector3 origin = center - Vector3.up * (halfHeight - radius);

        int n = Physics.SphereCastNonAlloc(origin, radius, Vector3.down, hits,
            groundProbeDistance, groundMask, QueryTriggerInteraction.Ignore);

        Transform best = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < n; i++)
        {
            Collider col = hits[i].collider;
            if (col == null) continue;
            if (col.transform == transform || col.transform.IsChildOf(transform)) continue;  // Don't stand on yourself
            if (hits[i].distance <= 0f) continue;              // Overlapping at start, normal invalid
            if (hits[i].normal.y < minGroundNormalY) continue; // Wall / steep slope, not a floor
            if (hits[i].distance < bestDistance)
            {
                bestDistance = hits[i].distance;
                best = col.transform;
            }
        }
        return best;
    }

    private void Attach(Transform t)
    {
        if (verboseLog && t != platform)
            Debug.Log($"[PlatformRider] {name} underfoot: {(platform ? platform.name : "(none)")} -> {(t ? t.name : "(none)")}", this);

        platform = t;
        standingOnReadout = t != null ? t.name : "(none)";
        if (t != null) CacheBasis();
    }

    private void CacheBasis()
    {
        lastPlatformPos = platform.position;
        lastPlatformRot = platform.rotation;
    }

    private void Release()
    {
        if (platform == null) return;
        platform = null;
        standingOnReadout = "(none)";
    }

    private void OnDrawGizmosSelected()
    {
        CharacterController cc = controller != null ? controller : GetComponent<CharacterController>();
        if (cc == null) return;

        float sideScale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));
        float upScale = Mathf.Abs(transform.lossyScale.y);
        float radius = Mathf.Max(0.01f, cc.radius * sideScale * 0.95f);
        float halfHeight = Mathf.Max(cc.height * upScale * 0.5f, radius);

        Vector3 center = transform.TransformPoint(cc.center);
        Vector3 origin = center - Vector3.up * (halfHeight - radius);

        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.6f);
        Gizmos.DrawWireSphere(origin, radius);
        Gizmos.DrawWireSphere(origin + Vector3.down * groundProbeDistance, radius);
        Gizmos.DrawLine(origin + Vector3.right * radius,
                        origin + Vector3.right * radius + Vector3.down * groundProbeDistance);
        Gizmos.DrawLine(origin - Vector3.right * radius,
                        origin - Vector3.right * radius + Vector3.down * groundProbeDistance);
    }
}
