using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 可交互物基类：实现 IInteractable，提供访问控制 + 焦点状态 + 全局登记。
/// 充电桩、开关、拉杆等继承本类即可。
///
/// 关键状态 IsFocused：被一个"有权限的交互者"聚焦时为 true——
/// 充电桩等持续式交互只需在 Update 里判断 IsFocused 即可工作。
///
/// 交互提示：InteractVerb（动作词）+ UsesKeyPress（是否按键式）供 InteractionPrompt 组文本。
/// </summary>
[DisallowMultipleComponent]
public abstract class InteractableBase : MonoBehaviour, IInteractable
{
    [Header("交互访问")]
    [Tooltip("谁能交互：仅玩家 / 仅机器人 / 都可以")]
    public InteractAccess access = InteractAccess.Both;
    [Tooltip("是否开放交互；关掉则任何人都用不了（如未激活 / 已损坏）")]
    public bool interactable = true;

    [Header("交互提示")]
    [Tooltip("提示里的动作词，如 Charge / Open / Pull")]
    [SerializeField] protected string interactVerb = "Interact";

    [Header("交互提示位置")]
    [Tooltip("显式锚点：拖一个空子物体进来，提示就固定跟它（最自由）。留空则用下面的比例在包围盒上定位")]
    [SerializeField] private Transform promptAnchor;
    [Range(0f, 1f)]
    [Tooltip("无显式锚点时，提示在包围盒高度上的位置：0=底部, 0.5=中心, 1=顶部；中下方约 0.3")]
    [SerializeField] private float promptVertical = 1f;
    [Tooltip("最终位置再叠加的世界偏移（微调用）")]
    [SerializeField] private Vector3 promptWorldOffset = Vector3.zero;

    [Header("调试（运行时只读，便于检查交互是否触发）")]
    [Tooltip("当前是否被有权限的交互者聚焦 = 你说的 interable 为 true")]
    [SerializeField] private bool isFocusedReadout;

    // —— 全局登记，供 Interactor 遍历（与 ElectricField 同一套思路）——
    private static readonly List<InteractableBase> all = new List<InteractableBase>();
    public static IReadOnlyList<InteractableBase> All => all;

	// public game object Icoin 

    public InteractAccess Access => access;
    public bool IsInteractable => interactable && isActiveAndEnabled;
    public Transform InteractTransform => transform;

    // —— 交互提示对外接口 ——
    /// 提示里的动作词（Charge / Open …）
    public string InteractVerb => interactVerb;
    /// 是否按键式交互：true → 提示显示 "Press [键] to 动作词"；false → 只显示动作词（持续/自动）
    public virtual bool UsesKeyPress => true;

    // —— 交互提示位置 ——
    /// 显式锚点（可空）；非空时提示固定跟它
    public Transform PromptAnchor => promptAnchor;
    /// 包围盒高度上的定位比例：0=底部, 0.5=中心, 1=顶部
    public float PromptVertical => promptVertical;
    /// 叠加的世界偏移
    public Vector3 PromptWorldOffset => promptWorldOffset;

    // —— 焦点状态 ——
    public bool IsFocused { get; private set; }
    public Interactor CurrentInteractor { get; private set; }

    protected virtual void OnEnable()
    {
        if (!all.Contains(this)) all.Add(this);
    }

    protected virtual void OnDisable()
    {
        all.Remove(this);
        if (IsFocused) ClearFocus();
    }

    // 把 IsFocused 镜像到可见字段，方便在 Inspector 里实时观察交互是否触发
    protected virtual void LateUpdate()
    {
        isFocusedReadout = IsFocused;
    }

    public bool CanBeUsedBy(InteractorType who)
    {
        if (!IsInteractable) return false;
        switch (access)
        {
            case InteractAccess.Both: return true;
            case InteractAccess.PlayerOnly: return who == InteractorType.Player;
            case InteractAccess.RobotOnly: return who == InteractorType.Robot;
            default: return false;
        }
    }

    public virtual void OnFocusEnter(Interactor interactor)
    {
		
        IsFocused = true;
		// icon.setActive() = True;
        CurrentInteractor = interactor;
    }

    public virtual void OnFocusExit(Interactor interactor)
    {
		// icon.setActive() = false 
        ClearFocus();
    }

    public virtual void OnInteract(Interactor interactor) { }

    private void ClearFocus()
    {
        IsFocused = false;
        CurrentInteractor = null;
    }
}