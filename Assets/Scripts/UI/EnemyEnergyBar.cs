using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 敌人头顶电量条（世界空间）。读同层/父级的 EnergySystem，跟随头顶、面向相机。
/// 填充 / 颜色 / label 行为对齐池子的 EntityEnergyBar，外观一致；
/// 差异只在：数据源是 EnergySystem（不是 ElectricEntity），且【操作模式下常驻显示】。
/// </summary>
[DisallowMultipleComponent]
public class EnemyEnergyBar : MonoBehaviour
{
    [Header("数据源")]
    [Tooltip("敌人的电量；留空取父级的 EnergySystem")]
    public EnergySystem energy;

    [Header("填充图（Image Type = Filled / Horizontal / Left）")]
    public Image frontFill;
    public Image bufferFill;
    [Tooltip("淡入淡出用；留空取本物体的 CanvasGroup")]
    public CanvasGroup group;
    [Tooltip("数值 label（可选，显示 当前/最大）")]
    public TMP_Text label;

    [Header("朝向")]
    public bool billboard = true;
    [Tooltip("相机；留空取 Camera.main")]
    public Camera cam;

    [Header("显示规则")]
    [Tooltip("操作模式下一直显示（不需靠近）")]
    public bool alwaysInOperationMode = true;
    [Tooltip("非操作模式下是否也显示")]
    public bool showOutsideOperationMode = false;
    [Tooltip("没电时是否仍显示空条")]
    public bool showWhenEmpty = true;

    [Header("平滑（与池子条一致）")]
    public float fastSpeed = 8f;
    public float slowSpeed = 1.5f;
    public float fadeSpeed = 10f;

    [Header("颜色（与池子条 interactiveColor 一致）")]
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

        // —— 是否显示 ——
        bool inOp = opMode != null && opMode.InOperationMode;
        bool show = inOp ? alwaysInOperationMode : showOutsideOperationMode;
        if (energy == null) show = false;
        else if (energy.IsEmpty && !showWhenEmpty) show = false;

        if (group != null)
            group.alpha = Mathf.MoveTowards(group.alpha, show ? 1f : 0f, fadeSpeed * dt);

        bool visible = group == null || group.alpha > 0.01f;
        if (!visible) return;

        // —— 缓冲填充（与 EntityEnergyBar 完全一致）——
        if (energy != null)
        {
            float t = energy.Fraction;

            float fS = (t < frontValue ? fastSpeed : slowSpeed);
            frontValue = Mathf.MoveTowards(frontValue, t, fS * dt);

            float bS = (t > bufferValue ? fastSpeed : slowSpeed);
            bufferValue = Mathf.MoveTowards(bufferValue, t, bS * dt);
            bufferValue = Mathf.Max(bufferValue, frontValue);   // buffer 不低于 front

            if (frontFill != null)
            {
                frontFill.fillAmount = frontValue;
                frontFill.color = frontColor;
            }
            if (bufferFill != null) bufferFill.fillAmount = bufferValue;
            if (label != null)
                label.text = $"{Mathf.RoundToInt(energy.CurrentEnergy)} / {Mathf.RoundToInt(energy.MaxEnergy)}";
        }

        // —— 面向相机 ——
        if (billboard && cam != null)
        {
            Vector3 dir = transform.position - cam.transform.position;
            if (dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }
    }
}