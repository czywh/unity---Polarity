using UnityEngine;

/// <summary>
/// Passive falling. Attach to player / robot, on the same object as CharacterMotorBase and CharacterController.
///
/// Problem solved: CharacterSwitcher disables a character's CharacterMotorBase when switching away,
/// and gravity is applied in the motor's Update -- once the component is disabled, non-active characters hang in the air.
///
/// This component stays enabled, and only when "motor is disabled (i.e. not the active character) and not in the death flow"
/// takes over [vertical] gravity so the character falls to the ground naturally; it yields as soon as the character becomes active (motor enabled).
/// Only handles vertical falling, never horizontal movement, matching this game's inertia-free movement model.
///
/// All settings are read from the motor (gravity / groundCheck / groundMask); no duplicate parameters, just add it.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PassiveFall : MonoBehaviour
{
    private CharacterController controller;
    private CharacterMotorBase motor;
    private CharacterDeathHandler death;         // Optional
    private RobotConsoleMover consoleMover;      // Optional: takes over gravity and jumping in Operation Mode
    private float verticalVelocity;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        motor = GetComponent<CharacterMotorBase>();
        death = GetComponent<CharacterDeathHandler>();
        consoleMover = GetComponent<RobotConsoleMover>();
    }

    void Update()
    {
        if (motor == null) return;

        // Operation Mode: when RobotConsoleMover is enabled it owns vertical motion (gravity + jump).
        // Must yield here -- otherwise both components accumulate verticalVelocity and each call
        // controller.Move(), doubling gravity and immediately pushing jumps back to the ground.
        if (consoleMover != null && consoleMover.enabled)
        {
            verticalVelocity = 0f;
            return;
        }

        // Active character (motor enabled) -> the motor handles gravity itself; this component yields and resets
        // In death flow -> CharacterDeathHandler freezes/teleports; do not fight it by applying gravity here
        bool dying = death != null && death.IsDying;
        if (motor.enabled || dying)
        {
            verticalVelocity = 0f;
            return;
        }

        // CharacterController is briefly disabled at the moment of teleport; skip this frame
        if (!controller.enabled) return;

        bool grounded = motor.groundCheck != null
            ? Physics.CheckSphere(motor.groundCheck.position, motor.groundCheckRadius,
                                  motor.groundMask, QueryTriggerInteraction.Ignore)
            : controller.isGrounded;

        if (grounded && verticalVelocity < 0f)
            verticalVelocity = -2f;   // Stick to the ground, avoid accumulating a huge fall speed

        verticalVelocity += motor.gravity * Time.deltaTime;
        controller.Move(Vector3.up * verticalVelocity * Time.deltaTime);
    }
}