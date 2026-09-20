using UnityEngine;

/// <summary>
/// Enemy → IBarSource adapter. Attach to an enemy to expose its EnergySystem to the EntityBarManager pool,
/// reusing the same EntityEnergyBar prefab / pooling / screen follow. Display rules are decided by the manager (enemies = always shown in Operation Mode).
/// </summary>
[DisallowMultipleComponent]
public class EnemyBarSource : MonoBehaviour, IBarSource
{
    [Tooltip("Enemy energy; empty = use the EnergySystem on this object/parent")]
    public EnergySystem energy;
    [Tooltip("Bar anchor height offset relative to the enemy (overhead)")]
    public float headOffset = 2f;
    [Tooltip("Whether the bar foreground uses the active color (normally always true for enemies)")]
    public bool interactive = true;

    private void Awake()
    {
        if (energy == null) energy = GetComponentInParent<EnergySystem>();
    }

    public bool BarAlive => this != null && energy != null;
    public float Fraction => energy != null ? energy.Fraction : 0f;
    public float CurrentEnergy => energy != null ? energy.CurrentEnergy : 0f;
    public float MaxEnergy => energy != null ? energy.MaxEnergy : 0f;
    public bool BarInteractive => interactive;
    public float BarThresholdFraction => -1f;   // enemies have no threshold line
    public Vector3 BarWorldPosition => transform.position + Vector3.up * headOffset;

    // For the manager's "inside field / aimed at" checks
    public EnergySystem Energy => energy;
    public Transform Tf => transform;
    public Collider Col => cachedCol != null ? cachedCol : (cachedCol = GetComponentInChildren<Collider>());
    private Collider cachedCol;
}