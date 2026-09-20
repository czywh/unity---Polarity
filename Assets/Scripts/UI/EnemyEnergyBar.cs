using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Enemy overhead energy bar (world space). Reads the EnergySystem on the same object/parent, follows the head, faces the camera.
/// Fill / color / label behavior matches the pooled EntityEnergyBar so they look the same;
/// the only differences: the data source is EnergySystem (not ElectricEntity), and it is [always shown in Operation Mode].
/// </summary>
[DisallowMultipleComponent]
public class EnemyEnergyBar : MonoBehaviour
{
    [Header("Data Source")]
    [Tooltip("The enemy's energy; empty = use the parent's EnergySystem")]
    public EnergySystem energy;

    [Header("Fill Images (Image Type = Filled / Horizontal / Left)")]
    public Image frontFill;
    public Image bufferFill;
    [Tooltip("For fade in/out; empty = use this object's CanvasGroup")]
    public CanvasGroup group;
    [Tooltip("Value label (optional, shows current/max)")]
    public TMP_Text label;

    [Header("Facing")]
    public bool billboard = true;
    [Tooltip("Camera; empty = Camera.main")]
    public Camera cam;

    [Header("Display Rules")]
    [Tooltip("Always shown in Operation Mode (no need to be near)")]
    public bool alwaysInOperationMode = true;
    [Tooltip("Whether to also show outside Operation Mode")]
    public bool showOutsideOperationMode = false;
    [Tooltip("Whether to still show an empty bar when out of energy")]
    public bool showWhenEmpty = true;

    [Header("Smoothing (same as pooled bars)")]
    public float fastSpeed = 8f;
    public float slowSpeed = 1.5f;
    public float fadeSpeed = 10f;

    [Header("Color (same as pooled bar interactiveColor)")]
    public Color frontColor = new Color(0.66f, 0.30f, 0.95f);

    private float frontValue, bufferValue;
    private OperationModeController opMode;

    private void Awake()
    {
        if (energy == null) energy = GetComponentInParent<EnergySystem>();
        if (group == null) group = GetComponent<CanvasGroup>();
        if (cam == null) cam = Camera.main;
        opMode = FindFirstObjectByType<OperationModeController>();

        float f = energy != null ? energy.Fraction : 1f;
        frontValue = bufferValue = f;
        if (group != null) group.alpha = 0f;

        SetupFill(frontFill);
        SetupFill(bufferFill);
    }

    private static void SetupFill(Image img)
    {
        if (img == null) return;
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = (int)Image.OriginHorizontal.Left;
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (cam == null) cam = Camera.main;

        // -- Visibility --
        bool inOp = opMode != null && opMode.InOperationMode;
        bool show = inOp ? alwaysInOperationMode : showOutsideOperationMode;
        if (energy == null) show = false;
        else if (energy.IsEmpty && !showWhenEmpty) show = false;

        if (group != null)
            group.alpha = Mathf.MoveTowards(group.alpha, show ? 1f : 0f, fadeSpeed * dt);

        bool visible = group == null || group.alpha > 0.01f;
        if (!visible) return;

        // -- Buffered fill (identical to EntityEnergyBar) --
        if (energy != null)
        {
            float t = energy.Fraction;

            float fS = (t < frontValue ? fastSpeed : slowSpeed);
            frontValue = Mathf.MoveTowards(frontValue, t, fS * dt);

            float bS = (t > bufferValue ? fastSpeed : slowSpeed);
            bufferValue = Mathf.MoveTowards(bufferValue, t, bS * dt);
            bufferValue = Mathf.Max(bufferValue, frontValue);   // buffer never below front

            if (frontFill != null)
            {
                frontFill.fillAmount = frontValue;
                frontFill.color = frontColor;
            }
            if (bufferFill != null) bufferFill.fillAmount = bufferValue;
            if (label != null)
                label.text = $"{Mathf.RoundToInt(energy.CurrentEnergy)} / {Mathf.RoundToInt(energy.MaxEnergy)}";
        }

        // -- Face the camera --
        if (billboard && cam != null)
        {
            Vector3 dir = transform.position - cam.transform.position;
            if (dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }
    }
}