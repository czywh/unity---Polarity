using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// PROP　激光塔开关：玩家靠近 + 对准后按 F 开 / 关激光塔，可反复切换。
///
/// 提示文案随状态切换（InteractionPrompt 直出 InteractVerb 原文，所以这里存整句）：
///   激光开着 → Press "F" to shut down LaserTower
///   激光关了 → Press "F" to open LaserTower
///
/// 两次切换之间强制最短间隔 toggleCooldown（默认 0.3 秒），避免连点导致
/// LaserTower 反复 Destroy / Instantiate 激光实例。
///
/// 用法：挂在控制台 / 闸刀模型上，把要控制的 LaserTower 拖进 towers。
/// 留空则自动取本物体或父级上的 LaserTower。
/// </summary>
public class LaserTowerSwitch : InteractableBase
{
    [Header("控制的激光塔（可多个：一个闸刀关一组塔）")]
    [Tooltip("留空则自动取本物体 / 父级 / 子级上的 LaserTower")]
    [SerializeField] private List<LaserTower> towers = new List<LaserTower>();

    [Header("提示文案（整句，会原样显示）")]
    [Tooltip("激光开着时显示——按下即关闭")]
    public string verbWhenOn = "Press \"F\" to shut down LaserTower";
    [Tooltip("激光关闭时显示——按下即开启")]
    public string verbWhenOff = "Press \"F\" to open LaserTower";

    [Header("切换限制")]
    [Tooltip("两次切换之间的最短间隔（秒），防连点")]
    public float toggleCooldown = 0.3f;

    [Header("初始状态")]
    [Tooltip("开局激光是否处于开启状态")]
    public bool startOn = true;

    [Header("事件（接灯光 / 音效 / 动画）")]
    public UnityEvent onTurnedOn;
    public UnityEvent onTurnedOff;

    [Header("调试（运行时只读）")]
    [SerializeField] private bool isOn = true;
    [Tooltip("距离下次可切换还剩多少秒")]
    [SerializeField] private float cooldownRemaining;

    private float nextToggleTime;

    /// 激光当前是否开启
    public bool IsOn => isOn;
    /// 现在是否处于冷却中（按 F 无效）
    public bool OnCooldown => Time.time < nextToggleTime;

    protected virtual void Reset()
    {
        access = InteractAccess.PlayerOnly;   // 玩家专属开关
        interactVerb = verbWhenOn;
    }

    protected virtual void Awake()
    {
        CollectTowers();

        // 接管开局状态：把塔自己的 fireOnStart 关掉，统一由本开关的 startOn 决定。
        // Awake 一定早于任何 Start，所以塔的 Start 里不会再自行 Fire()，
        // 不用担心两者的 Start 执行顺序。
        for (int i = 0; i < towers.Count; i++)
            if (towers[i] != null) towers[i].fireOnStart = false;
    }

    protected virtual void Start()
    {
        ApplyState(startOn, fireEvents: false);
        nextToggleTime = 0f;
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();   // 基类镜像 IsFocused 到调试字段
        cooldownRemaining = Mathf.Max(0f, nextToggleTime - Time.time);
    }

    // 玩家按下 F
    public override void OnInteract(Interactor interactor)
    {
        if (OnCooldown) return;      // 冷却中：这次按键直接吞掉
        nextToggleTime = Time.time + toggleCooldown;
        ApplyState(!isOn, fireEvents: true);
    }

    /// <summary>外部也可直接调用（关卡脚本 / 电量机关联动）。会跳过冷却。</summary>
    public void SetOn(bool on) => ApplyState(on, fireEvents: true);

    private void ApplyState(bool on, bool fireEvents)
    {
        isOn = on;

        for (int i = 0; i < towers.Count; i++)
        {
            if (towers[i] == null) continue;
            towers[i].SetFiring(on);
        }

        // 提示文案跟着状态走（InteractionPrompt 每帧读 InteractVerb，改了即时生效）
        interactVerb = on ? verbWhenOn : verbWhenOff;

        if (fireEvents)
        {
            if (on) onTurnedOn?.Invoke();
            else onTurnedOff?.Invoke();
        }
    }

    private void CollectTowers()
    {
        towers.RemoveAll(t => t == null);
        if (towers.Count > 0) return;

        var found = GetComponentInParent<LaserTower>();
        if (found != null) { towers.Add(found); return; }

        towers.AddRange(GetComponentsInChildren<LaserTower>(true));
        if (towers.Count == 0)
            Debug.LogWarning("[LaserTowerSwitch] 没有找到要控制的 LaserTower，请手动拖进 towers", this);
    }

    private void OnValidate()
    {
        // 编辑期改文案时，Inspector 上的 interactVerb 同步预览
        if (!Application.isPlaying) interactVerb = startOn ? verbWhenOn : verbWhenOff;
    }
}