using UnityEngine;

/// <summary>
/// CORE-02　角色切换器。按下 Q：在「玩家」与「机器人」之间切换
///  ① 控制权（启用 / 禁用各自的控制脚本）
///  ② 相机焦点（OrbitFollowCamera.SetTarget）
///  ③ 光标
///
/// 本类是【控制权的唯一裁决者】：谁能动，只由这里根据「当前角色 + 是否死亡中 + 是否外部冻结」决定。
/// 死亡/复活流程不要自己去开关控制脚本，而是调用 RefreshControlState() 让本类重新裁决。
/// 操作模式等需要"全员冻结"的场景，用 SetExternallyFrozen(true)，避免期间角色死亡触发误开控制。
/// </summary>
public class CharacterSwitcher : MonoBehaviour
{
    public enum Character { Player, Robot }

    [Header("控制脚本（切换时启用 / 禁用）")]
    [Tooltip("控制玩家的脚本，例如 PlayerController、PlayerAimController")]
    public MonoBehaviour[] playerControlScripts;
    [Tooltip("控制机器人的脚本，例如 RobotController、RobotAimController")]
    public MonoBehaviour[] robotControlScripts;

    [Header("相机")]
    public OrbitFollowCamera cameraController;
    public Transform playerFocus;
    public Transform robotFocus;

    [Header("输入")]
    public KeyCode switchKey = KeyCode.Q;

    [Header("光标")]
    [Tooltip("控制角色时是否锁定并隐藏光标")]
    public bool lockCursorForPlayer = true;

    [Header("死亡联动（可选：填了才会在死亡中避免误开控制）")]
    public CharacterDeathHandler playerDeathHandler;
    public CharacterDeathHandler robotDeathHandler;

    public Character Current { get; private set; } = Character.Player;

    /// <summary>
    /// 外部冻结（如操作模式）：为 true 时，RefreshControlState / Apply 一律禁用所有控制，
    /// 不受"当前角色 / 死亡态"影响。避免操作模式下角色死亡触发 RefreshControlState 误开控制。
    /// </summary>
    public bool ExternallyFrozen { get; private set; }

    /// <summary>设置外部冻结状态（由 OperationModeController 调用）。</summary>
    public void SetExternallyFrozen(bool frozen)
    {
        ExternallyFrozen = frozen;
        RefreshControlState();   // 立即按新状态重新裁决
    }

    void Start()
    {
        Apply(Character.Player, instant: true);
    }

    void Update()
    {
        if (Input.GetKeyDown(switchKey))
            Toggle();
    }

    public void Toggle()
    {
        SwitchTo(Current == Character.Player ? Character.Robot : Character.Player);
    }

    public void SwitchTo(Character target)
    {
        if (target == Current) return;
        Apply(target, instant: false);
        Current = target;
    }

    /// <summary>
    /// 死亡处理器自动注册到这里，免去手动往两个槽里拖引用。
    /// </summary>
    public void RegisterDeathHandler(bool isPlayer, CharacterDeathHandler handler)
    {
        if (handler == null) return;
        if (isPlayer) playerDeathHandler = handler;
        else robotDeathHandler = handler;
    }

    /// <summary>
    /// 按【当前角色 + 死亡态 + 外部冻结】重新裁决控制权。
    /// </summary>
    public void RefreshControlState()
    {
        // 操作模式等外部冻结：一律禁用所有控制，无视当前角色 / 死亡态
        if (ExternallyFrozen)
        {
            SetEnabled(playerControlScripts, false);
            SetEnabled(robotControlScripts, false);
            return;
        }

        bool isPlayer = (Current == Character.Player);
        SetEnabled(playerControlScripts, isPlayer && !IsDying(playerDeathHandler));
        SetEnabled(robotControlScripts, !isPlayer && !IsDying(robotDeathHandler));
    }

    private void Apply(Character target, bool instant)
    {
        bool isPlayer = (target == Character.Player);

        // ① 控制权交接（外部冻结时一律禁用；否则考虑死亡态）
        if (ExternallyFrozen)
        {
            SetEnabled(playerControlScripts, false);
            SetEnabled(robotControlScripts, false);
        }
        else
        {
            SetEnabled(playerControlScripts, isPlayer && !IsDying(playerDeathHandler));
            SetEnabled(robotControlScripts, !isPlayer && !IsDying(robotDeathHandler));
        }

        // ② 相机焦点
        Transform focus = isPlayer ? playerFocus : robotFocus;
        if (cameraController != null && focus != null)
            cameraController.SetTarget(focus, instant);
        else
            Debug.LogWarning("CharacterSwitcher: cameraController 或对应 focus 未指定", this);

        // ③ 光标
        if (lockCursorForPlayer)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private static bool IsDying(CharacterDeathHandler h) => h != null && h.IsDying;

    private static void SetEnabled(MonoBehaviour[] scripts, bool on)
    {
        if (scripts == null) return;
        foreach (var s in scripts)
            if (s != null) s.enabled = on;
    }
}