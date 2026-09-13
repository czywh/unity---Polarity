using UnityEngine;

/// <summary>
/// CORE-06　机器人电量系统（先用颜色表达消耗）。
///
/// 满电 = fullColor(FA741E 橙)，空电 = emptyColor(灰)，按电量比例在两色间渐变。
/// 对外提供 Drain（耗电，发射器调用）/ Recharge（充电，充电桩调用）/ 状态查询。
/// 自身不主动耗电——耗多少、何时耗由 RobotFieldEmitter 等驱动。
/// </summary>
[DisallowMultipleComponent]
public class EnergySystem : MonoBehaviour
{
    [Header("电量")]
    public float maxEnergy = 100f;
    [Tooltip("初始电量，默认满")]
    public float currentEnergy = 100f;

    [Header("颜色表现（褪色）")]
    [Tooltip("受电量控制上色的渲染器；留空自动收集机器人身上的")]
    [SerializeField] private Renderer[] renderers;
    [Tooltip("满电色（FA741E）")]
    public Color fullColor = new Color(0.9804f, 0.4549f, 0.1176f, 1f);
    [Tooltip("空电色（灰）")]
    public Color emptyColor = new Color(0.45f, 0.45f, 0.45f, 1f);

    // —— 对外只读状态 ——
    public float CurrentEnergy => currentEnergy;
    public float MaxEnergy => maxEnergy;
    public float Fraction => maxEnergy > 0f ? Mathf.Clamp01(currentEnergy / maxEnergy) : 0f;
    public bool HasEnergy => currentEnergy > 0f;
    public bool IsEmpty => currentEnergy <= 0f;

    /// 电量耗尽的那一刻触发一次
    public event System.Action Depleted;

    private MaterialPropertyBlock mpb;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP
    private static readonly int ColorId = Shader.PropertyToID("_Color");         // 内置管线

    private void Awake()
    {
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(true);
        mpb = new MaterialPropertyBlock();
        currentEnergy = Mathf.Clamp(currentEnergy, 0f, maxEnergy);
    }

    private void Start()
    {
        ApplyColor();
    }

    /// 消耗电量（发射器每帧调用 drainPerSecond * dt）
    public void Drain(float amount)
    {
        if (amount <= 0f || currentEnergy <= 0f) return;
        float prev = currentEnergy;
        currentEnergy = Mathf.Max(0f, currentEnergy - amount);
        if (!Mathf.Approximately(prev, currentEnergy)) ApplyColor();
        if (currentEnergy <= 0f && prev > 0f) Depleted?.Invoke();
    }

    /// 充电（充电桩用）
    public void Recharge(float amount)
    {
        if (amount <= 0f) return;
        float prev = currentEnergy;
        currentEnergy = Mathf.Min(maxEnergy, currentEnergy + amount);
        if (!Mathf.Approximately(prev, currentEnergy)) ApplyColor();
    }

    // 按电量比例在 灰↔橙 之间上色
    private void ApplyColor()
    {
        if (renderers == null) return;
        Color c = Color.Lerp(emptyColor, fullColor, Fraction);
        foreach (var r in renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, c);
            mpb.SetColor(ColorId, c);
            r.SetPropertyBlock(mpb);
        }
    }
}