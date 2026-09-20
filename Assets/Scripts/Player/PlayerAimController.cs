using UnityEngine;

/// <summary>
/// PLAYER (Aim)  Player third-person aim controller, same logic as RobotAimController:
///  - Right-click toggles "Aim / Move"; when aiming, the camera smoothly swings behind and zooms in, a crosshair appears at screen center,
///    and the character faces along the camera (crosshair direction).
///  - Only active while controlling the player; switching away from the player exits aim automatically.
///
/// Unlike the robot: the player doesn't fire missiles, but continuously records [the first object] under the crosshair (AimedTarget)
/// for later logic (currently detection + debug display only; nothing is done to the object).
///
/// Recommended to add to CharacterSwitcher's playerControlScripts.
/// Requirement: camera uses OrbitFollowCamera with holdRightMouseToRotate unchecked.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class PlayerAimController : MonoBehaviour
{
    [Header("References (empty = auto-fetch)")]
    public OrbitFollowCamera cam;

    [Header("Aim Ray")]
    [Tooltip("Crosshair hit-detection layers; can later be narrowed to only objects that need handling")]
    public LayerMask aimMask = ~0;
    public float maxAimDistance = 1000f;

    [Header("Discharge (aim at an electric entity and hold left mouse = discharge)")]
    [Tooltip("Discharge per second (design units/sec, same units as object energy)")]
    public float dischargeRate = 50f;

    [Header("Crosshair (optional; empty = built-in simple crosshair)")]
    public GameObject crosshair;

    [Header("Debug (runtime read-only)")]
    [SerializeField] private bool aiming;
    [Tooltip("Whether currently discharging an object (left mouse held and aimed at a dischargeable entity)")]
    [SerializeField] private bool discharging;
    [Tooltip("Name of the first object currently under the crosshair")]
    [SerializeField] private string aimedTargetName = "(none)";

    private PlayerController player;
    private Camera cameraComp;

    public bool IsAiming { get; private set; }

    // -- For later handling: the first object currently under the crosshair --
    public Transform AimedTarget { get; private set; }
    public bool HasAimHit { get; private set; }
    public RaycastHit LastHit { get; private set; }
    // Electric entity currently aimed at (for discharge); null if none
    public ElectricEntity AimedEntity { get; private set; }
    // Enemy energy currently aimed at (for draining); null if none
    public EnergySystem AimedEnemyEnergy { get; private set; }
    // Discharge lock target: keeps draining while left mouse is held even if the ray leaves it
    private ElectricEntity dischargeTarget;
    private EnergySystem dischargeEnemyEnergy;

    private void Awake()
    {
        player = GetComponent<PlayerController>();
        if (cam == null && Camera.main != null)
            cam = Camera.main.GetComponent<OrbitFollowCamera>();
        cameraComp = cam != null ? cam.GetComponent<Camera>() : Camera.main;
        if (crosshair != null) crosshair.SetActive(false);
    }

    private void Update()
    {
        // Aiming only allowed while "controlling the player"; when switched away (PlayerController disabled) force exit and ignore input
        if (player == null || !player.enabled)
        {
            if (IsAiming) SetAiming(false);
            return;
        }

        // Right-click toggles aim / move -- unless the player is carrying an Energy Relay (hands are full; press F to place it first)
        if (Input.GetMouseButtonDown(1) && EnergyRelay.CarriedRelay == null) SetAiming(!IsAiming);

        if (!IsAiming) { ClearTarget(); return; }

        // Refresh the first object under the crosshair each frame
        UpdateAimTarget();

        // Discharge: while left mouse is held, lock one dischargeable target and keep draining until Min Energy or release.
        // Locking isn't limited to the press frame -- sweeping the crosshair onto a target while holding also locks; once locked, even if the target drops below threshold
        // or its collision turns off (ray can't hit it), keep draining until full or released.
        if (Input.GetMouseButton(0))
        {
            if (dischargeTarget == null && AimedEntity != null && AimedEntity.CanDischarge)
                dischargeTarget = AimedEntity;

            // Enemy energy: keep draining once locked (likewise, sweep while holding to lock)
            if (dischargeEnemyEnergy == null && AimedEnemyEnergy != null)
                dischargeEnemyEnergy = AimedEnemyEnergy;

            if (dischargeTarget != null)
                dischargeTarget.Discharge(dischargeRate * Time.deltaTime);
            if (dischargeEnemyEnergy != null)
                dischargeEnemyEnergy.Drain(dischargeRate * Time.deltaTime);

            discharging = dischargeTarget != null || dischargeEnemyEnergy != null;
        }
        else
        {
            dischargeTarget = null;
            dischargeEnemyEnergy = null;
            discharging = false;
        }
    }

    private void LateUpdate()
    {
        // Only after the camera settles does the character face along the camera (avoids the character swinging during the aim-in turn)
        if (IsAiming && cam != null && cam.AimReady)
        {
            Vector3 f = cam.AimForward;
            if (f.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(f, Vector3.up);
        }
    }

    /// <summary>Leave aim mode from outside (e.g. when the player picks up an Energy Relay)</summary>
    public void ExitAim()
    {
        if (IsAiming) SetAiming(false);
    }

    private void SetAiming(bool on)
    {
        IsAiming = on;
        aiming = on;

        if (cam != null)
        {
            if (on) cam.BeginAim(transform.eulerAngles.y);   // smoothly swing behind, no snapping
            else cam.EndAim();
        }
        if (crosshair != null) crosshair.SetActive(on);

        if (!on)
        {
            ClearTarget();
            dischargeTarget = null;   // exiting aim releases the discharge lock
            dischargeEnemyEnergy = null;
        }
    }

    // Raycast from screen center to find "the first object aimed at"
    private void UpdateAimTarget()
    {
        if (cameraComp == null) { ClearTarget(); return; }

        Ray ray = cameraComp.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        // Hit Triggers too: electric entities that turn into Triggers (passable) below threshold can still be selected to keep draining
        if (Physics.Raycast(ray, out RaycastHit hit, maxAimDistance, aimMask, QueryTriggerInteraction.Collide))
        {
            HasAimHit = true;
            LastHit = hit;
            AimedTarget = hit.transform;
            AimedEntity = hit.transform.GetComponentInParent<ElectricEntity>();
            // Only treat "enemies" (with EnemyChaser) EnergySystem as drain targets, to avoid draining the robot itself
            var enemy = hit.transform.GetComponentInParent<EnemyChaser>();
            AimedEnemyEnergy = enemy != null ? enemy.GetComponent<EnergySystem>() : null;
            aimedTargetName = hit.transform.name;
        }
        else
        {
            ClearTarget();
        }
    }

    private void ClearTarget()
    {
        HasAimHit = false;
        AimedTarget = null;
        AimedEntity = null;
        AimedEnemyEnergy = null;
        aimedTargetName = "(none)";
        discharging = false;
    }

    private void OnDisable()
    {
        // Switched away from player → exit aim and reset camera (cursor is managed by CharacterSwitcher, not touched here)
        if (IsAiming)
        {
            IsAiming = false;
            aiming = false;
            if (cam != null) cam.EndAim();
            if (crosshair != null) crosshair.SetActive(false);
            ClearTarget();
        }
    }

    // Built-in simple crosshair (used when crosshair is not assigned)
    private void OnGUI()
    {
        if (!IsAiming || crosshair != null) return;

        float cx = Screen.width * 0.5f;
        float cy = Screen.height * 0.5f;
        const float len = 10f, thick = 2f;

        Color old = GUI.color;
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(cx - len, cy - thick * 0.5f, len * 2f, thick), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - thick * 0.5f, cy - len, thick, len * 2f), Texture2D.whiteTexture);
        GUI.color = old;
    }
}