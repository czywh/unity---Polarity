using UnityEngine;

/// <summary>
/// PLAYER-01 Main character movement controller.
/// The entire movement core is inherited from CharacterMotorBase; only the player's own parameters are declared here.
/// </summary>
public class PlayerController : CharacterMotorBase
{
    [Header("Player Speed / Jump")]
    public float walkSpeed = 4f;
    public float runSpeed = 7f;
    public float jumpHeight = 1.5f;

    // Feed player parameters to the base class
    protected override float WalkSpeed => walkSpeed;
    protected override float RunSpeed => runSpeed;
    protected override float JumpHeight => jumpHeight;
    protected override bool CanRunNow => true;   // Player can always run
    protected override bool CanJumpNow => true;  // Player can always jump
    protected override bool CanPhantom => true;  // Player can enter phantom state (floating)

    // No extra player input for now; attack / interaction will be separate components, not stuffed into the movement controller.
}