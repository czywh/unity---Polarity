using UnityEngine;

/// <summary>
/// 交互者：挂在玩家 / 机器人身上。每帧从所有可交互物里挑出
/// 「在范围内 + 相机对准 + 权限匹配」的最近一个设为焦点，并在按键时触发交互。
///
/// 控制权仲裁：主动向 CharacterSwitcher 确认"我是不是当前被操控的角色"。
///   · 不是当前角色 → 立即清空焦点并停止工作
///   · ExternallyFrozen（操作模式等）→ 同样停止工作
/// 这样即使忘了把本组件加进 CharacterSwitcher 的控制脚本数组，也不会出现
/// 「切到玩家后，机器人那边的 Interactor 仍在给共用相机点亮交互提示」的问题。
/// 仍然建议照常加进数组（双保险，且能一并冻结移动等其它脚本）。
/// </summary>
public class Interactor : MonoBehaviour
{
    [Header("身份")]
    [Tooltip("这个交互者代表谁；决定能用哪些 RobotOnly / PlayerOnly 物体")]
    public InteractorType type = InteractorType.Player;

    [Header("控制权（留空自动查找场景中的 CharacterSwitcher）")]
    [Tooltip("控制权威。找不到时退回只看本组件的 enabled 状态")]
    [SerializeField] private CharacterSwitcher switcher;

    [Header("检测")]
    [Tooltip("距离参照点；留空用本物体")]
    public Transform origin;
    [Tooltip("判断对准用的相机；留空用 Camera.main")]
    public Camera cam;
    [Tooltip("交互所需的最大距离（角色到物体）")]
    public float interactRange = 3f;
    [Tooltip("是否要求相机对准物体")]
    public bool requireCameraAim = true;
    [Range(0f, 90f)]
    [Tooltip("相机前方与物体方向的最大夹角，越大越宽松")]
    public float aimAngle = 35f;

    [Header("按键交互")]
    public KeyCode interactKey = KeyCode.F;

    [Header("调试（运行时只读）")]
    [Tooltip("本交互者当前是否握有控制权（= 是否为被操控角色且未被外部冻结）")]
    [SerializeField] private bool hasControlReadout;
    [SerializeField] private string currentReadout;

    /// 当前聚焦的物体（无则 null）
    public InteractableBase Current { get; private set; }

    /// <summary>是否握有控制权：当前被操控的角色，且未被外部冻结</summary>
    public bool HasControl
    {
        get
        {
            if (!isActiveAndEnabled) return false;
            if (switcher == null) return true;               // 无仲裁者：退回旧行为
            if (switcher.ExternallyFrozen) return false;     // 操作模式等：全员停手

            return type == InteractorType.Player
                ? switcher.Current == CharacterSwitcher.Character.Player
                : switcher.Current == CharacterSwitcher.Character.Robot;
        }
    }

    private void Awake()
    {
        if (switcher == null) switcher = FindFirstObjectByType<CharacterSwitcher>();
    }

    private void Update()
    {
        hasControlReadout = HasControl;

        // 没有控制权 → 主动清空焦点（否则提示 UI 会读到残留的 Current）
        if (!hasControlReadout)
        {
            ClearCurrent();
            currentReadout = "(无控制权)";
            return;
        }

        InteractableBase best = FindBest();

        if (best != Current)
        {
            if (Current != null) Current.OnFocusExit(this);
            Current = best;
            if (Current != null) Current.OnFocusEnter(this);
        }

        currentReadout = Current != null ? Current.name : "(无)";

        if (Current != null && Input.GetKeyDown(interactKey))
            Current.OnInteract(this);
    }

    private InteractableBase FindBest()
    {
        Transform o = origin != null ? origin : transform;
        Camera c = cam != null ? cam : Camera.main;

        InteractableBase best = null;
        float bestDist = float.MaxValue;

        var list = InteractableBase.All;
        for (int i = 0; i < list.Count; i++)
        {
            var it = list[i];
            if (it == null || !it.CanBeUsedBy(type)) continue;

            Vector3 p = it.InteractTransform.position;
            float dist = Vector3.Distance(o.position, p);
            if (dist > interactRange) continue;

            // 相机对准判断：相机前方与"相机→物体"方向的夹角不超过 aimAngle
            if (requireCameraAim && c != null)
            {
                Vector3 toObj = p - c.transform.position;
                if (toObj.sqrMagnitude > 0.0001f &&
                    Vector3.Angle(c.transform.forward, toObj.normalized) > aimAngle)
                    continue;
            }

            if (dist < bestDist) { bestDist = dist; best = it; }
        }

        return best;
    }

    private void ClearCurrent()
    {
        if (Current == null) return;
        Current.OnFocusExit(this);
        Current = null;
    }

    private void OnDisable()
    {
        // 被禁用（如切走角色）时，主动解除当前焦点
        ClearCurrent();
        hasControlReadout = false;
    }

    private void OnDrawGizmosSelected()
    {
        Transform o = origin != null ? origin : transform;
        Gizmos.color = new Color(0.3f, 1f, 0.6f, 0.5f);
        Gizmos.DrawWireSphere(o.position, interactRange);
    }
}