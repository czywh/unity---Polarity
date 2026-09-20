using UnityEngine;

/// <summary>
/// ROBOT-01 Robot movement controller.
/// The movement core is inherited from CharacterMotorBase; only the robot's own parameters are declared here,
/// and HandleExtraInput() hooks up the three ability interfaces "field / missile / recall" (implementations to come).
/// </summary>
public class RobotController : CharacterMotorBase
{
    [Header("Robot Speed / Jump")]
    public float walkSpeed = 4f;
    public float runSpeed = 6f;
    public bool canRun = true;
    public bool canJump = true;
    public float jumpHeight = 1.2f;

    [Header("Ability Keys (interfaces reserved, implemented in later modules)")]
    [Tooltip("Toggle the electric field. Q is taken by character switching, so E for now")]
    public KeyCode fieldKey = KeyCode.E;
    [Tooltip("Recall missile fields")]
    public KeyCode missileRecallKey = KeyCode.G;
    // Left click = fire missile

    protected override float WalkSpeed => walkSpeed;
    protected override float RunSpeed => runSpeed;
    protected override float JumpHeight => jumpHeight;
    protected override bool CanRunNow => canRun;
    protected override bool CanJumpNow => canJump;

    // -- Robot abilities: reserved as interfaces --
    private IFieldEmitter fieldEmitter;
    private IMissileRecaller missileRecaller;
    // Missile firing is now driven by RobotAimController while aiming (left click only fires when aiming)

    protected override void Awake()
    {
        base.Awake();   // Don't forget to let the base class get the CharacterController

        fieldEmitter = GetComponent<IFieldEmitter>();
        missileRecaller = GetComponent<IMissileRecaller>();
    }

    // Robot-specific input entry point
    protected override void HandleExtraInput()
    {
        if (Input.GetKeyDown(fieldKey)) fieldEmitter?.ToggleField();
        if (Input.GetKeyDown(missileRecallKey)) missileRecaller?.TryRecall();
    }

    protected override void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.color = Color.yellow;   // Robot uses yellow to distinguish
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
}