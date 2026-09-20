using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI-01 Robot energy bar: buffered health-style bar pinned to the top-left of the screen + low-energy warning.
///
/// Hierarchy (under Canvas, bottom to top = draw order):
///   EnergyBar_BG  -- background track (dark, empty-bar background)
///     ├─ BufferFill -- buffer layer (light, follows slowly, handles "ghost trail / pre-fill")
///     └─ FrontFill  -- main layer (orange, follows fast, shows real energy, drawn on top)
///
/// Buffer logic: main/buffer layers each use the fast speed in the "leading direction" and the slow speed in the "catching-up direction",
///          always keeping buffer >= front; losing energy reveals a ghost trail, charging pre-fills in light color first.
///
/// Low-energy warning: below the threshold, the main layer gets redder and blinks faster the "deeper it sinks";
///            blinking is done by brightening (alpha always 1), to avoid showing the buffer layer behind.
/// </summary>
[DisallowMultipleComponent]
public class EnergyBarUI : MonoBehaviour
{
    [Header("Data Source")]
    [Tooltip("The robot's EnergySystem; empty = auto-find the first one in the scene at runtime")]
    [SerializeField] private EnergySystem energy;

    [Header("Fill Images (Filled / Horizontal / Left)")]
    [Tooltip("Orange main layer")]
    [SerializeField] private Image frontFill;
    [Tooltip("Light buffer layer, drawn behind the main layer")]
    [SerializeField] private Image bufferFill;

    [Header("Speed (fill fraction per second)")]
    [Tooltip("Leading-edge speed: front when losing energy / buffer when charging")]
    [SerializeField] private float fastSpeed = 8f;
    [Tooltip("Catch-up edge speed: buffer when losing energy / front when charging. Smaller = ghost trail lingers longer")]
    [SerializeField] private float slowSpeed = 1.5f;

    [Header("Buffer Layer Tint (optional)")]
    [Tooltip("When enabled, the buffer layer uses different colors for draining and charging")]
    [SerializeField] private bool tintBufferByDirection = false;
    [SerializeField] private Color drainColor = new Color(1f, 0.92f, 0.80f);
    [SerializeField] private Color chargeColor = new Color(0.65f, 1f, 0.75f);

    [Header("Low-Energy Warning")]
    [Tooltip("Master toggle")]
    [SerializeField] private bool enableLowWarning = true;
    [Range(0f, 1f)]
    [Tooltip("Start warning when energy is below this fraction (0.2 = 20%)")]
    [SerializeField] private float lowThreshold = 0.2f;
    [Tooltip("Warning color at the deepest point (near empty)")]
    [SerializeField] private Color warningColor = new Color(0.89f, 0.23f, 0.18f);
    [Tooltip("Blink frequency when just entering the warning zone")]
    [SerializeField] private float minPulseSpeed = 2f;
    [Tooltip("Blink frequency when near empty")]
    [SerializeField] private float maxPulseSpeed = 8f;
    [Range(0f, 1f)]
    [Tooltip("How much the pulse brightens toward white; larger = harsher flashing")]
    [SerializeField] private float pulseStrength = 0.55f;

    [Header("Value Label (optional)")]
    [Tooltip("Text showing the energy number; can be left empty")]
    [SerializeField] private TMP_Text label;
    [Tooltip("Percent = percentage (73%); Value = current/max (73 / 100)")]
    [SerializeField] private LabelMode labelMode = LabelMode.Percent;

    public enum LabelMode { Percent, Value }

    private float frontValue;
    private float bufferValue;
    private float lastTarget;
    private Color normalColor = new Color(0.98f, 0.45f, 0.12f); // FA741E fallback

    private void Awake()
    {
        // If an older Unity version errors, replace FindFirstObjectByType with FindObjectOfType
        if (energy == null)
            energy = FindFirstObjectByType<EnergySystem>();

        SetupFillImage(frontFill);
        SetupFillImage(bufferFill);

        // Remember the normal color set in the Inspector; restore it after the warning ends
        if (frontFill != null) normalColor = frontFill.color;
    }

    private void Start()
    {
        float f = energy != null ? energy.Fraction : 1f;
        frontValue = bufferValue = lastTarget = f;
        Apply();
    }

    private void Update()
    {
        if (energy == null) return;

        float target = energy.Fraction;
        float dt = Time.deltaTime;

        // Main layer: down (draining) leads → fast, up (charging) catches up → slow
        float fSpeed = (target < frontValue ? fastSpeed : slowSpeed);
        frontValue = Mathf.MoveTowards(frontValue, target, fSpeed * dt);

        // Buffer layer: up (charging) leads → fast, down (draining) catches up → slow
        float bSpeed = (target > bufferValue ? fastSpeed : slowSpeed);
        bufferValue = Mathf.MoveTowards(bufferValue, target, bSpeed * dt);

        bufferValue = Mathf.Max(bufferValue, frontValue);

        if (tintBufferByDirection && bufferFill != null)
        {
            if (target < lastTarget - 0.0001f) bufferFill.color = drainColor;
            else if (target > lastTarget + 0.0001f) bufferFill.color = chargeColor;
        }
        lastTarget = target;

        Apply();
    }

    private void Apply()
    {
        if (frontFill != null) frontFill.fillAmount = frontValue;
        if (bufferFill != null) bufferFill.fillAmount = bufferValue;

        UpdateFrontColor();
        UpdateLabel();
    }

    // Value label: read CurrentEnergy / MaxEnergy directly from EnergySystem
    private void UpdateLabel()
    {
        if (label == null || energy == null) return;

        label.text = labelMode == LabelMode.Value
            ? $"{Mathf.RoundToInt(energy.CurrentEnergy)} / {Mathf.RoundToInt(energy.MaxEnergy)}"
            : $"{Mathf.RoundToInt(energy.Fraction * 100f)}%";
    }

    // Low energy: lower = redder and faster blinking; alpha always 1 to avoid showing the buffer layer behind
    private void UpdateFrontColor()
    {
        if (frontFill == null) return;

        if (!enableLowWarning || energy == null || lowThreshold <= 0f)
        {
            frontFill.color = normalColor;
            return;
        }

        float f = energy.Fraction;
        if (f > lowThreshold)
        {
            frontFill.color = normalColor;
            return;
        }

        // severity: 0 at threshold, 1 when empty
        float severity = 1f - Mathf.Clamp01(f / lowThreshold);

        // Base color: normal color → warning red
        Color baseC = Color.Lerp(normalColor, warningColor, severity);

        // Pulse: oscillate between base color and brightened version; frequency rises with severity
        float speed = Mathf.Lerp(minPulseSpeed, maxPulseSpeed, severity);
        float pulse = Mathf.Sin(Time.unscaledTime * speed) * 0.5f + 0.5f; // 0..1
        Color highlight = Color.Lerp(baseC, Color.white, pulseStrength);
        Color c = Color.Lerp(baseC, highlight, pulse);
        c.a = 1f;

        frontFill.color = c;
    }

    private static void SetupFillImage(Image img)
    {
        if (img == null) return;
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = (int)Image.OriginHorizontal.Left;
    }

    /// Called externally (e.g. to rebind after switching characters)
    public void SetEnergySource(EnergySystem source)
    {
        energy = source;
        float f = energy != null ? energy.Fraction : 0f;
        frontValue = bufferValue = lastTarget = f;
    }
}