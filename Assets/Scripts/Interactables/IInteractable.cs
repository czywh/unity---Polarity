using UnityEngine;

/// <summary>交互者类型：当前由谁发起交互</summary>
public enum InteractorType { Player, Robot }

/// <summary>谁可以交互这个物体</summary>
public enum InteractAccess { PlayerOnly, RobotOnly, Both }

/// <summary>
/// 可交互物接口。场景中一切可交互对象（充电桩、开关、拉杆…）实现它。
/// 实际项目里一般直接继承 InteractableBase（已实现本接口 + 焦点管理）。
/// </summary>
public interface IInteractable
{
    /// 谁能交互（Player / Robot / Both）
    InteractAccess Access { get; }

    /// 物体当前是否开放交互（可被临时关闭，如未激活 / 已损坏）
    bool IsInteractable { get; }

    /// 距离 / 朝向参照点
    Transform InteractTransform { get; }

    /// 综合判断：该类型的交互者现在能否使用本物体
    bool CanBeUsedBy(InteractorType who);

    /// 进入焦点（满足范围 + 对准 + 权限）
    void OnFocusEnter(Interactor interactor);

    /// 离开焦点
    void OnFocusExit(Interactor interactor);

    /// 按下交互键时触发（按键式交互用；持续式如充电可只看 IsFocused）
    void OnInteract(Interactor interactor);
}