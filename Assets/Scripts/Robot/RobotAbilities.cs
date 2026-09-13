using UnityEngine;

// 机器人能力接口：实现待后续模块完成（电子领域 / 导弹）。
// RobotController / RobotAimController 通过接口调用，实现为 null 时自动跳过。

/// <summary>ROBOT-04　开关即时电子领域</summary>
public interface IFieldEmitter
{
    void ToggleField();
}

/// <summary>ROBOT-05　发射导弹</summary>
public interface IMissileLauncher
{
    void TryFire();                    // 朝正前方
    void TryFire(Vector3 direction);   // 朝指定方向（瞄准准星方向）
}

/// <summary>ROBOT-07　G 键回收鼠标附近的导弹领域</summary>
public interface IMissileRecaller
{
    void TryRecall();
}