using UnityEngine;

/// <summary>
/// PROP-01 / ROBOT-08 Charging dock: inherits InteractableBase. RobotOnly by default.
///
/// Normal mode: robot approaches + camera aims at it (IsFocused), press F -> OnInteract -> start charging (auto fills up);
///          losing focus interrupts it; with instantFull checked, one press fills instantly.
/// Operation Mode: Interactor is frozen and the camera is an orthographic top-down view, so RobotConsoleMover uses a pure distance check
///          and calls ChargeDirect() to charge directly (see the public method below).
/// </summary>
public class ChargingDock : InteractableBase
{
    [Header("Charging")]
    [Tooltip("Speed of auto-charging to full after pressing interact (energy per second); ignored when Instant Full is checked")]
    public float rechargeRate = 25f;
    [Tooltip("Checked: one press fills instantly; unchecked: one press then auto-charges to full at rechargeRate")]
    public bool instantFull = false;

    [Header("Debug (read-only at runtime)")]
    [SerializeField] private bool charging;        // Whether currently auto-charging
    [SerializeField] private float robotEnergy01;  // Energy fraction of the robot currently charging

    protected virtual void Reset()
    {
        access = InteractAccess.RobotOnly;
        interactVerb = "Charge";
    }

    protected virtual void Update()
    {
        // Leaving the charging dock (lost focus) -> interrupt this charge
        if (!IsFocused) { charging = false; robotEnergy01 = 0f; return; }

        var energy = GetEnergy();
        robotEnergy01 = energy != null ? energy.Fraction : 0f;

        if (!charging || energy == null) return;

        if (energy.Fraction >= 1f) { charging = false; return; }
        energy.Recharge(rechargeRate * Time.deltaTime);
    }

    // Normal mode: press interact (F) -> start charging / fill instantly
    public override void OnInteract(Interactor interactor)
    {
        var energy = GetEnergy();
        if (energy == null) return;

        if (instantFull) energy.Recharge(energy.MaxEnergy);
        else charging = true;
    }

    /// <summary>
    /// Direct charging for Operation Mode etc. (bypasses Interactor focus). Call once per frame;
    /// with instantFull it fills in one go and returns false; otherwise charges at rechargeRate, returns false when full, else true.
    /// </summary>
    public bool ChargeDirect(EnergySystem energy)
    {
        if (energy == null) return false;
        robotEnergy01 = energy.Fraction;

        if (instantFull) { energy.Recharge(energy.MaxEnergy); return false; }
        if (energy.Fraction >= 1f) return false;

        energy.Recharge(rechargeRate * Time.deltaTime);
        return true;
    }

    private EnergySystem GetEnergy()
    {
        return CurrentInteractor != null
            ? CurrentInteractor.GetComponentInParent<EnergySystem>()
            : null;
    }
}