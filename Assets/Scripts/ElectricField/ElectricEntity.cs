using UnityEngine;

/// <summary>充电 / 放电模式：仅充电、仅放电、充放皆可。</summary>
public enum ElectricEnergyMode
{
    ChargeOnly,     // 仅充电：机器人领域充电，玩家不能放电
    DischargeOnly,  // 仅放电：领域不充电，只有玩家瞄准放电
    Both            // 充放皆可：机器人充电 + 玩家放电
}

/// <summary>离开领域后的自动行为（三选一）。</summary>
public enum OutsideBehavior
{
    None,        // 离场后保持当前电量
    Discharge,   // 离场后（保持一会）自动放电
    SelfCharge   // 离场后（保持一会）自动回电到满
}

/// <summary>
/// CORE-05　电子实体基类（电量模型）。
///
/// 电量为设计单位（范围 0 ~ maxEnergy，常用 maxEnergy≈100，最大可到 1000）。
/// 颜色与可交互都按【电量百分比 = CurrentEnergy / maxEnergy】计算（充电、放电统一）：
///   · CurrentEnergy < interactThreshold → 偏 unchargedColor（灰）：不可交互、关碰撞（可穿过）
///   · CurrentEnergy ≥ interactThreshold → 偏 chargedColor（紫）：可交互、开碰撞（站立 / 阻挡）
///
/// 电量来源按模式区分：
///   · 充电交互：机器人领域内电量上升（仅 ChargeOnly / Both）
///   · 放电交互：玩家瞄准按住左键调 Discharge() 使电量下降（仅 DischargeOnly / Both）
/// </summary>
[DisallowMultipleComponent]
public abstract class ElectricEntity : MonoBehaviour
{
    [Header("受控部件（留空自动收集）")]
    [SerializeField] protected Renderer[] renderers;
    [SerializeField] protected Collider[] colliders;

    [Header("电量（设计单位，范围 0~1000，常用 100 左右）")]
    [Range(0f, 1000f)]
    [Tooltip("该物体的最大电量")]
    public float maxEnergy = 100f;
    [Tooltip("最小电量：放电最多降到这里（0 = 可吸干净）")]
    public float minEnergy = 0f;
    [Tooltip("初始电量。仅放电的阻挡物一般设为 = maxEnergy（开局满电、可交互）")]
    public float initialEnergy = 0f;
    [Tooltip("可交互阈值（设计单位）。如 maxEnergy=100 时设 50")]
    public float interactThreshold = 50f;

    [Header("模式")]
    [Tooltip("仅充电 / 仅放电 / 充放皆可")]
    public ElectricEnergyMode energyMode = ElectricEnergyMode.ChargeOnly;

    [Header("外部驱动（吸电桩）")]
    [Tooltip("被吸电桩绑定后自动为 true：电量改由吸电桩按百分比驱动，本体不再自行充放电、不响应领域 / 直接放电")]
    public bool externallyDriven = false;

    [Header("充电（机器人领域）")]
    [Tooltip("在领域内每秒充电量（设计单位/秒）")]
    public float chargeRate = 50f;

    [Header("离场自动行为")]
    [Tooltip("离开领域后：None 保持 / Discharge 自动放电 / SelfCharge 自动回满")]
    public OutsideBehavior outsideBehavior = OutsideBehavior.Discharge;
    [Tooltip("离场后先保持当前电量多少秒，再开始自动放电 / 自动回电")]
    public float holdAfterLeaving = 5f;
    [Tooltip("离场自动放电每秒量（Discharge 用）")]
    public float outsideDischargeRate = 100f;
    [Tooltip("离场自动回电每秒量（SelfCharge 用）")]
    public float selfChargeRate = 50f;

    [Header("碰撞切换方式")]
    [Tooltip("勾选：不可交互时把碰撞体设为 Trigger（可穿过但仍能被瞄准射线命中，可重新选中继续吸电）。\n取消：不可交互时直接禁用碰撞体（射线也打不到）")]
    public bool keepAimableWhenPassable = true;
    [Tooltip("碰撞体是否随可交互状态开关。移动障碍物设 false：碰撞体始终保持实心，靠位置表达状态")]
    public bool colliderFollowsInteractive = true;

    [Header("颜色（按电量百分比渐变）")]
    [Tooltip("是否按电量渐变上色；吸电桩这类纯中枢可关掉，保留自身材质外观")]
    public bool applyColorGradient = true;
    [Tooltip("低电色（灰）：不可交互")]
    public Color unchargedColor = new Color(0.5f, 0.5f, 0.5f, 1f);
    [Tooltip("满电色（紫）：可交互")]
    public Color chargedColor = new Color(0.6f, 0.2f, 0.9f, 1f);

    [Header("血条位置（世界跟随 UI，可逐物体调）")]
    [Tooltip("显式锚点：拖一个空子物体进来，血条固定跟它（最自由）。留空则用下面的比例在包围盒上定位")]
    [SerializeField] private Transform barAnchor;
    [Range(0f, 1f)]
    [Tooltip("无显式锚点时，血条在包围盒高度上的位置：0=底部, 0.5=中心, 1=顶部")]
    [SerializeField] private float barVertical = 1f;
    [Tooltip("血条位置再叠加的世界偏移（微调用）")]
    [SerializeField] private Vector3 barWorldOffset = Vector3.zero;

    [Header("调试（运行时只读）")]
    [Tooltip("当前电量（镜像 CurrentEnergy，方便观察充/放电）")]
    [SerializeField] private float currentEnergyReadout;
    [Tooltip("当前是否可交互（电量 ≥ 阈值）")]
    [SerializeField] private bool interactiveReadout;

    // —— 对外只读状态 ——
    public float CurrentEnergy { get; private set; }
    public float MaxEnergy => maxEnergy;
    public float Fraction => maxEnergy > 0f ? Mathf.Clamp01(CurrentEnergy / maxEnergy) : 0f;  // 0..1 百分比
    public bool IsInteractive { get; private set; }
    public bool CanCharge => energyMode == ElectricEnergyMode.ChargeOnly || energyMode == ElectricEnergyMode.Both;
    public bool CanDischarge => energyMode == ElectricEnergyMode.DischargeOnly || energyMode == ElectricEnergyMode.Both;

    // —— 血条位置（供世界跟随 UI 读取）——
    public Transform BarAnchor => barAnchor;
    public float BarVertical => barVertical;
    public Vector3 BarWorldOffset => barWorldOffset;

    private float leaveHoldTimer;
    private MaterialPropertyBlock mpb;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP
    private static readonly int ColorId = Shader.PropertyToID("_Color");         // 内置管线

    protected virtual void OnValidate()
    {
        maxEnergy = Mathf.Clamp(maxEnergy, 0f, 1000f);
        minEnergy = Mathf.Clamp(minEnergy, 0f, maxEnergy);
        initialEnergy = Mathf.Clamp(initialEnergy, minEnergy, maxEnergy);
        interactThreshold = Mathf.Clamp(interactThreshold, 0f, maxEnergy);
    }

    protected virtual void Awake()
    {
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(true);
        if (colliders == null || colliders.Length == 0)
            colliders = GetComponentsInChildren<Collider>(true);
        mpb = new MaterialPropertyBlock();
        CurrentEnergy = Mathf.Clamp(initialEnergy, minEnergy, maxEnergy);
    }

    protected virtual void Start()
    {
        if (renderers != null)
            foreach (var r in renderers) if (r != null) r.enabled = true;

        ApplyColor();
        SetInteractive(CurrentEnergy >= interactThreshold, force: true);
    }

    protected virtual void Update()
    {
        if (externallyDriven) return;   // 电量由吸电桩驱动，跳过自身充放电逻辑

        // 领域内充电：仅"允许充电"的物体（ChargeOnly / Both）在领域内才 +电
        bool fieldCharging = CanCharge && IsCovered();
        float prev = CurrentEnergy;

        if (fieldCharging)
        {
            CurrentEnergy += chargeRate * Time.deltaTime;
            leaveHoldTimer = holdAfterLeaving;   // 正在被充电 → 重置离场计时
        }
        else
        {
            // 不在被领域充电 → 离场自动行为（先保持一段时间，再执行）
            if (leaveHoldTimer > 0f)
            {
                leaveHoldTimer -= Time.deltaTime;
            }
            else
            {
                switch (outsideBehavior)
                {
                    case OutsideBehavior.Discharge:
                        CurrentEnergy -= outsideDischargeRate * Time.deltaTime;
                        break;
                    case OutsideBehavior.SelfCharge:
                        CurrentEnergy += selfChargeRate * Time.deltaTime;   // 自愈回满
                        break;
                    // None：什么都不做
                }
            }
        }

        CurrentEnergy = Mathf.Clamp(CurrentEnergy, minEnergy, maxEnergy);

        if (!Mathf.Approximately(CurrentEnergy, prev)) ApplyColor();

        bool shouldInteract = CurrentEnergy >= interactThreshold;
        if (shouldInteract != IsInteractive) SetInteractive(shouldInteract, false);
    }

    /// 玩家放电：瞄准 + 按住左键时每帧调用，amount 为设计单位。仅 DischargeOnly / Both 生效。
    public void Discharge(float amount)
    {
        if (externallyDriven) return;   // 由吸电桩驱动，忽略直接放电（玩家应对吸电桩操作）
        if (!CanDischarge || amount <= 0f) return;

        leaveHoldTimer = holdAfterLeaving;   // 每次放电都重置离场计时 → 停手一段时间后才自愈

        float prev = CurrentEnergy;
        CurrentEnergy = Mathf.Clamp(CurrentEnergy - amount, minEnergy, maxEnergy);
        if (Mathf.Approximately(CurrentEnergy, prev)) return;

        ApplyColor();
        bool shouldInteract = CurrentEnergy >= interactThreshold;
        if (shouldInteract != IsInteractive) SetInteractive(shouldInteract, false);
    }

    /// <summary>
    /// 环境效果扣电（激光等），与玩家放电是两个不同的动作：
    ///   · Discharge() 受 energyMode 限制，代表"玩家主动抽电"这个玩法权限；
    ///   · DrainExternal() 不受 energyMode 限制，代表"被环境破坏"，
    ///     所以 ChargeOnly 的吸电桩也能被激光抽干 —— 玩家抽不了，但激光能。
    ///
    /// 同样会重置离场计时，因此被激光照着的物件不会一边掉电一边自愈。
    /// externallyDriven（电量由吸电桩驱动的电桥）忽略本调用：它的电量下一帧就会被
    /// 吸电桩覆盖，扣了也没意义，应该去打吸电桩本身。
    /// </summary>
    public void DrainExternal(float amount)
    {
        if (externallyDriven || amount <= 0f) return;

        leaveHoldTimer = holdAfterLeaving;   // 正在被破坏 → 推迟离场自愈

        float prev = CurrentEnergy;
        CurrentEnergy = Mathf.Clamp(CurrentEnergy - amount, minEnergy, maxEnergy);
        if (Mathf.Approximately(CurrentEnergy, prev)) return;

        ApplyColor();
        bool shouldInteract = CurrentEnergy >= interactThreshold;
        if (shouldInteract != IsInteractive) SetInteractive(shouldInteract, false);
    }

    /// 供吸电桩调用：按百分比（0..1）设置本体电量，并刷新颜色 / 可交互状态。
    /// 电量 = fraction × 本体 maxEnergy，因此每座桥仍用自己的 maxEnergy / interactThreshold。
    public void SetEnergyFraction(float fraction)
    {
        float target = Mathf.Clamp01(fraction) * maxEnergy;
        if (!Mathf.Approximately(target, CurrentEnergy))
        {
            CurrentEnergy = target;
            ApplyColor();
        }
        bool shouldInteract = CurrentEnergy >= interactThreshold;
        if (shouldInteract != IsInteractive) SetInteractive(shouldInteract, false);
    }

    // 把状态镜像到可见字段（任何模式每帧都刷新，方便 Inspector 观察）
    protected virtual void LateUpdate()
    {
        currentEnergyReadout = CurrentEnergy;
        interactiveReadout = IsInteractive;
    }

    /// 是否被任意领域覆盖：优先用【碰撞体形状 ⨯ 领域球】精确求交（球体真正碰到桥才算）；
    /// 无可用碰撞体时退回包围盒。充电与 UI 血条共用同一判定。
    public bool IsCovered()
    {
        var mgr = ElectricFieldManager.Instance;
        if (mgr == null) return false;

        bool hasCollider = false;
        if (colliders != null)
        {
            foreach (var c in colliders)
            {
                if (c == null || !c.enabled) continue;
                hasCollider = true;
                if (mgr.IsColliderInAnyField(c)) return true;   // 球真正相交碰撞体
            }
        }
        if (hasCollider) return false;   // 有碰撞体但都没相交

        return mgr.IsBoundsInAnyField(WorldBounds);   // 退化：无碰撞体时用包围盒
    }

    /// 自身渲染器合并的世界包围盒（无渲染器则退化为自身位置的小盒）
    public Bounds WorldBounds
    {
        get
        {
            if (renderers != null && renderers.Length > 0)
            {
                bool has = false; Bounds b = default;
                foreach (var r in renderers)
                {
                    if (r == null) continue;
                    if (!has) { b = r.bounds; has = true; }
                    else b.Encapsulate(r.bounds);
                }
                if (has) return b;
            }
            return new Bounds(transform.position, Vector3.one * 0.1f);
        }
    }

    // 电量表现（充电 / 放电 / 外部驱动统一走这里）；子类可重写为其它表现（如透明度）
    protected virtual void ApplyColor()
    {
        if (!applyColorGradient) return;   // 关闭渐变（如吸电桩）：保留自身材质外观
        if (renderers == null) return;
        Color c = Color.Lerp(unchargedColor, chargedColor, Fraction);
        foreach (var r in renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, c);
            mpb.SetColor(ColorId, c);
            r.SetPropertyBlock(mpb);
        }
    }

    // 可交互 → 实心碰撞（可站立 / 阻挡）；不可交互 → 视选项：
    //   keepAimableWhenPassable=true  → 变 Trigger（可穿过，但射线仍能命中，可重新选中吸电）
    //   keepAimableWhenPassable=false → 直接禁用碰撞体
    private void SetInteractive(bool interactive, bool force)
    {
        IsInteractive = interactive;
        if (colliderFollowsInteractive && colliders != null)
        {
            foreach (var col in colliders)
            {
                if (col == null) continue;
                if (keepAimableWhenPassable)
                {
                    col.enabled = true;              // 始终启用，保证射线能打到
                    col.isTrigger = !interactive;    // 不可交互时变 Trigger：可穿过但可命中
                }
                else
                {
                    col.isTrigger = false;
                    col.enabled = interactive;       // 老行为：不可交互直接关碰撞
                }
            }
        }
        OnInteractiveChanged(interactive);
    }

    /// 子类钩子：可交互状态切换时的额外表现（音效 / 粒子等）
    protected virtual void OnInteractiveChanged(bool interactive) { }
}