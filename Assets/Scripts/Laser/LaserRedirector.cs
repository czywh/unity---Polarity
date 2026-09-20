using UnityEngine;

/// <summary>
/// Laser redirector: when hit by a laser, fires a new laser with [adjustable angle / adjustable length] from this device.
/// The output laser is [not a child of this device]; its direction is computed from the "initial facing" or the "incoming direction",
/// so even if this device spins (SpinY decoration), the output laser direction is unaffected.
///
/// Debug: same as LaserTower, OnDrawGizmos always draws the output ray's direction and length (red line + yellow muzzle sphere),
/// so you can tune redirectAngle without selecting it or entering Play mode.
/// </summary>
public class LaserRedirector : MonoBehaviour
{
    [Header("Output laser prefab (with Hovl_Laser + LaserBarrier)")]
    public GameObject laserPrefab;

    [Header("Redirect Angle")]
    [Tooltip("Deflection angle relative to the incoming direction (degrees)")]
    public float redirectAngle = 90f;
    [Tooltip("Axis to deflect around (usually world Up for horizontal redirection)")]
    public Vector3 rotateAxis = Vector3.up;
    [Tooltip("Checked: ignore the incoming direction and use the [initial forward] as a fixed output direction (unaffected by spinning)")]
    public bool useFixedDirection = false;

    [Header("Output Position / Parameters")]
    [Tooltip("Output origin offset relative to this device (uses initial facing, unaffected by spinning)")]
    public Vector3 muzzleLocalOffset = Vector3.zero;
    [Tooltip("Output laser length (editable at runtime)")]
    public float maxLength = 40f;
    public Vector3 laserScale = Vector3.one;

    [Header("Sustain")]
    [Tooltip("After being hit, turn off the output if not hit again within this time (seconds)")]
    public float sustainTime = 0.2f;

    [Header("Debug")]
    [Tooltip("Always draw the output ray's direction and length in the Scene view")]
    public bool drawGizmo = true;

    [Header("Runtime (read-only)")]
    [SerializeField] private bool active;
    [SerializeField] private Vector3 lastIncomingDir;

    private GameObject outputInstance;
    private Hovl_Laser outHovl;
    private LaserBarrier outBarrier;
    private float offUntil;

    // Initial facing / position (not changed by spinning)
    private Quaternion initialRotation;
    private Vector3 initialForward;
    private bool initialized;

    private void Awake()
    {
        initialRotation = transform.rotation;
        initialForward = transform.forward;
        lastIncomingDir = initialForward;
        initialized = true;
    }

    /// Called by LaserBarrier when it hits this device, passing the incoming laser direction (world)
    public void Hit(Vector3 incomingDir)
    {
        lastIncomingDir = incomingDir.sqrMagnitude > 0.0001f ? incomingDir.normalized : InitialForward;
        offUntil = Time.time + sustainTime;
        if (!active) { active = true; SpawnOutput(); }
        UpdateOutput();
    }

    private void Update()
    {
        if (active && Time.time >= offUntil) { active = false; DespawnOutput(); }
        else if (active) UpdateOutput();
    }

    // -- Direction / position (Awake hasn't run in edit mode, so fall back to the current transform) --

    private Vector3 InitialForward => initialized ? initialForward : transform.forward;
    private Quaternion InitialRotation => initialized ? initialRotation : transform.rotation;

    /// Incoming direction: the recorded value at runtime, this object's forward in edit mode
    private Vector3 IncomingDirection()
    {
        if (initialized && lastIncomingDir.sqrMagnitude > 0.0001f) return lastIncomingDir.normalized;
        return transform.forward;
    }

    /// Output direction (Gizmo and runtime share the same calculation)
    public Vector3 OutputDirection()
    {
        if (useFixedDirection) return InitialForward;   // Fixed direction, unaffected by spinning
        Vector3 axis = rotateAxis.sqrMagnitude > 0.0001f ? rotateAxis.normalized : Vector3.up;
        return (Quaternion.AngleAxis(redirectAngle, axis) * IncomingDirection()).normalized;
    }

    /// Output origin (offset computed from initial facing, so the muzzle doesn't wander while spinning)
    public Vector3 MuzzlePosition() => transform.position + InitialRotation * muzzleLocalOffset;

    // Use a different up when the direction is parallel to world Up, to avoid LookRotation warnings
    private static Quaternion SafeLook(Vector3 dir)
    {
        Vector3 up = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.999f ? Vector3.forward : Vector3.up;
        return Quaternion.LookRotation(dir, up);
    }

    // -- Output laser instance --

    private void SpawnOutput()
    {
        if (laserPrefab == null) { Debug.LogWarning("[LaserRedirector] laserPrefab not assigned", this); active = false; return; }
        // No parent → doesn't inherit this device's spin / scale
        outputInstance = Instantiate(laserPrefab, MuzzlePosition(), SafeLook(OutputDirection()), null);
        outputInstance.transform.localScale = laserScale;
        outputInstance.SetActive(true);
        outHovl = outputInstance.GetComponentInChildren<Hovl_Laser>();
        outBarrier = outputInstance.GetComponentInChildren<LaserBarrier>();
        UpdateOutput();
    }

    private void UpdateOutput()
    {
        if (outputInstance == null) return;
        outputInstance.transform.position = MuzzlePosition();
        outputInstance.transform.rotation = SafeLook(OutputDirection());
        outputInstance.transform.localScale = laserScale;
        // Sync length every frame → changing maxLength at runtime takes effect immediately
        if (outHovl != null) outHovl.MaxLength = maxLength;
        if (outBarrier != null) outBarrier.maxLength = maxLength;
    }

    private void DespawnOutput()
    {
        if (outputInstance != null) Destroy(outputInstance);
        outputInstance = null; outHovl = null; outBarrier = null;
    }

    private void OnDestroy() => DespawnOutput();

    // -- Gizmo: same drawing as LaserTower --

    private void OnDrawGizmos()
    {
        if (!drawGizmo) return;

        Vector3 pos = MuzzlePosition();
        Vector3 fwd = OutputDirection();

        Gizmos.color = Color.red;
        Gizmos.DrawLine(pos, pos + fwd * maxLength);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(pos, 0.15f);
    }
}