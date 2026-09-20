using UnityEngine;

/// <summary>
/// 被动落地。挂在 player / robot 上，和 CharacterMotorBase、CharacterController 同物体。
///
/// 解决的问题：CharacterSwitcher 切走角色时会禁用其 CharacterMotorBase，
/// 而重力是在 motor 的 Update 里施加的——组件一禁用，非当前角色就卡在空中不落地。
///
/// 本组件常驻启用，只在「motor 被禁用(即非当前角色) 且 不在死亡流程中」时，
/// 接管【垂直方向】的重力，让角色自然落到地面；一旦成为当前角色(motor 启用)立即让位。
/// 只管竖直下落，不碰水平移动，符合本作无惯性的移动模型。
///
/// 配置全部从 motor 读取(gravity / groundCheck / groundMask)，无需重复设参，加上即可。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PassiveFall : MonoBehaviour
{
    private CharacterController controller;
    private CharacterMotorBase motor;
    private CharacterDeathHandler death;         // 可空
    private RobotConsoleMover consoleMover;      // 可空：操作模式下由它接管重力与跳跃
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

        // 操作模式：RobotConsoleMover 启用时由它独占垂直运动（重力 + 跳跃）。
        // 这里必须让位——否则两个组件各自累积 verticalVelocity 并各调一次
        // controller.Move()，重力会变成两份，跳跃也会被立刻按回地面。
        if (consoleMover != null && consoleMover.enabled)
        {
            verticalVelocity = 0f;
            return;
        }

        // 当前角色(motor 启用) → motor 自己处理重力，本组件让位并清零
        // 死亡流程中 → 交给 CharacterDeathHandler 冻结/传送，别在这抢着施加重力
        bool dying = death != null && death.IsDying;
        if (motor.enabled || dying)
        {
            verticalVelocity = 0f;
            return;
        }

        // 传送瞬间 CharacterController 被临时关闭，跳过这一帧
        if (!controller.enabled) return;

        bool grounded = motor.groundCheck != null
            ? Physics.CheckSphere(motor.groundCheck.position, motor.groundCheckRadius,
                                  motor.groundMask, QueryTriggerInteraction.Ignore)
            : controller.isGrounded;

        if (grounded && verticalVelocity < 0f)
            verticalVelocity = -2f;   // 贴地，避免累积成巨大下坠速度

        verticalVelocity += motor.gravity * Time.deltaTime;
        controller.Move(Vector3.up * verticalVelocity * Time.deltaTime);
    }
}