using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Laser tower: spawns an [adjustable number] of laser prefabs at start and fires continuously (replaces Hovl_DemoLasers click-to-fire).
/// Two layouts supported:
///   - Fan: centered on the fire point, lasers spread evenly by angle.
///   - Parallel: lasers arranged in parallel, spread sideways by spacing.
/// Count, prefab, position / rotation / length / scale can be edited directly in the Inspector.
/// Each laser instance should carry Hovl_Laser (visuals) + LaserBarrier (knockback / energy drain).
///
/// On/off: SetFiring(bool) / Toggle(), controlled externally by LaserTowerSwitch etc.
/// </summary>
public class LaserTower : MonoBehaviour
{
    public enum Layout { Fan, Parallel }

    [Header("Laser Prefab")]
    [Tooltip("Laser prefab to fire (with Hovl_Laser + LaserBarrier)")]
    public GameObject laserPrefab;

    [Header("Count / Layout")]
    [Tooltip("Number of lasers")]
    [Min(1)] public int count = 1;
    public Layout layout = Layout.Fan;
    [Tooltip("Fan: angle between adjacent lasers (degrees)")]
    public float fanAngleStep = 15f;
    [Tooltip("Parallel: lateral spacing between adjacent lasers (meters)")]
    public float parallelSpacing = 1f;

    [Header("Fire Point / Direction")]
    [Tooltip("Fire origin; empty = use this object's own transform")]
    public Transform firePoint;
    [Tooltip("Extra position offset on top of the fire point (local)")]
    public Vector3 localOffset = Vector3.zero;
    [Tooltip("Extra rotation on top of the fire point (Euler angles)")]
    public Vector3 localEuler = Vector3.zero;

    [Header("Parameters")]
    public float maxLength = 40f;
    [Tooltip("Overall laser scale (adjusts prefab effect size)")]
    public Vector3 laserScale = Vector3.one;
    [Tooltip("Whether spawned lasers are children of this tower (follow tower movement/rotation)")]
    public bool parentToTower = true;

    [Header("On/Off")]
    [Tooltip("Whether to fire automatically at start. If a LaserTowerSwitch is attached, its startOn takes precedence")]
    public bool fireOnStart = true;

    [Header("Energy Relay auto-aim")]
    [Tooltip("When an Energy Relay's attraction box contains this tower, turn toward the relay and fire the beams at it")]
    public bool autoAimRelays = true;
    [Tooltip("Turn speed of the tower body toward the relay (degrees/second, yaw only)")]
    public float aimTurnSpeed = 120f;
    [Tooltip("When no relay attracts the tower any more, turn back to the original facing")]
    public bool returnToInitialWhenIdle = true;
    [Tooltip("How often (seconds) the tower looks for relays")]
    public float relayScanInterval = 0.25f;

    [Header("Runtime")]
    [SerializeField] private List<GameObject> instances = new List<GameObject>();
    [SerializeField] private EnergyRelay aimTarget;

    private readonly List<Quaternion> instanceLocalRot = new List<Quaternion>();
    private Quaternion initialRotation;
    private float nextRelayScan;

    /// The relay this tower is currently aiming at (null if none)
    public EnergyRelay AimTarget => aimTarget;

    /// Whether currently firing
    public bool IsFiring => instances.Count > 0;

    private void Awake()
    {
        initialRotation = transform.rotation;
    }

    private void Start()
    {
        if (fireOnStart) Fire();
    }

    private void Update()
    {
        if (!autoAimRelays) return;

        if (Time.time >= nextRelayScan)
        {
            nextRelayScan = Time.time + relayScanInterval;
            aimTarget = EnergyRelay.FindAttracting(transform.position);
        }

        // -- Tower body: yaw toward the relay (or back to the initial facing) --
        Quaternion wantBody;
        if (aimTarget != null)
        {
            Vector3 flat = aimTarget.AimPosition - transform.position; flat.y = 0f;
            wantBody = flat.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(flat.normalized, Vector3.up) : transform.rotation;
        }
        else if (returnToInitialWhenIdle) wantBody = initialRotation;
        else return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, wantBody, aimTurnSpeed * Time.deltaTime);

        // -- Beams: full 3D aim at the relay so they actually hit it; otherwise back to their layout rotation --
        for (int i = 0; i < instances.Count; i++)
        {
            var go = instances[i];
            if (go == null) continue;
            if (aimTarget != null)
            {
                Vector3 d = aimTarget.AimPosition - go.transform.position;
                if (d.sqrMagnitude > 0.0001f)
                    go.transform.rotation = Quaternion.RotateTowards(go.transform.rotation, Quaternion.LookRotation(d.normalized, Vector3.up), aimTurnSpeed * Time.deltaTime);
            }
            else if (i < instanceLocalRot.Count)
            {
                Quaternion want = parentToTower ? transform.rotation * instanceLocalRot[i] : instanceLocalRot[i];
                go.transform.rotation = Quaternion.RotateTowards(go.transform.rotation, want, aimTurnSpeed * Time.deltaTime);
            }
        }
    }

    /// <summary>Turn lasers on / off. Repeated calls with the same state do nothing, avoiding rebuilding instances on spam clicks.</summary>
    public void SetFiring(bool on)
    {
        if (on == IsFiring) return;
        if (on) Fire();
        else StopFire();
    }

    /// <summary>Invert the current state</summary>
    public void Toggle() => SetFiring(!IsFiring);

    /// Spawn all lasers with the current configuration
    public void Fire()
    {
        StopFire();
        if (laserPrefab == null) { Debug.LogWarning("[LaserTower] laserPrefab not assigned", this); return; }

        Transform origin = firePoint != null ? firePoint : transform;
        Vector3 basePos = origin.TransformPoint(localOffset);
        Quaternion baseRot = origin.rotation * Quaternion.Euler(localEuler);
        Vector3 right = baseRot * Vector3.right;

        int n = Mathf.Max(1, count);
        float mid = (n - 1) * 0.5f;   // Center alignment

        for (int i = 0; i < n; i++)
        {
            float k = i - mid;   // -mid .. +mid, centered
            Vector3 pos = basePos;
            Quaternion rot = baseRot;

            if (layout == Layout.Fan)
                rot = baseRot * Quaternion.Euler(0f, k * fanAngleStep, 0f);   // Spread around Y
            else // Parallel
                pos = basePos + right * (k * parallelSpacing);               // Lateral offset

            var go = Instantiate(laserPrefab, pos, rot, parentToTower ? origin : null);
            go.transform.localScale = laserScale;
            go.SetActive(true);
            ApplyMaxLength(go, maxLength);
            instances.Add(go);
            // remember the layout rotation (relative to the tower when parented) so the beam can return to it after relay aiming
            instanceLocalRot.Add(parentToTower ? Quaternion.Inverse(transform.rotation) * rot : rot);
        }
    }

    /// Clear all lasers
    public void StopFire()
    {
        for (int i = 0; i < instances.Count; i++)
            if (instances[i] != null) Destroy(instances[i]);
        instances.Clear();
        instanceLocalRot.Clear();
    }

    /// Call after changing the count at runtime to rebuild
    public void SetCount(int newCount) { count = Mathf.Max(1, newCount); Fire(); }

    private void ApplyMaxLength(GameObject go, float len)
    {
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            var t = mb.GetType();
            var f = t.GetField("MaxLength") ?? t.GetField("maxLength");
            if (f != null && f.FieldType == typeof(float)) f.SetValue(mb, len);
        }
    }

    private void OnDrawGizmos()
    {
        Transform origin = firePoint != null ? firePoint : transform;
        Vector3 basePos = origin.TransformPoint(localOffset);
        Quaternion baseRot = origin.rotation * Quaternion.Euler(localEuler);
        Vector3 right = baseRot * Vector3.right;

        int n = Mathf.Max(1, count);
        float mid = (n - 1) * 0.5f;

        Gizmos.color = Color.red;
        for (int i = 0; i < n; i++)
        {
            float k = i - mid;
            Vector3 pos = basePos;
            Vector3 fwd;
            if (layout == Layout.Fan)
            {
                pos = basePos;
                fwd = (baseRot * Quaternion.Euler(0f, k * fanAngleStep, 0f)) * Vector3.forward;
            }
            else
            {
                pos = basePos + right * (k * parallelSpacing);
                fwd = baseRot * Vector3.forward;
            }
            Gizmos.DrawLine(pos, pos + fwd * maxLength);
        }
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(basePos, 0.15f);
    }
}