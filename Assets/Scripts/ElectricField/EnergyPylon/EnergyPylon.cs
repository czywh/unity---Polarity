using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// PROP Energy pylon: energy hub. Charges whenever any field covers it (robot E field and missile fields both count -- both are ElectricField).
///
/// It relays its energy [fraction] to one or more bound interactables (electric bridges etc.):
///   Pylon fraction f  →  each bound bridge's energy = f × that bridge's own maxEnergy
/// So each bridge still uses its own maxEnergy / interactThreshold and can activate at different charge levels in turn.
///
/// Bound bridges are automatically marked externallyDriven: they no longer charge/discharge themselves or respond to fields directly;
/// everything is relayed through the pylon. The player / robot only needs to act on the pylon.
/// </summary>
public class EnergyPylon : ElectricEntity
{
    [Header("Energy Pylon: Bound Interactables")]
    [Tooltip("Bound electric bridges / electric entities: driven by this pylon's energy fraction instead of charging/discharging themselves")]
    [SerializeField] private List<ElectricEntity> boundEntities = new List<ElectricEntity>();

    protected override void Awake()
    {
        base.Awake();
        applyColorGradient = false;   // Pylons never use the gradient (safety net for leftover true on existing instances)
        // Bound entities are driven by this pylon: mark externallyDriven so they skip their own charge/discharge / field response
        for (int i = 0; i < boundEntities.Count; i++)
            if (boundEntities[i] != null && boundEntities[i] != this)
                boundEntities[i].externallyDriven = true;
    }

    protected override void Start()
    {
        base.Start();
        PushToBound();   // Sync once initially
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();   // Energy mirror debug fields
        PushToBound();       // Push this pylon's energy fraction to all bound entities
    }

    private void PushToBound()
    {
        float f = Fraction;
        for (int i = 0; i < boundEntities.Count; i++)
            if (boundEntities[i] != null && boundEntities[i] != this)
                boundEntities[i].SetEnergyFraction(f);
    }

    private void Reset()
    {
        energyMode = ElectricEnergyMode.ChargeOnly;    // Charged by fields (E field / missile field)
        outsideBehavior = OutsideBehavior.None;        // By default keep energy after leaving
        interactThreshold = 0f;                        // Always "interactable" → stable collider, easier coverage checks
        keepAimableWhenPassable = true;
        initialEnergy = 0f;
        chargeRate = 50f;
        applyColorGradient = false;                    // Pylons don't need a color gradient; keep own material
    }
}