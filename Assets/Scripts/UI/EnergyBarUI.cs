using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI-01 机器人电量条：屏幕左上角常驻的缓冲式血条 + 低电量警告。
///
/// 层级结构（Canvas 下，从下到上 = 绘制顺序）：
///   EnergyBar_BG  —— 底槽（深色，空条背景）
///     ├─ BufferFill —— 缓冲层（浅色，慢速跟随，负责"残影 / 预充"）
///     └─ FrontFill  —— 主体层（橙色，快速跟随，显示真实电量，画在最上面）
///
/// 缓冲逻辑：主体层/缓冲层各在"领先方向"用快速度、"追赶方向"用慢速度，
///          恒满足 buffer >= front，掉电露出残影、充电先浅色预充。
///
/// 低电量警告：电量低于阈值后，主体层随"陷得越深"越红、闪烁越快；
///            闪烁用提亮实现（alpha 恒为 1），避免透出后面的缓冲层。
/// </summary>
[DisallowMultipleComponent]
public class EnergyBarUI : MonoBehaviour
{
    [Header("数据源")]
    [Tooltip("机器人的 EnergySystem；留空则运行时自动查找场景中的第一个")]
    [SerializeField] private EnergySystem energy;

    [Header("填充图（Filled / Horizontal / Left）")]
    [Tooltip("橙色主体层")]
    [SerializeField] private Image frontFill;
    [Tooltip("浅色缓冲层，画在主体后面")]
    [SerializeField] private Image bufferFill;

    [Header("速度（每秒填充比例）")]
    [Tooltip("领先边速度：掉电时的 front / 充电时的 buffer")]
    [SerializeField] private float fastSpeed = 8f;
    [Tooltip("追赶边速度：掉电时的 buffer / 充电时的 front。越小残影停留越久")]
    [SerializeField] private float slowSpeed = 1.5f;

    [Header("缓冲层变色（可选）")]
    [Tooltip("开启后，掉电和充电时缓冲层用不同颜色")]
    [SerializeField] private bool tintBufferByDirection = false;
    [SerializeField] private Color drainColor = new Color(1f, 0.92f, 0.80f);
    [SerializeField] private Color chargeColor = new Color(0.65f, 1f, 0.75f);

    [Header("低电量警告")]
    [Tooltip("总开关")]
    [SerializeField] private bool enableLowWarning = true;
    [Range(0f, 1f)]
    [Tooltip("电量低于此比例开始警告（0.2 = 20%）")]
    [SerializeField] private float lowThreshold = 0.2f;
    [Tooltip("最深处（接近耗尽）的警告色")]
    [SerializeField] private Color warningColor = new Color(0.89f, 0.23f, 0.18f);
    [Tooltip("刚进警告区的闪烁频率")]
    [SerializeField] private float minPulseSpeed = 2f;
    [Tooltip("接近耗尽时的闪烁频率")]
    [SerializeField] private float maxPulseSpeed = 8f;
    [Range(0f, 1f)]
    [Tooltip("脉冲时向白色提亮的幅度，越大闪得越刺眼")]
    [SerializeField] private float pulseStrength = 0.55f;

    [Header("数值 label（可选）")]
    [Tooltip("显示电量数字的文本，可留空")]
    [SerializeField] private TMP_Text label;
    [Tooltip("Percent = 百分比 (73%)；Value = 当前/最大 (73 / 100)")]
    [SerializeField] private LabelMode labelMode = LabelMode.Percent;

    public enum LabelMode { Percent, Value }

    private float frontValue;
    private float bufferValue;
    private float lastTarget;
    private Color normalColor = new Color(0.98f, 0.45f, 0.12f); // FA741E 兜底

    private void Awake()
    {
        // 若 Unity 版本较老报错，把 FindFirstObjectByType 换成 FindObjectOfType
        if (energy == null)
            energy = FindFirstObjectByType<EnergySystem>();

        SetupFillImage(frontFill);
        SetupFillImage(bufferFill);

        // 记住 Inspector 里设的正常色，警告结束后还原
        if (frontFill != null) normalColor = frontFill.color;
    }

    private void Start()
    {
        float f = energy != null ? energy.Fraction : 1f;
        frontValue = bufferValue = lastTarget = f;
        Apply();
    }

    private void Update()
    {
        if (energy == null) return;

        float target = energy.Fraction;
        float dt = Time.deltaTime;

        // 主体层：向下(掉电)领先→快，向上(充电)追赶→慢
        float fSpeed = (target < frontValue ? fastSpeed : slowSpeed);
        frontValue = Mathf.MoveTowards(frontValue, target, fSpeed * dt);

        // 缓冲层：向上(充电)领先→快，向下(掉电)追赶→慢
        float bSpeed = (target > bufferValue ? fastSpeed : slowSpeed);
        bufferValue = Mathf.MoveTowards(bufferValue, target, bSpeed * dt);

        bufferValue = Mathf.Max(bufferValue, frontValue);

        if (tintBufferByDirection && bufferFill != null)
        {
            if (target < lastTarget - 0.0001f) bufferFill.color = drainColor;
            else if (target > lastTarget + 0.0001f) bufferFill.color = chargeColor;
        }
        lastTarget = target;

        Apply();
    }

    private void Apply()
    {
        if (frontFill != null) frontFill.fillAmount = frontValue;
        if (bufferFill != null) bufferFill.fillAmount = bufferValue;

        UpdateFrontColor();
        UpdateLabel();
    }

    // 数值 label：直接读 EnergySystem 的 CurrentEnergy / MaxEnergy
    private void UpdateLabel()
    {
        if (label == null || energy == null) return;

        label.text = labelMode == LabelMode.Value
            ? $"{Mathf.RoundToInt(energy.CurrentEnergy)} / {Mathf.RoundToInt(energy.MaxEnergy)}"
            : $"{Mathf.RoundToInt(energy.Fraction * 100f)}%";
    }

    // 低电量：越低越红、闪烁越快；alpha 恒为 1，避免透出后面的缓冲层
    private void UpdateFrontColor()
    {
        if (frontFill == null) return;

        if (!enableLowWarning || energy == null || lowThreshold <= 0f)
        {
            frontFill.color = normalColor;
            return;
        }

        float f = energy.Fraction;
        if (f > lowThreshold)
        {
            frontFill.color = normalColor;
            return;
        }

        // severity：阈值处=0，耗尽=1
        float severity = 1f - Mathf.Clamp01(f / lowThreshold);

        // 底色：正常色 → 警告红
        Color baseC = Color.Lerp(normalColor, warningColor, severity);

        // 脉冲：底色 ↔ 提亮版之间来回，频率随 severity 升高
        float speed = Mathf.Lerp(minPulseSpeed, maxPulseSpeed, severity);
        float pulse = Mathf.Sin(Time.unscaledTime * speed) * 0.5f + 0.5f; // 0..1
        Color highlight = Color.Lerp(baseC, Color.white, pulseStrength);
        Color c = Color.Lerp(baseC, highlight, pulse);
        c.a = 1f;

        frontFill.color = c;
    }

    private static void SetupFillImage(Image img)
    {
        if (img == null) return;
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = (int)Image.OriginHorizontal.Left;
    }

    /// 供外部（如切换角色后重新绑定）调用
    public void SetEnergySource(EnergySystem source)
    {
        energy = source;
        float f = energy != null ? energy.Fraction : 0f;
        frontValue = bufferValue = lastTarget = f;
    }
}