using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI-02 世界跟随电量条（被动版，池化）。数据源改为通用接口 IBarSource，
/// 于是同一套 prefab / 池子既能显示电子实体(ElectricEntity)，也能显示敌人(EnergySystem)。
///
/// 本条不自己决定显隐，由 EntityBarManager 通过 Assign / KeepShown / Hide 控制。
/// 自己只负责：跟随目标世界坐标、双层缓冲填充、阈值线、配色、淡入淡出。
/// 挂在 Overlay Canvas 下（通常作为 Prefab 被池化实例化）。
/// </summary>
[DisallowMultipleComponent]
public class EntityEnergyBar : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("世界→屏幕换算相机；留空取 Camera.main")]
    [SerializeField] private Camera cam;
    [SerializeField] private CanvasGroup group;
    [SerializeField] private RectTransform rect;
    [SerializeField] private Image frontFill;
    [SerializeField] private Image bufferFill;
    [Tooltip("阈值刻度竖线，可留空")]
    [SerializeField] private RectTransform thresholdMarker;
    [Tooltip("可选数字标签")]
    [SerializeField] private TMP_Text label;

    [Header("世界跟随")]
    [Tooltip("屏幕上额外偏移多少像素")]
    [SerializeField] private Vector2 screenPixelOffset = new Vector2(0f, 24f);

    [Header("缓冲填充速度")]
    [SerializeField] private float fastSpeed = 8f;
    [SerializeField] private float slowSpeed = 1.5f;

    [Header("淡入淡出")]
    [SerializeField] private float fadeSpeed = 10f;

    [Header("配色")]
    [SerializeField] private Color interactiveColor = new Color(0.66f, 0.30f, 0.95f);
    [SerializeField] private Color inactiveColor = new Color(0.55f, 0.55f, 0.60f);

    private IBarSource target;
    private float frontValue, bufferValue;
    private float alphaTarget;
    private RectTransform canvasRect;
    private Camera canvasCam;

    /// 当前跟随的数据源（null = 空闲，可被复用）
    public IBarSource Target => target;
    /// 是否空闲（已彻底淡出、无目标 / 目标已失效）
    public bool IsFree => target == null || !target.BarAlive;

    private void Awake()
    {
        if (cam == null) cam = Camera.main;
        if (rect == null) rect = transform as RectTransform;
        if (group == null) group = GetComponent<CanvasGroup>();

        var canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            canvasRect = canvas.transform as RectTransform;
            canvasCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        }

        SetupFill(frontFill);
        SetupFill(bufferFill);

        if (group != null) group.alpha = 0f;
        alphaTarget = 0f;
    }

    /// 开始显示一个新目标（切目标时填充值直接对齐）
    public void Assign(IBarSource e)
    {
        target = e;
        if (e != null)
        {
            frontValue = bufferValue = e.Fraction;
            UpdateThresholdMarker();
        }
        alphaTarget = 1f;
    }

    public void KeepShown() => alphaTarget = 1f;
    public void Hide() => alphaTarget = 0f;

    private void LateUpdate()
    {
        float dt = Time.deltaTime;

        // 目标失效（被销毁）→ 立即释放
        if (target != null && !target.BarAlive) { target = null; if (group != null) group.alpha = 0f; return; }

        // —— 世界跟随定位 ——
        bool onScreen = false;
        if (target != null && cam != null)
        {
            Vector3 sp = cam.WorldToScreenPoint(target.BarWorldPosition);
            if (sp.z > 0f && canvasRect != null)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect, sp, canvasCam, out Vector2 local);
                rect.anchoredPosition = local + screenPixelOffset;
                onScreen = true;
            }
        }

        // —— 缓冲填充 ——
        if (target != null)
        {
            float t = target.Fraction;
            float fS = (t < frontValue ? fastSpeed : slowSpeed);
            frontValue = Mathf.MoveTowards(frontValue, t, fS * dt);
            float bS = (t > bufferValue ? fastSpeed : slowSpeed);
            bufferValue = Mathf.MoveTowards(bufferValue, t, bS * dt);
            bufferValue = Mathf.Max(bufferValue, frontValue);

            if (frontFill != null)
            {
                frontFill.fillAmount = frontValue;
                frontFill.color = target.BarInteractive ? interactiveColor : inactiveColor;
            }
            if (bufferFill != null) bufferFill.fillAmount = bufferValue;
            if (label != null)
                label.text = $"{Mathf.RoundToInt(target.CurrentEnergy)} / {Mathf.RoundToInt(target.MaxEnergy)}";
        }

        // —— 淡入淡出（相机背后 / 无法定位时本帧强制透明，但不释放目标）——
        float a = (target != null && !onScreen) ? 0f : alphaTarget;
        if (group != null) group.alpha = Mathf.MoveTowards(group.alpha, a, fadeSpeed * dt);

        if (alphaTarget <= 0f && (group == null || group.alpha <= 0.01f))
            target = null;
    }

    private void UpdateThresholdMarker()
    {
        if (thresholdMarker == null || target == null || frontFill == null) return;
        float tf = target.BarThresholdFraction;
        if (tf < 0f) { thresholdMarker.gameObject.SetActive(false); return; }  // 无阈值线（如敌人）
        thresholdMarker.gameObject.SetActive(true);
        float w = frontFill.rectTransform.rect.width;
        Vector2 p = thresholdMarker.anchoredPosition;
        p.x = tf * w;
        thresholdMarker.anchoredPosition = p;
    }

    private static void SetupFill(Image img)
    {
        if (img == null) return;
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = (int)Image.OriginHorizontal.Left;
    }
}