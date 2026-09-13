using UnityEngine;

/// <summary>
/// 电量条的通用数据源：血条只需要这些信息即可渲染，与具体是 ElectricEntity 还是敌人无关。
/// </summary>
public interface IBarSource
{
    bool BarAlive { get; }               // 目标是否仍有效（被销毁则 false）
    float Fraction { get; }              // 0..1
    float CurrentEnergy { get; }
    float MaxEnergy { get; }
    bool BarInteractive { get; }         // 前景条配色：激活(紫) / 非激活(灰)
    float BarThresholdFraction { get; }  // 阈值线位置 0..1；<0 = 无阈值线
    Vector3 BarWorldPosition { get; }    // 条在世界中的锚点
}

/// <summary>
/// ElectricEntity → IBarSource 的包装器（纯 C# 适配，不改 ElectricEntity 本身）。
/// 由 EntityBarManager 缓存复用，每个实体一个。
/// </summary>
public class ElectricEntityBarSource : IBarSource
{
    public readonly ElectricEntity Entity;
    public ElectricEntityBarSource(ElectricEntity e) { Entity = e; }

    public bool BarAlive => Entity != null;
    public float Fraction => Entity != null ? Entity.Fraction : 0f;
    public float CurrentEnergy => Entity != null ? Entity.CurrentEnergy : 0f;
    public float MaxEnergy => Entity != null ? Entity.MaxEnergy : 0f;
    public bool BarInteractive => Entity != null && Entity.IsInteractive;

    public float BarThresholdFraction =>
        (Entity != null && Entity.MaxEnergy > 0f)
            ? Mathf.Clamp01(Entity.interactThreshold / Entity.MaxEnergy)
            : -1f;

    public Vector3 BarWorldPosition
    {
        get
        {
            if (Entity == null) return Vector3.zero;
            if (Entity.BarAnchor != null)
                return Entity.BarAnchor.position + Entity.BarWorldOffset;
            Bounds b = Entity.WorldBounds;
            float y = Mathf.Lerp(b.min.y, b.max.y, Entity.BarVertical);
            return new Vector3(b.center.x, y, b.center.z) + Entity.BarWorldOffset;
        }
    }
}