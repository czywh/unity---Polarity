using UnityEngine;

/// <summary>
/// Character movement core base class: the shared "how to move" for player and robot.
///
/// Includes: camera-relative movement + smooth turning toward move direction + gravity + coyote time/jump buffer + unified ground check,
/// plus a set of public read-only state (for animation / visuals to read).
///
/// Subclasses only need to: (1) declare speed/jump params under their own Headers and feed them in via abstract properties;
///          (2) override HandleExtraInput() when extra input is needed (e.g. robot ability keys).
/// Control handoff: CharacterSwitcher enables / disables the subclass components.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public abstract class CharacterMotorBase : MonoBehaviour
{
    [Header("Turning")]
    public float turnSmoothTime = 0.1f;

    [Header("Gravity / Jump Feel")]
    public float gravity = -25f;
    [Tooltip("Grace time after leaving the ground during which jumping is still allowed (coyote time)")]
    public float coyoteTime = 0.1f;
    [Tooltip("Buffer time for pressing jump just before landing")]
    public float jumpBufferTime = 0.1f;

    [Header("Ground Check")]
    [Tooltip("Empty object at the feet; falls back to CharacterController.isGrounded if empty")]
    public Transform groundCheck;
    public float groundCheckRadius = 0.2f;
    public LayerMask groundMask = ~0;

    [Header("Camera")]
    [Tooltip("Camera that determines move direction; uses Camera.main if empty")]
    public Transform cameraTransform;

    [Header("Runtime Lock")]
    [Tooltip("Set true during interactions / cutscenes: freezes horizontal movement but still applies gravity")]
    public bool motionLocked = false;

    [Header("Phantom State (gravity-free hover inside gravity-affecting electric fields)")]
    [Tooltip("Rise / descend speed while hovering")]
    public float phantomVerticalSpeed = 4f;
    [Tooltip("Rise key")]
    public KeyCode phantomUpKey = KeyCode.Space;
    [Tooltip("Descend key")]
    public KeyCode phantomDownKey = KeyCode.LeftControl;

    // Subclass decides whether it has the phantom state ability (player true, robot false)
    protected virtual bool CanPhantom => false;
    // Whether currently in phantom state (inside a gravity-affecting field)
    public bool InPhantom { get; private set; }

    // -- Params provided by subclasses (base only reads them, holds no concrete fields) --
    protected abstract float WalkSpeed { get; }
    protected abstract float RunSpeed { get; }
    protected abstract float JumpHeight { get; }
    protected abstract bool CanRunNow { get; }   // Whether running is allowed (player always true, robot depends on canRun)
    protected abstract bool CanJumpNow { get; }  // Whether jumping is allowed

    // -- Public read-only state --
    public bool IsGrounded { get; private set; }
    public bool IsRunning { get; private set; }
    public bool IsMoving { get; private set; }
    public float PlanarSpeed { get; private set; }
    public float MaxSpeed => RunSpeed;
    public float VerticalVelocity => verticalVelocity;

    /// Fired on the frame a jump succeeds (subscribe to play jump animation, sound, etc.)
    public event System.Action Jumped;

    protected CharacterController controller;
    private float turnSmoothVelocity;
    private float verticalVelocity;
    private float lastGroundedTime = -99f;
    private float lastJumpPressedTime = -99f;

    protected virtual void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    protected virtual void Start()
    {
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    protected virtual void Update()
    {
        UpdateGrounded();

        // Phantom state check: has the ability AND is inside a "gravity-affecting field"
        InPhantom = CanPhantom
            && ElectricFieldManager.Instance != null
            && ElectricFieldManager.Instance.IsInsidePhantomField(transform.position);

        // -- Centralized input reading: hooking up an InputManager later only changes this block --
        float inputX = Input.GetAxisRaw("Horizontal");
        float inputZ = Input.GetAxisRaw("Vertical");
        bool runHeld = CanRunNow && Input.GetKey(KeyCode.LeftShift);
        if (CanJumpNow && Input.GetButtonDown("Jump")) lastJumpPressedTime = Time.time;

        Vector3 planar;
        if (motionLocked)
        {
            planar = Vector3.zero;
            IsRunning = false;
        }
        else
        {
            planar = MoveHorizontal(inputX, inputZ, runHeld);
        }

        ApplyGravityAndJump();

        Vector3 velocity = planar + Vector3.up * verticalVelocity;
        controller.Move(velocity * Time.deltaTime);

        PlanarSpeed = new Vector3(planar.x, 0f, planar.z).magnitude;
        IsMoving = PlanarSpeed > 0.05f;

        // Subclass-specific input (robot ability keys etc.). The base doesn't care what it is.
        HandleExtraInput();
    }

    /// Camera-relative direction + smooth turn toward move direction; returns this frame's horizontal velocity
    private Vector3 MoveHorizontal(float inputX, float inputZ, bool runHeld)
    {
        Vector3 input = new Vector3(inputX, 0f, inputZ);
        if (input.sqrMagnitude < 0.01f)
        {
            IsRunning = false;
            return Vector3.zero;
        }
        input.Normalize();

        float camYaw = cameraTransform != null ? cameraTransform.eulerAngles.y : 0f;
        float targetAngle = Mathf.Atan2(input.x, input.z) * Mathf.Rad2Deg + camYaw;

        float angle = Mathf.SmoothDampAngle(
            transform.eulerAngles.y, targetAngle, ref turnSmoothVelocity, turnSmoothTime);
        transform.rotation = Quaternion.Euler(0f, angle, 0f);

        IsRunning = runHeld;
        float speed = runHeld ? RunSpeed : WalkSpeed;

        Vector3 moveDir = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
        return moveDir * speed;
    }

    private void ApplyGravityAndJump()
    {
        // Phantom state: no gravity, Space to rise / Ctrl to descend, speed controlled directly
        if (InPhantom)
        {
            float v = 0f;
            if (Input.GetKey(phantomUpKey)) v += phantomVerticalSpeed;
            if (Input.GetKey(phantomDownKey)) v -= phantomVerticalSpeed;
            verticalVelocity = v;
            lastJumpPressedTime = -99f;   // Clear jump buffer to avoid an accidental jump when leaving phantom state
            return;
        }

        if (IsGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        bool coyoteOk = Time.time - lastGroundedTime <= coyoteTime;
        bool jumpBuffered = Time.time - lastJumpPressedTime <= jumpBufferTime;
        if (coyoteOk && jumpBuffered)
        {
            verticalVelocity = Mathf.Sqrt(-2f * gravity * JumpHeight);
            lastJumpPressedTime = -99f;
            lastGroundedTime = -99f;
            Jumped?.Invoke();
        }

        verticalVelocity += gravity * Time.deltaTime;
    }

    private void UpdateGrounded()
    {
        if (groundCheck != null)
            IsGrounded = Physics.CheckSphere(
                groundCheck.position, groundCheckRadius, groundMask, QueryTriggerInteraction.Ignore);
        else
            IsGrounded = controller.isGrounded;

        if (IsGrounded) lastGroundedTime = Time.time;
    }

    /// Subclasses override to handle their own extra input (does nothing by default)
    protected virtual void HandleExtraInput() { }

    /// Clear velocity when switched away (disabled), so switching back doesn't carry old fall speed
    protected virtual void OnDisable()
    {
        verticalVelocity = 0f;
        PlanarSpeed = 0f;   // Reset on switch-away so the animation doesn't stick in walking
        IsMoving = false;
        IsRunning = false;
    }

    protected virtual void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
}