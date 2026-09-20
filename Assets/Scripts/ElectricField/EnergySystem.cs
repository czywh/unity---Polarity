using UnityEngine;

/// <summary>
/// CORE-06 - Robot energy system (consumption expressed via color for now).
///
/// Full = fullColor (FA741E orange), empty = emptyColor (gray), lerps between the two by energy ratio.
/// Exposes Drain (consume energy, called by the emitter) / Recharge (called by the charging dock) / state queries.
/// Doesn't drain on its own -- how much and when is driven by RobotFieldEmitter etc.
/// </summary>
[DisallowMultipleComponent]
public class EnergySystem : MonoBehaviour
{
    [Header("Energy")]
    public float maxEnergy = 100f;
    [Tooltip("Initial energy, full by default")]
    public float currentEnergy = 100f;

    [Header("Color Feedback (fade)")]
    [Tooltip("Renderers colored by energy; empty = auto-collect those on the robot")]
    [SerializeField] private Renderer[] renderers;
    [Tooltip("Full-energy color (FA741E)")]
    public Color fullColor = new Color(0.9804f, 0.4549f, 0.1176f, 1f);
    [Tooltip("Empty-energy color (gray)")]
    public Color emptyColor = new Color(0.45f, 0.45f, 0.45f, 1f);

    // -- Public read-only state --
    public float CurrentEnergy => currentEnergy;
    public float MaxEnergy => maxEnergy;
    public float Fraction => maxEnergy > 0f ? Mathf.Clamp01(currentEnergy / maxEnergy) : 0f;
    public bool HasEnergy => currentEnergy > 0f;
    public bool IsEmpty => currentEnergy <= 0f;

    /// Fired once at the moment energy runs out
    public event System.Action Depleted;

    /// <summary>Total energy consumed this run (only increases; recharging doesn't offset it). Used by the results screen</summary>
    public float TotalDrained { get; private set; }
    /// <summary>Total energy recharged this run</summary>
    public float TotalRecharged { get; private set; }

    private MaterialPropertyBlock mpb;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP
    private static readonly int ColorId = Shader.PropertyToID("_Color");         // Built-in pipeline

    private void Awake()
    {
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(true);
        mpb = new MaterialPropertyBlock();
        currentEnergy = Mathf.Clamp(currentEnergy, 0f, maxEnergy);
    }

    private void Start()
    {
        ApplyColor();
    }

    /// Consume energy (emitter calls drainPerSecond * dt every frame)
    public void Drain(float amount)
    {
        if (amount <= 0f || currentEnergy <= 0f) return;
        float prev = currentEnergy;
        currentEnergy = Mathf.Max(0f, currentEnergy - amount);
        TotalDrained += prev - currentEnergy;          // Only count what was actually deducted
        if (!Mathf.Approximately(prev, currentEnergy)) ApplyColor();
        if (currentEnergy <= 0f && prev > 0f) Depleted?.Invoke();
    }

    /// Recharge (used by the charging dock)
    public void Recharge(float amount)
    {
        if (amount <= 0f) return;
        float prev = currentEnergy;
        currentEnergy = Mathf.Min(maxEnergy, currentEnergy + amount);
        TotalRecharged += currentEnergy - prev;
        if (!Mathf.Approximately(prev, currentEnergy)) ApplyColor();
    }

    // Color between gray <-> orange by energy ratio
    private void ApplyColor()
    {
        if (renderers == null) return;
        Color c = Color.Lerp(emptyColor, fullColor, Fraction);
        foreach (var r in renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, c);
            mpb.SetColor(ColorId, c);
            r.SetPropertyBlock(mpb);
        }
    }
}