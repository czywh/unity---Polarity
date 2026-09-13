using UnityEngine;

/// <summary>
/// PROP-01 / ROBOT-08　充电桩：继承 InteractableBase。默认 RobotOnly。
///
/// 普通模式：机器人靠近 + 相机对准（IsFocused）后按 F → OnInteract → 开始充电（自动充满）；
///          失焦则中断；instantFull 勾选时按一下瞬间充满。
/// 操作模式：Interactor 被冻结、相机是正交俯视，改由 RobotConsoleMover 用纯距离判定，
///          调 ChargeDirect() 直接充电（见下方公开方法）。
/// </summary>
public class ChargingDock : InteractableBase
{
    [Header("充电")]
    [Tooltip("按下交互键后，自动充到满的速度（每秒充电量）；Instant Full 勾选时忽略")]
    public float rechargeRate = 25f;
    [Tooltip("勾选：按一下瞬间充满；不勾：按一下后按 rechargeRate 自动充到满")]
    public bool instantFull = false;

    [Header("调试（运行时只读）")]
    [SerializeField] private bool charging;        // 是否正在自动充电
    [SerializeField] private float robotEnergy01;  // 当前充电机器人的电量比例

    protected virtual void Reset()
    {
        access = InteractAccess.RobotOnly;
        interactVerb = "Charge";
    }

    protected virtual void Update()
    {
        // 离开充电桩（失焦）→ 中断本次充电
        if (!IsFocused) { charging = false; robotEnergy01 = 0f; return; }

        var energy = GetEnergy();
        robotEnergy01 = energy != null ? energy.Fraction : 0f;

        if (!charging || energy == null) return;

        if (energy.Fraction >= 1f) { charging = false; return; }
        energy.Recharge(rechargeRate * Time.deltaTime);
    }

    // 普通模式：按下交互键(F) → 开始充电 / 瞬间充满
    public override void OnInteract(Interactor interactor)
    {
        var energy = GetEnergy();
        if (energy == null) return;

        if (instantFull) energy.Recharge(energy.MaxEnergy);
        else charging = true;
    }

    /// <summary>
    /// 供操作模式等直接充电（不经 Interactor 焦点）。每帧调用一次；
    /// instantFull 时一次灌满并返回 false；否则按 rechargeRate 充，充满返回 false，否则 true。
    /// </summary>
    public bool ChargeDirect(EnergySystem energy)
    {
        if (energy == null) return false;
        robotEnergy01 = energy.Fraction;

        if (instantFull) { energy.Recharge(energy.MaxEnergy); return false; }
        if (energy.Fraction >= 1f) return false;

        energy.Recharge(rechargeRate * Time.deltaTime);
        return true;
    }

    private EnergySystem GetEnergy()
    {
        return CurrentInteractor != null
            ? CurrentInteractor.GetComponentInParent<EnergySystem>()
            : null;
    }
}