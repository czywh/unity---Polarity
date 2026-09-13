using UnityEngine;

/// <summary>
/// 敌人 → IBarSource 适配器。挂在敌人上，把它的 EnergySystem 暴露给 EntityBarManager 池子，
/// 复用同一套 EntityEnergyBar prefab / 池化 / 屏幕跟随。显示规则由管理器决定（敌人=操作模式常驻）。
/// </summary>
[DisallowMultipleComponent]
public class EnemyBarSource : MonoBehaviour, IBarSource
{
    [Tooltip("敌人电量；留空取本物体/父级的 EnergySystem")]
    public EnergySystem energy;
    [Tooltip("血条锚点相对敌人的高度偏移（头顶）")]
    public float headOffset = 2f;
    [Tooltip("血条前景是否用激活色（一般敌人恒 true）")]
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
    public float BarThresholdFraction => -1f;   // 敌人无阈值线
    public Vector3 BarWorldPosition => transform.position + Vector3.up * headOffset;

    // 供管理器做"领域内 / 瞄准"判定
    public EnergySystem Energy => energy;
    public Transform Tf => transform;
    public Collider Col => cachedCol != null ? cachedCol : (cachedCol = GetComponentInChildren<Collider>());
    private Collider cachedCol;
}