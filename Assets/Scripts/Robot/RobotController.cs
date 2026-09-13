using UnityEngine;

/// <summary>
/// ROBOT-01　机器人移动控制器。
/// 移动内核继承自 CharacterMotorBase；这里只声明机器人自己的参数，
/// 并通过 HandleExtraInput() 接上「领域 / 导弹 / 回收」三个能力接口（实现待后续）。
/// </summary>
public class RobotController : CharacterMotorBase
{
    [Header("机器人速度 / 跳跃")]
    public float walkSpeed = 4f;
    public float runSpeed = 6f;
    public bool canRun = true;
    public bool canJump = true;
    public float jumpHeight = 1.2f;

    [Header("能力按键（接口预留，实现待后续模块）")]
    [Tooltip("开关电子领域。Q 已被角色切换占用，这里暂用 E")]
    public KeyCode fieldKey = KeyCode.E;
    [Tooltip("回收导弹领域")]
    public KeyCode missileRecallKey = KeyCode.G;
    // 左键 = 发射导弹

    protected override float WalkSpeed => walkSpeed;
    protected override float RunSpeed => runSpeed;
    protected override float JumpHeight => jumpHeight;
    protected override bool CanRunNow => canRun;
    protected override bool CanJumpNow => canJump;

    // —— 机器人能力：以接口形式预留 ——
    private IFieldEmitter fieldEmitter;
    private IMissileRecaller missileRecaller;
    // 导弹发射改由 RobotAimController 在瞄准状态下驱动（左键只在瞄准时才发射）

    protected override void Awake()
    {
        base.Awake();   // 别忘了让基类拿到 CharacterController

        fieldEmitter = GetComponent<IFieldEmitter>();
        missileRecaller = GetComponent<IMissileRecaller>();
    }

    // 机器人专属输入入口
    protected override void HandleExtraInput()
    {
        if (Input.GetKeyDown(fieldKey)) fieldEmitter?.ToggleField();
        if (Input.GetKeyDown(missileRecallKey)) missileRecaller?.TryRecall();
    }

    protected override void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.color = Color.yellow;   // 机器人用黄色区分
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
}