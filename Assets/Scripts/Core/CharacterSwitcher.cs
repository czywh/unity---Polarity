using UnityEngine;

/// <summary>
/// CORE-02 Character switcher. Press Q to switch between "player" and "robot":
///  (1) control (enable / disable each one's control scripts)
///  (2) camera focus (OrbitFollowCamera.SetTarget)
///  (3) cursor
///
/// This class is [the sole authority over control]: who can move is decided only here, from "current character + dying or not + externally frozen or not".
/// Death/respawn flows must not toggle control scripts themselves; instead call RefreshControlState() so this class re-decides.
/// For cases that need "freeze everyone" such as Operation Mode, use SetExternallyFrozen(true) so a character dying meanwhile doesn't wrongly re-enable control.
/// </summary>
public class CharacterSwitcher : MonoBehaviour
{
    public enum Character { Player, Robot }

    [Header("Control Scripts (enabled / disabled on switch)")]
    [Tooltip("Scripts that control the player, e.g. PlayerController, PlayerAimController")]
    public MonoBehaviour[] playerControlScripts;
    [Tooltip("Scripts that control the robot, e.g. RobotController, RobotAimController")]
    public MonoBehaviour[] robotControlScripts;

    [Header("Camera")]
    public OrbitFollowCamera cameraController;
    public Transform playerFocus;
    public Transform robotFocus;

    [Header("Input")]
    public KeyCode switchKey = KeyCode.Q;

    [Header("Cursor")]
    [Tooltip("Whether to lock and hide the cursor while controlling a character")]
    public bool lockCursorForPlayer = true;

    [Header("Death Link (optional: only when set does it avoid wrongly enabling control while dying)")]
    public CharacterDeathHandler playerDeathHandler;
    public CharacterDeathHandler robotDeathHandler;

    public Character Current { get; private set; } = Character.Player;

    /// <summary>
    /// External freeze (e.g. Operation Mode): when true, RefreshControlState / Apply always disable all control,
    /// regardless of "current character / dying state". Prevents a death in Operation Mode from triggering RefreshControlState and wrongly enabling control.
    /// </summary>
    public bool ExternallyFrozen { get; private set; }

    /// <summary>Set the external freeze state (called by OperationModeController).</summary>
    public void SetExternallyFrozen(bool frozen)
    {
        ExternallyFrozen = frozen;
        RefreshControlState();   // Re-decide immediately with the new state
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
    /// Death handlers auto-register here, so references don't need to be dragged into the two slots manually.
    /// </summary>
    public void RegisterDeathHandler(bool isPlayer, CharacterDeathHandler handler)
    {
        if (handler == null) return;
        if (isPlayer) playerDeathHandler = handler;
        else robotDeathHandler = handler;
    }

    /// <summary>
    /// Re-decide control based on [current character + dying state + external freeze].
    /// </summary>
    public void RefreshControlState()
    {
        // External freeze such as Operation Mode: disable all control, ignoring current character / dying state
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

        // (1) Control handoff (always disabled when externally frozen; otherwise consider dying state)
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

        // (2) Camera focus
        Transform focus = isPlayer ? playerFocus : robotFocus;
        if (cameraController != null && focus != null)
            cameraController.SetTarget(focus, instant);
        else
            Debug.LogWarning("CharacterSwitcher: cameraController or the matching focus is not assigned", this);

        // (3) Cursor
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