using UnityEngine;

/// <summary>
/// PLAYER-01　主角移动控制器。
/// 移动内核全部继承自 CharacterMotorBase，这里只声明玩家自己的参数。
/// </summary>
public class PlayerController : CharacterMotorBase
{
    [Header("玩家速度 / 跳跃")]
    public float walkSpeed = 4f;
    public float runSpeed = 7f;
    public float jumpHeight = 1.5f;

    // 把玩家参数喂给基类
    protected override float WalkSpeed => walkSpeed;
    protected override float RunSpeed => runSpeed;
    protected override float JumpHeight => jumpHeight;
    protected override bool CanRunNow => true;   // 玩家恒可奔跑
    protected override bool CanJumpNow => true;  // 玩家恒可跳跃
    protected override bool CanPhantom => true;  // 玩家可进入幽灵态悬浮

    // 玩家暂无额外输入；攻击 / 交互以后各自独立成组件，不塞进移动控制器。
}