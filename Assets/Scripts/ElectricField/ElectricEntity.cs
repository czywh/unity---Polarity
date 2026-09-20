using UnityEngine;

/// <summary>Charge / discharge mode: charge only, discharge only, or both.</summary>
public enum ElectricEnergyMode
{
    ChargeOnly,     // Charge only: charged by the robot's field; the player cannot discharge it
    DischargeOnly,  // Discharge only: the field does not charge it; only the player can discharge by aiming
    Both            // Both: robot charges + player discharges
}

/// <summary>Automatic behavior after leaving the field (pick one of three).</summary>
public enum OutsideBehavior
{
    None,        // Keep current energy after leaving
    Discharge,   // After leaving (and a short hold), discharge automatically
    SelfCharge   // After leaving (and a short hold), recharge automatically to full
}

/// <summary>
/// CORE-05 Electric entity base class (energy model).
///
/// Energy is in design units (range 0 ~ maxEnergy; typically maxEnergy~100, up to 1000).
/// Color and interactability are both driven by [energy fraction = CurrentEnergy / maxEnergy] (same for charging and discharging):
///   - CurrentEnergy < interactThreshold -> toward unchargedColor (gray): not interactable, collision off (passable)
///   - CurrentEnergy >= interactThreshold -> toward chargedColor (purple): interactable, collision on (standable / blocking)
///
/// Energy sources by mode:
///   - Charging: energy rises while inside the robot's field (ChargeOnly / Both only)
///   - Discharging: player aims and holds left mouse to call Discharge(), lowering energy (DischargeOnly / Both only)
/// </summary>
[DisallowMultipleComponent]
public abstract class ElectricEntity : MonoBehaviour
{
    [Header("Controlled Parts (auto-collected if empty)")]
    [SerializeField] protected Renderer[] renderers;
    [SerializeField] protected Collider[] colliders;

    [Header("Energy (design units, range 0~1000, typically around 100)")]
    [Range(0f, 1000f)]
    [Tooltip("Maximum energy of this object")]
    public float maxEnergy = 100f;
    [Tooltip("Minimum energy: discharging stops here (0 = can be fully drained)")]
    public float minEnergy = 0f;
    [Tooltip("Initial energy. Discharge-only blockers are usually set to = maxEnergy (full at start, interactable)")]
    public float initialEnergy = 0f;
    [Tooltip("Interact threshold (design units). E.g. 50 when maxEnergy=100")]
    public float interactThreshold = 50f;

    [Header("Mode")]
    [Tooltip("Charge only / Discharge only / Both")]
    public ElectricEnergyMode energyMode = ElectricEnergyMode.ChargeOnly;

    [Header("External Drive (Energy Pylon)")]
    [Tooltip("Set to true automatically when bound to an energy pylon: energy is then driven by the pylon's fraction; this object no longer charges/discharges itself or responds to fields / direct discharge")]
    public bool externallyDriven = false;

    [Header("Charging (Robot Field)")]
    [Tooltip("Energy gained per second inside the field (design units/sec)")]
    public float chargeRate = 50f;

    [Header("Auto Behavior After Leaving")]
    [Tooltip("After leaving the field: None = keep / Discharge = auto discharge / SelfCharge = auto refill")]
    public OutsideBehavior outsideBehavior = OutsideBehavior.Discharge;
    [Tooltip("Seconds to hold current energy after leaving before auto discharge / auto recharge starts")]
    public float holdAfterLeaving = 5f;
    [Tooltip("Auto discharge per second after leaving (used by Discharge)")]
    public float outsideDischargeRate = 100f;
    [Tooltip("Auto recharge per second after leaving (used by SelfCharge)")]
    public float selfChargeRate = 50f;

    [Header("Collision Toggle Mode")]
    [Tooltip("Checked: when not interactable, set colliders to Trigger (passable but still hit by the aim ray, so it can be re-selected to keep draining).\nUnchecked: when not interactable, disable colliders entirely (the ray can't hit them either)")]
    public bool keepAimableWhenPassable = true;
    [Tooltip("Whether colliders toggle with the interactable state. Set false for moving obstacles: colliders stay solid and position expresses the state")]
    public bool colliderFollowsInteractive = true;

    [Header("Color (gradient by energy fraction)")]
    [Tooltip("Whether to tint by energy gradient; turn off for pure hubs like energy pylons to keep their own material look")]
    public bool applyColorGradient = true;
    [Tooltip("Low-energy color (gray): not interactable")]
    public Color unchargedColor = new Color(0.5f, 0.5f, 0.5f, 1f);
    [Tooltip("Full-energy color (purple): interactable")]
    public Color chargedColor = new Color(0.6f, 0.2f, 0.9f, 1f);

    [Header("Health Bar Position (world-following UI, adjustable per object)")]
    [Tooltip("Explicit anchor: drag an empty child here and the bar follows it (most flexible). If empty, the ratio below positions it on the bounding box")]
    [SerializeField] private Transform barAnchor;
    [Range(0f, 1f)]
    [Tooltip("Without an explicit anchor, the bar's position along the bounding box height: 0=bottom, 0.5=center, 1=top")]
    [SerializeField] private float barVertical = 1f;
    [Tooltip("Extra world offset added to the bar position (for fine-tuning)")]
    [SerializeField] private Vector3 barWorldOffset = Vector3.zero;

    [Header("Debug (read-only at runtime)")]
    [Tooltip("Current energy (mirrors CurrentEnergy, for watching charge/discharge)")]
    [SerializeField] private float currentEnergyReadout;
    [Tooltip("Whether currently interactable (energy >= threshold)")]
    [SerializeField] private bool interactiveReadout;

    // -- Public read-only state --
    public float CurrentEnergy { get; private set; }
    public float MaxEnergy => maxEnergy;
    public float Fraction => maxEnergy > 0f ? Mathf.Clamp01(CurrentEnergy / maxEnergy) : 0f;  // 0..1 fraction
    public bool IsInteractive { get; private set; }
    public bool CanCharge => energyMode == ElectricEnergyMode.ChargeOnly || energyMode == ElectricEnergyMode.Both;
    public bool CanDischarge => energyMode == ElectricEnergyMode.DischargeOnly || energyMode == ElectricEnergyMode.Both;

    // -- Health bar position (read by world-following UI) --
    public Transform BarAnchor => barAnchor;
    public float BarVertical => barVertical;
    public Vector3 BarWorldOffset => barWorldOffset;

    private float leaveHoldTimer;
    private MaterialPropertyBlock mpb;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP
    private static readonly int ColorId = Shader.PropertyToID("_Color");         // Built-in pipeline

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
        if (externallyDriven) return;   // Energy driven by a pylon; skip own charge/discharge logic

        // Charging in field: only objects that "allow charging" (ChargeOnly / Both) gain energy inside a field
        bool fieldCharging = CanCharge && IsCovered();
        float prev = CurrentEnergy;

        if (fieldCharging)
        {
            CurrentEnergy += chargeRate * Time.deltaTime;
            leaveHoldTimer = holdAfterLeaving;   // Being charged -> reset the leave timer
        }
        else
        {
            // Not being charged by a field -> post-leave auto behavior (hold for a while, then run)
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
                        CurrentEnergy += selfChargeRate * Time.deltaTime;   // Self-heal to full
                        break;
                    // None: do nothing
                }
            }
        }

        CurrentEnergy = Mathf.Clamp(CurrentEnergy, minEnergy, maxEnergy);

        if (!Mathf.Approximately(CurrentEnergy, prev)) ApplyColor();

        bool shouldInteract = CurrentEnergy >= interactThreshold;
        if (shouldInteract != IsInteractive) SetInteractive(shouldInteract, false);
    }

    /// Player discharge: called every frame while aiming + holding left mouse; amount is in design units. Only works for DischargeOnly / Both.
    public void Discharge(float amount)
    {
        if (externallyDriven) return;   // Driven by a pylon; ignore direct discharge (player should act on the pylon)
        if (!CanDischarge || amount <= 0f) return;

        leaveHoldTimer = holdAfterLeaving;   // Every discharge resets the leave timer -> self-heal only after the player stops for a while

        float prev = CurrentEnergy;
        CurrentEnergy = Mathf.Clamp(CurrentEnergy - amount, minEnergy, maxEnergy);
        if (Mathf.Approximately(CurrentEnergy, prev)) return;

        ApplyColor();
        bool shouldInteract = CurrentEnergy >= interactThreshold;
        if (shouldInteract != IsInteractive) SetInteractive(shouldInteract, false);
    }

    /// <summary>
    /// Environmental energy drain (lasers etc.), a separate action from player discharge:
    ///   - Discharge() is limited by energyMode, representing the "player actively drains" gameplay permission;
    ///   - DrainExternal() ignores energyMode, representing "damaged by the environment",
    ///     so even a ChargeOnly pylon can be drained by a laser -- the player can't, but a laser can.
    ///
    /// Also resets the leave timer, so objects hit by a laser don't self-heal while losing energy.
    /// externallyDriven (bridges whose energy is driven by a pylon) ignore this call: their energy gets
    /// overwritten by the pylon next frame, so draining is pointless -- hit the pylon itself instead.
    /// </summary>
    public void DrainExternal(float amount)
    {
        if (externallyDriven || amount <= 0f) return;

        leaveHoldTimer = holdAfterLeaving;   // Being damaged -> delay post-leave self-heal

        float prev = CurrentEnergy;
        CurrentEnergy = Mathf.Clamp(CurrentEnergy - amount, minEnergy, maxEnergy);
        if (Mathf.Approximately(CurrentEnergy, prev)) return;

        ApplyColor();
        bool shouldInteract = CurrentEnergy >= interactThreshold;
        if (shouldInteract != IsInteractive) SetInteractive(shouldInteract, false);
    }

    /// Called by energy pylons: set this object's energy by fraction (0..1) and refresh color / interactable state.
    /// Energy = fraction x this object's maxEnergy, so each bridge still uses its own maxEnergy / interactThreshold.
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

    // Mirror state to visible fields (refreshed every frame in every mode, for Inspector watching)
    protected virtual void LateUpdate()
    {
        currentEnergyReadout = CurrentEnergy;
        interactiveReadout = IsInteractive;
    }

    /// Whether covered by any field: prefer exact [collider shape x field sphere] intersection (only counts when the sphere actually touches the bridge);
    /// falls back to bounds when no usable collider. Charging and the UI bar share this same check.
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
                if (mgr.IsColliderInAnyField(c)) return true;   // Sphere actually intersects the collider
            }
        }
        if (hasCollider) return false;   // Has colliders but none intersect

        return mgr.IsBoundsInAnyField(WorldBounds);   // Fallback: use bounds when there is no collider
    }

    /// Combined world bounds of own renderers (falls back to a small box at own position if no renderer)
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

    // Energy visuals (charging / discharging / external drive all go through here); subclasses may override with other visuals (e.g. transparency)
    protected virtual void ApplyColor()
    {
        if (!applyColorGradient) return;   // Gradient off (e.g. pylons): keep own material look
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

    // Interactable -> solid collision (standable / blocking); not interactable -> depends on option:
    //   keepAimableWhenPassable=true  -> becomes Trigger (passable, but the ray still hits it so it can be re-selected for draining)
    //   keepAimableWhenPassable=false -> disable colliders entirely
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
                    col.enabled = true;              // Always enabled so the ray can hit it
                    col.isTrigger = !interactive;    // Trigger when not interactable: passable but hittable
                }
                else
                {
                    col.isTrigger = false;
                    col.enabled = interactive;       // Old behavior: disable collision when not interactable
                }
            }
        }
        OnInteractiveChanged(interactive);
    }

    /// Subclass hook: extra feedback when the interactable state toggles (sound / particles etc.)
    protected virtual void OnInteractiveChanged(bool interactive) { }
}