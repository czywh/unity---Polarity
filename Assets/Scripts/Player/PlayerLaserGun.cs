using UnityEngine;

/// <summary>
/// PLAYER (Laser gun)  Hand-held laser for the player, layered on top of PlayerAimController:
///   - Right-click enters aim mode (handled by PlayerAimController) -> the weapon model (a LaserTower_01 mesh parented to the
///     player's hand) is shown; leaving aim mode hides it again.
///   - While aiming, hold left mouse -> a laser beam is fired from the muzzle, using the SAME laser prefab the laser towers use
///     (Hovl laser visuals + LaserBarrier gameplay), so hits behave exactly like tower lasers: enemies are drained / slowed,
///     pylons are drained, bridges / walls cut the beam. Release left mouse -> the beam is removed.
///   - The beam is aimed at the crosshair: every frame it is rotated from the muzzle toward the point under the screen centre,
///     so what you see in the crosshair is what the beam hits, regardless of where the hand is.
///   - The player is registered as the beam's owner (LaserBarrier.owner) so the beam never knocks the player back.
///   - Optional energy cost per second from the player's EnergySystem; firing stops when the energy runs out.
///
/// Setup (see the Inspector tooltips):
///   1. Duplicate the LaserTower_01 model, parent it under the player's hand / an anchor child of the Player, scale it down,
///      disable it, and drag it into Weapon Model.
///   2. Add an empty child at the model's barrel tip, +Z pointing out of the barrel, drag it into Muzzle.
///   3. Drag the tower's laser prefab ("Laser beam demo interactions") into Laser Prefab.
/// Attach this to the Player root (same object as PlayerController / PlayerAimController).
/// </summary>
[RequireComponent(typeof(PlayerAimController))]
public class PlayerLaserGun : MonoBehaviour
{
    public enum FireMode
    {
        [InspectorName("Hold (beam stays on while left mouse is held)")] Hold,
        [InspectorName("Burst (one timed beam per click)")] Burst,
    }

    [Header("Weapon model")]
    [Tooltip("The hand-held laser model (e.g. a scaled-down LaserTower_01) parented under the player. Shown only while aiming. Leave disabled in the scene")]
    public GameObject weaponModel;
    [Tooltip("Where the beam starts. An empty child at the barrel tip with +Z pointing out of the barrel. Empty = weaponModel's transform")]
    public Transform muzzle;

    [Header("Laser")]
    [Tooltip("Laser prefab -- use the SAME one the laser towers use (Hovl laser + LaserBarrier), so hits behave identically")]
    public GameObject laserPrefab;
    [Tooltip("Max beam length (written into the prefab's MaxLength / maxLength fields, like LaserTower does)")]
    public float maxLength = 40f;
    [Tooltip("Scale applied to the spawned laser instance (tune the effect size for a hand-held gun)")]
    public Vector3 laserScale = Vector3.one;
    public FireMode fireMode = FireMode.Hold;
    [Tooltip("Burst mode only: how long one beam stays on (seconds)")]
    public float burstDuration = 0.4f;
    [Tooltip("Burst mode only: minimum time between two bursts (seconds)")]
    public float burstCooldown = 0.2f;

    [Header("Aiming")]
    [Tooltip("Layers the crosshair ray can hit when deciding where the beam points")]
    public LayerMask aimMask = ~0;
    [Tooltip("If the crosshair ray hits nothing, the beam points at this distance along the camera ray")]
    public float maxAimDistance = 200f;
    [Tooltip("Rotate the beam toward the crosshair every frame (recommended). Off = beam simply follows the muzzle's own +Z")]
    public bool aimAtCrosshair = true;

    [Header("Energy (optional)")]
    [Tooltip("Energy drained per second from the player's EnergySystem while firing. 0 = free")]
    public float energyCostPerSecond = 0f;

    [Header("Debug (runtime read-only)")]
    [SerializeField] private bool firing;
    [SerializeField] private string aimPointReadout = "(none)";
    public bool verboseLog = false;

    public bool IsFiring => beam != null;

    private PlayerAimController aim;
    private EnergySystem energy;
    private Camera cam;
    private GameObject beam;
    private LaserBarrier beamBarrier;
    private float burstEndTime;
    private float nextBurstTime;

    private void Awake()
    {
        aim = GetComponent<PlayerAimController>();
        energy = GetComponent<EnergySystem>();
        cam = Camera.main;
        if (muzzle == null && weaponModel != null) muzzle = weaponModel.transform;
        if (weaponModel != null) weaponModel.SetActive(false);
    }

    private void Update()
    {
        bool aiming = aim != null && aim.enabled && aim.IsAiming;

        // Weapon model visibility follows aim mode
        if (weaponModel != null && weaponModel.activeSelf != aiming) weaponModel.SetActive(aiming);

        if (!aiming)
        {
            if (IsFiring) StopFire();
            return;
        }

        // -- Fire input --
        if (fireMode == FireMode.Hold)
        {
            bool want = Input.GetMouseButton(0);
            if (want && !IsFiring) StartFire();
            else if (!want && IsFiring) StopFire();
        }
        else
        {
            if (Input.GetMouseButtonDown(0) && !IsFiring && Time.time >= nextBurstTime)
            {
                StartFire();
                burstEndTime = Time.time + burstDuration;
            }
            if (IsFiring && Time.time >= burstEndTime)
            {
                StopFire();
                nextBurstTime = Time.time + burstCooldown;
            }
        }

        // -- Energy --
        if (IsFiring && energyCostPerSecond > 0f && energy != null)
        {
            if (!energy.HasEnergy) { StopFire(); return; }
            energy.Drain(energyCostPerSecond * Time.deltaTime);
        }
    }

    // After PlayerAimController.LateUpdate has turned the player toward the camera, point the beam at the crosshair
    private void LateUpdate()
    {
        if (!IsFiring) return;

        if (aimAtCrosshair && cam != null)
        {
            Vector3 target = GetAimPoint();
            Vector3 dir = target - beam.transform.position;
            if (dir.sqrMagnitude > 0.0001f)
                beam.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }
        else
        {
            beam.transform.rotation = muzzle != null ? muzzle.rotation : transform.rotation;
        }
    }

    private void StartFire()
    {
        if (laserPrefab == null) { Debug.LogWarning("[PlayerLaserGun] laserPrefab not assigned", this); return; }
        if (energyCostPerSecond > 0f && energy != null && !energy.HasEnergy) return;

        Transform origin = muzzle != null ? muzzle : transform;
        // Child of the muzzle: position follows the hand automatically; rotation is overridden in LateUpdate
        beam = Instantiate(laserPrefab, origin.position, origin.rotation, origin);
        beam.transform.localScale = laserScale;
        beam.SetActive(true);
        ApplyMaxLength(beam, maxLength);

        // Never knock the shooter back with their own beam
        beamBarrier = beam.GetComponentInChildren<LaserBarrier>(true);
        if (beamBarrier != null) beamBarrier.owner = transform;

        firing = true;
        Log("fire");
    }

    private void StopFire()
    {
        if (beam != null) Destroy(beam);
        beam = null;
        beamBarrier = null;
        firing = false;
        aimPointReadout = "(none)";
        Log("stop");
    }

    // Point under the screen centre, ignoring the player's own colliders
    private Vector3 GetAimPoint()
    {
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        RaycastHit[] hits = Physics.RaycastAll(ray, maxAimDistance, aimMask, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        Vector3 point = ray.origin + ray.direction * maxAimDistance;
        string name = "(none)";
        for (int i = 0; i < hits.Length; i++)
        {
            var h = hits[i];
            if (h.transform.IsChildOf(transform)) continue;          // own body / weapon
            if (beam != null && h.transform.IsChildOf(beam.transform)) continue;   // own beam effects
            if (h.distance < best) { best = h.distance; point = h.point; name = h.transform.name; }
        }
        aimPointReadout = name;
        return point;
    }

    // Same trick as LaserTower: write MaxLength into every component that has such a field (Hovl_Laser / Hovl_LaserDemo / LaserBarrier)
    private static void ApplyMaxLength(GameObject go, float len)
    {
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            var t = mb.GetType();
            var f = t.GetField("MaxLength") ?? t.GetField("maxLength");
            if (f != null && f.FieldType == typeof(float)) f.SetValue(mb, len);
        }
    }

    private void OnDisable()
    {
        // Switched away from the player / component disabled -> holster
        if (IsFiring) StopFire();
        if (weaponModel != null) weaponModel.SetActive(false);
    }

    private void Log(string msg)
    {
        if (verboseLog) Debug.Log($"[PlayerLaserGun] {msg}", this);
    }

    private void OnDrawGizmosSelected()
    {
        Transform origin = muzzle != null ? muzzle : (weaponModel != null ? weaponModel.transform : transform);
        Gizmos.color = Color.red;
        Gizmos.DrawLine(origin.position, origin.position + origin.forward * 3f);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(origin.position, 0.08f);
    }
}
