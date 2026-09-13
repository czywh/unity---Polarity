using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// PROP　吸电桩：能量中枢。任意领域覆盖它时充电（机器人 E 领域、导弹领域都算——都是 ElectricField）。
///
/// 它把自己的电量【百分比】转接给绑定的一个或多个可交互物（电子桥等）：
///   吸电桩百分比 f  →  每座绑定桥的电量 = f × 该桥自己的 maxEnergy
/// 于是每座桥仍用各自的 maxEnergy / interactThreshold，能在不同充电进度依次激活。
///
/// 绑定的桥被自动标记为 externallyDriven：它们不再自行充放电、不直接响应领域，
/// 一切经由吸电桩转接。玩家 / 机器人只需对吸电桩操作。
/// </summary>
public class EnergyPylon : ElectricEntity
{
    [Header("吸电桩：绑定的可交互物")]
    [Tooltip("绑定的电子桥 / 电子实体：它们改由本桩电量百分比驱动，不再自行充放电")]
    [SerializeField] private List<ElectricEntity> boundEntities = new List<ElectricEntity>();

    protected override void Awake()
    {
        base.Awake();
        applyColorGradient = false;   // 吸电桩恒不渐变（兜底已有实例里残留的 true）
        // 绑定的实体交由本桩驱动：标记 externallyDriven，让它们跳过自身充放电 / 领域响应
        for (int i = 0; i < boundEntities.Count; i++)
            if (boundEntities[i] != null && boundEntities[i] != this)
                boundEntities[i].externallyDriven = true;
    }

    protected override void Start()
    {
        base.Start();
        PushToBound();   // 初始对齐一次
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();   // 电量镜像调试字段
        PushToBound();       // 把本桩电量百分比推送给所有绑定实体
    }

    private void PushToBound()
    {
        float f = Fraction;
        for (int i = 0; i < boundEntities.Count; i++)
            if (boundEntities[i] != null && boundEntities[i] != this)
                boundEntities[i].SetEnergyFraction(f);
    }

    private void Reset()
    {
        energyMode = ElectricEnergyMode.ChargeOnly;    // 靠领域充电（E 领域 / 导弹领域）
        outsideBehavior = OutsideBehavior.None;        // 默认离场保持电量
        interactThreshold = 0f;                        // 始终"可交互"→碰撞体稳定，便于覆盖判定
        keepAimableWhenPassable = true;
        initialEnergy = 0f;
        chargeRate = 50f;
        applyColorGradient = false;                    // 吸电桩不需要颜色渐变，保留自身材质
    }
}