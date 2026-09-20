using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// PROP - Laser tower switch: player approaches + aims, presses F to turn the laser tower on / off; can toggle repeatedly.
///
/// Prompt text changes with state (InteractionPrompt shows InteractVerb verbatim, so full sentences are stored here):
///   Laser on  -> Press "F" to shut down LaserTower
///   Laser off -> Press "F" to open LaserTower
///
/// A minimum interval toggleCooldown (default 0.3s) is enforced between toggles, to avoid rapid clicks making
/// LaserTower repeatedly Destroy / Instantiate laser instances.
///
/// Usage: attach to the console / lever model and drag the LaserTowers to control into towers.
/// If empty, LaserTower on this object or its parents is used automatically.
/// </summary>
public class LaserTowerSwitch : InteractableBase
{
    [Header("Controlled Laser Towers (multiple allowed: one lever turns off a group of towers)")]
    [Tooltip("If empty, LaserTower on this object / parent / children is used automatically")]
    [SerializeField] private List<LaserTower> towers = new List<LaserTower>();

    [Header("Prompt Text (full sentence, shown as-is)")]
    [Tooltip("Shown while the laser is on -- pressing turns it off")]
    public string verbWhenOn = "Press \"F\" to shut down LaserTower";
    [Tooltip("Shown while the laser is off -- pressing turns it on")]
    public string verbWhenOff = "Press \"F\" to open LaserTower";

    [Header("Toggle Limit")]
    [Tooltip("Minimum interval between toggles (seconds), prevents spam clicking")]
    public float toggleCooldown = 0.3f;

    [Header("Initial State")]
    [Tooltip("Whether the laser starts on")]
    public bool startOn = true;

    [Header("Events (hook up lights / SFX / animation)")]
    public UnityEvent onTurnedOn;
    public UnityEvent onTurnedOff;

    [Header("Debug (runtime, read-only)")]
    [SerializeField] private bool isOn = true;
    [Tooltip("Seconds remaining until the next toggle is allowed")]
    [SerializeField] private float cooldownRemaining;

    private float nextToggleTime;

    /// Whether the laser is currently on
    public bool IsOn => isOn;
    /// Whether currently on cooldown (pressing F has no effect)
    public bool OnCooldown => Time.time < nextToggleTime;

    protected virtual void Reset()
    {
        access = InteractAccess.PlayerOnly;   // Player-only switch
        interactVerb = verbWhenOn;
    }

    protected virtual void Awake()
    {
        CollectTowers();

        // Take over the initial state: turn off the tower's own fireOnStart, so this switch's startOn decides.
        // Awake always runs before any Start, so the tower's Start won't Fire() on its own,
        // no need to worry about the Start order between the two.
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
        base.LateUpdate();   // Base class mirrors IsFocused to the debug field
        cooldownRemaining = Mathf.Max(0f, nextToggleTime - Time.time);
    }

    // Player pressed F
    public override void OnInteract(Interactor interactor)
    {
        if (OnCooldown) return;      // On cooldown: swallow this key press
        nextToggleTime = Time.time + toggleCooldown;
        ApplyState(!isOn, fireEvents: true);
    }

    /// <summary>Can also be called externally (level scripts / energy mechanism links). Skips the cooldown.</summary>
    public void SetOn(bool on) => ApplyState(on, fireEvents: true);

    private void ApplyState(bool on, bool fireEvents)
    {
        isOn = on;

        for (int i = 0; i < towers.Count; i++)
        {
            if (towers[i] == null) continue;
            towers[i].SetFiring(on);
        }

        // Prompt text follows state (InteractionPrompt reads InteractVerb every frame, changes apply immediately)
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
            Debug.LogWarning("[LaserTowerSwitch] No LaserTower found to control; please drag them into towers manually", this);
    }

    private void OnValidate()
    {
        // When editing the text in the editor, sync interactVerb in the Inspector as a preview
        if (!Application.isPlaying) interactVerb = startOn ? verbWhenOn : verbWhenOff;
    }
}