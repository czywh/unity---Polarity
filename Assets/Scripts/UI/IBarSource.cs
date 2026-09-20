using UnityEngine;

/// <summary>
/// Generic data source for energy bars: the bar only needs this info to render, regardless of whether it's an ElectricEntity or an enemy.
/// </summary>
public interface IBarSource
{
    bool BarAlive { get; }               // Whether the target is still valid (false if destroyed)
    float Fraction { get; }              // 0..1
    float CurrentEnergy { get; }
    float MaxEnergy { get; }
    bool BarInteractive { get; }         // Foreground bar color: active (purple) / inactive (gray)
    float BarThresholdFraction { get; }  // Threshold line position 0..1; <0 = no threshold line
    Vector3 BarWorldPosition { get; }    // Bar's anchor point in the world
}

/// <summary>
/// ElectricEntity -> IBarSource wrapper (pure C# adapter, doesn't modify ElectricEntity itself).
/// Cached and reused by EntityBarManager, one per entity.
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