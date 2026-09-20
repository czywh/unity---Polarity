using UnityEngine;

/// <summary>
/// ROBOT Operation Mode (god view) robot controller.
///   - Move: WASD on fixed world axes -- W→X+ S→X- A→Z+ D→Z-; speed matches RobotController, Shift to run.
///   - Jump: Input Manager "Jump" (Space by default); jump height / whether jumping is allowed come from RobotController,
///          with the same coyote time and jump buffer, so it feels identical to non-Operation Mode.
///   - Field: press E to toggle the electric field (IFieldEmitter).
///   - Interact: near any robot-usable interactable (distance only, no aiming) press F -- bypasses the frozen Interactor:
///       charging docks use ChargeDirect for continuous charging; others (e.g. the LevelGoal) use OnInteract as a one-shot trigger.
///   - Death: while the robot is dying, input is frozen (no moving / no field / no charging); only gravity remains.
///
/// Interaction prompt: in Operation Mode the Interactor is frozen and cannot provide focus. This component exposes
/// the per-frame "currently nearby interactable" via NearestInteractable, and InteractionPrompt falls back to it
/// when no Interactor holds control -- the condition is identical to pressing F,
/// so prompt shown = pressing F right now will definitely work.
///
/// Disabled normally; OperationModeController enables it on entering Operation Mode and disables it on exit.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class RobotConsoleMover : MonoBehaviour
{
    [Header("Movement (speeds default to the RobotController on the same object, to stay consistent)")]
    public KeyCode runKey = KeyCode.LeftShift;
    [Tooltip("Fallback walk speed when no RobotController is found")]
    public float fallbackWalkSpeed = 4f;
    [Tooltip("Fallback run speed when no RobotController is found")]
    public float fallbackRunSpeed = 6f;

    [Header("Jump (parameters from the RobotController on the same object, to stay consistent)")]
    [Tooltip("Whether jumping is allowed in Operation Mode. Combined with RobotController.canJump")]
    public bool allowJump = true;
    [Tooltip("Fallback jump height when no RobotController is found")]
    public float fallbackJumpHeight = 1.2f;
    [Tooltip("Grace period after leaving the ground during which jumping is still allowed (coyote time)")]
    public float coyoteTime = 0.1f;
    [Tooltip("Buffer time for pressing jump just before landing")]
    public float jumpBufferTime = 0.1f;

    [Header("Field")]
    [Tooltip("Key to toggle the field; leave None to follow RobotController's fieldKey")]
    public KeyCode fieldKeyOverride = KeyCode.None;

    [Header("Interaction (press this key in Operation Mode: charging dock = charge, others = trigger)")]
    public KeyCode chargeKey = KeyCode.F;
    [Tooltip("How close counts as \"next to an interactable\" (distance only, no aiming in god view).\nThe interaction prompt uses this same range, so they always agree")]
    public float chargeRange = 3f;

    [Header("Face Mouse")]
    [Tooltip("In Operation Mode the robot always faces the mouse (Y axis only)")]
    public bool faceMouse = true;
    [Tooltip("Turn speed (deg/s); 0 or negative = snap instantly")]
    public float turnSpeed = 720f;
    [Tooltip("Camera used to find the mouse point; leave empty to use Camera.main")]
    public Camera aimCamera;

    [Header("Gravity (keep grounded)")]
    public float gravity = -25f;

    [Header("Debug (read-only at runtime)")]
    [Tooltip("Currently nearby interactable (= the one the prompt UI shows)")]
    [SerializeField] private string nearestDockReadout = "(none)";
    [SerializeField] private bool chargingReadout;

    private CharacterController controller;
    private RobotController robotController;
    private IFieldEmitter fieldEmitter;
    private CharacterDeathHandler deathHandler;
    private EnergySystem energySystem;
    private float verticalVelocity;
    private float lastGroundedTime = -99f;
    private float lastJumpPressedTime = -99f;
    private Interactor robotInteractor;         // This robot's own Interactor (frozen; only borrowed as identity for OnInteract)
    private ChargingDock chargingFrom;          // Charging dock currently charging from
    private InteractableBase nearestInteractable;   // Nearest interactable computed this frame (read by the prompt UI)
    private InteractableBase focused;               // Non-dock interactable we've called OnFocusEnter on
    private ChargingDock nearestDock => nearestInteractable as ChargingDock;

    /// <summary>
    /// Object nearby that can be interacted with by pressing F (null if none). Charging dock / goal / any robot-usable InteractableBase.
    /// Always null while this component is disabled or the robot is dying, so no prompt lingers.
    /// </summary>
    public InteractableBase NearestInteractable => isActiveAndEnabled ? nearestInteractable : null;

    /// <summary>Legacy compatibility: currently nearby charging dock (null if not a dock)</summary>
    public ChargingDock NearestDock => isActiveAndEnabled ? nearestDock : null;

    /// <summary>Whether currently charging</summary>
    public bool IsCharging => chargingFrom != null;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        robotController = GetComponent<RobotController>();
        fieldEmitter = GetComponent<IFieldEmitter>();
        deathHandler = GetComponent<CharacterDeathHandler>();
        energySystem = GetComponent<EnergySystem>();
        robotInteractor = GetComponent<Interactor>();
    }

    private void OnEnable()
    {
        verticalVelocity = 0f;
        lastGroundedTime = -99f;
        lastJumpPressedTime = -99f;
    }

    private void OnDisable()
    {
        verticalVelocity = 0f;
        chargingFrom = null;
        ClearFocus();
        nearestInteractable = null;      // Clear on exiting Operation Mode so the prompt doesn't get stuck
        nearestDockReadout = "(none)";
        chargingReadout = false;
    }

    private void Update()
    {
        // Dying: freeze input, and [do NOT apply gravity ourselves].
        // Death / teleport is owned exclusively by CharacterDeathHandler (same convention as PassiveFall);
        // if we kept accumulating verticalVelocity here, after the respawn teleport we'd land at tens of m/s,
        // possibly tunneling through the respawn platform in one frame and falling again -- looking like "after dying once it can never die again".
        if (deathHandler != null && deathHandler.IsDying)
        {
            chargingFrom = null;
            ClearFocus();
            nearestInteractable = null;  // No prompt while dying
            nearestDockReadout = "(dying)";
            chargingReadout = false;
            verticalVelocity = 0f;
            lastJumpPressedTime = -99f;
            return;
        }

        // -- Movement: speed matches RobotController, Shift to run --
        float walk = robotController != null ? robotController.walkSpeed : fallbackWalkSpeed;
        float run  = robotController != null ? robotController.runSpeed  : fallbackRunSpeed;
        float speed = Input.GetKey(runKey) ? run : walk;

        float v = Input.GetAxisRaw("Vertical");    // W=+1, S=-1
        float h = Input.GetAxisRaw("Horizontal");  // D=+1, A=-1
        Vector3 dir = new Vector3(v, 0f, -h);      // W/S → X, A/D → Z (A is +Z, hence -h)
        if (dir.sqrMagnitude > 1f) dir.Normalize();

        // -- Ground check: same as CharacterMotorBase (sphere check if groundCheck exists, else fall back to CC) --
        bool grounded = CheckGrounded();
        if (grounded) lastGroundedTime = Time.time;

        // -- Jump input: uses Input Manager "Jump", same key as non-Operation Mode --
        if (CanJumpNow && Input.GetButtonDown("Jump")) lastJumpPressedTime = Time.time;

        // -- Gravity + jump (coyote time / jump buffer, feel matches CharacterMotorBase) --
        if (grounded && verticalVelocity < 0f) verticalVelocity = -2f;

        bool coyoteOk = Time.time - lastGroundedTime <= coyoteTime;
        bool jumpBuffered = Time.time - lastJumpPressedTime <= jumpBufferTime;
        if (coyoteOk && jumpBuffered)
        {
            float jumpHeight = robotController != null ? robotController.jumpHeight : fallbackJumpHeight;
            verticalVelocity = Mathf.Sqrt(-2f * gravity * jumpHeight);
            lastJumpPressedTime = -99f;
            lastGroundedTime = -99f;
        }

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = dir * speed + Vector3.up * verticalVelocity;
        controller.Move(velocity * Time.deltaTime);

        // -- Toggle field: E --
        KeyCode fk = fieldKeyOverride != KeyCode.None
            ? fieldKeyOverride
            : (robotController != null ? robotController.fieldKey : KeyCode.E);
        if (Input.GetKeyDown(fk)) fieldEmitter?.ToggleField();

        // -- Face mouse (Y axis only) --
        if (faceMouse) FaceMouse();

        // -- Charging dock: press F to charge when one is nearby (distance check only) --
        HandleCharging();
    }

    /// Whether jumping is allowed: this component's toggle + RobotController.canJump
    private bool CanJumpNow => allowJump && (robotController == null || robotController.canJump);

    /// Ground check. Uses RobotController's groundCheck if set, to match non-Operation Mode
    private bool CheckGrounded()
    {
        if (robotController != null && robotController.groundCheck != null)
            return Physics.CheckSphere(
                robotController.groundCheck.position,
                robotController.groundCheckRadius,
                robotController.groundMask,
                QueryTriggerInteraction.Ignore);
        return controller.isGrounded;
    }

    // Find the mouse point on the robot's horizontal plane and face it (Y only)
    private void FaceMouse()
    {
        Camera c = aimCamera != null ? aimCamera : Camera.main;
        if (c == null) return;

        Ray ray = c.ScreenPointToRay(Input.mousePosition);

        // Intersect with the horizontal plane through the robot (normal up)
        Plane plane = new Plane(Vector3.up, transform.position);
        if (!plane.Raycast(ray, out float enter)) return;

        Vector3 hit = ray.GetPoint(enter);
        Vector3 look = hit - transform.position;
        look.y = 0f;
        if (look.sqrMagnitude < 0.0001f) return;   // Mouse is almost on top of us, skip

        Quaternion target = Quaternion.LookRotation(look.normalized, Vector3.up);
        transform.rotation = turnSpeed > 0f
            ? Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime)
            : target;
    }

    private void HandleCharging()
    {
        // Computed once per frame, used by both "press F" and "interaction prompt" -- single source of truth, they can't desync
        nearestInteractable = FindNearestInteractable();
        var dock = nearestDock;

        // -- Non-dock interactables (goal etc.): maintain focus so IsFocused / subclass logic matches normal mode --
        var other = dock == null ? nearestInteractable : null;
        if (other != focused)
        {
            ClearFocus();
            if (other != null && robotInteractor != null)
            {
                other.OnFocusEnter(robotInteractor);
                focused = other;
            }
        }

        if (Input.GetKeyDown(chargeKey))
        {
            if (dock != null)
                chargingFrom = dock;                       // Charging dock: start continuous charging
            else if (focused != null && robotInteractor != null)
                focused.OnInteract(robotInteractor);      // Others: one-shot trigger (LevelGoal → victory)
        }

        if (chargingFrom != null)
        {
            // Walked away / no battery / fully charged → stop
            if (dock != chargingFrom || energySystem == null) chargingFrom = null;
            else if (!chargingFrom.ChargeDirect(energySystem)) chargingFrom = null;
        }

        nearestDockReadout = nearestInteractable != null ? nearestInteractable.name : "(none)";
        chargingReadout = chargingFrom != null;
    }

    private void ClearFocus()
    {
        if (focused != null && robotInteractor != null) focused.OnFocusExit(robotInteractor);
        focused = null;
    }

    // Find the nearest robot-usable interactable by distance only (no camera aiming needed)
    private InteractableBase FindNearestInteractable()
    {
        var list = InteractableBase.All;
        InteractableBase best = null;
        float bestD = chargeRange;
        for (int i = 0; i < list.Count; i++)
        {
            var it = list[i];
            if (it == null || !it.CanBeUsedBy(InteractorType.Robot)) continue;
            float d = Vector3.Distance(transform.position, it.InteractTransform.position);
            if (d <= bestD) { bestD = d; best = it; }
        }
        return best;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, chargeRange);
    }
}