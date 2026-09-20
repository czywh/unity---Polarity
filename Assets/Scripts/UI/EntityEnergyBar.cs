using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI-02 World-following energy bar (passive, pooled). Data source is now the generic IBarSource interface,
/// so the same prefab / pool can show both electric entities (ElectricEntity) and enemies (EnergySystem).
///
/// This bar doesn't decide its own visibility; EntityBarManager controls it via Assign / KeepShown / Hide.
/// It is only responsible for: following the target's world position, two-layer buffered fill, threshold line, colors, fade in/out.
/// Lives under an Overlay Canvas (usually instantiated from a pooled Prefab).
/// </summary>
[DisallowMultipleComponent]
public class EntityEnergyBar : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Camera for world -> screen conversion; uses Camera.main if left empty")]
    [SerializeField] private Camera cam;
    [SerializeField] private CanvasGroup group;
    [SerializeField] private RectTransform rect;
    [SerializeField] private Image frontFill;
    [SerializeField] private Image bufferFill;
    [Tooltip("Threshold tick line, can be left empty")]
    [SerializeField] private RectTransform thresholdMarker;
    [Tooltip("Optional number label")]
    [SerializeField] private TMP_Text label;

    [Header("World Follow")]
    [Tooltip("Extra on-screen offset in pixels")]
    [SerializeField] private Vector2 screenPixelOffset = new Vector2(0f, 24f);

    [Header("Buffered Fill Speed")]
    [SerializeField] private float fastSpeed = 8f;
    [SerializeField] private float slowSpeed = 1.5f;

    [Header("Fade In/Out")]
    [SerializeField] private float fadeSpeed = 10f;

    [Header("Colors")]
    [SerializeField] private Color interactiveColor = new Color(0.66f, 0.30f, 0.95f);
    [SerializeField] private Color inactiveColor = new Color(0.55f, 0.55f, 0.60f);

    private IBarSource target;
    private float frontValue, bufferValue;
    private float alphaTarget;
    private RectTransform canvasRect;
    private Camera canvasCam;

    /// Currently followed data source (null = idle, can be reused)
    public IBarSource Target => target;
    /// Whether idle (fully faded out, no target / target invalid)
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

    /// Start showing a new target (fill value snaps directly when switching targets)
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

        // Target invalid (destroyed) -> release immediately
        if (target != null && !target.BarAlive) { target = null; if (group != null) group.alpha = 0f; return; }

        // -- World-follow positioning --
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

        // -- Buffered fill --
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

        // -- Fade in/out (behind the camera / can't be positioned: force transparent this frame, but don't release the target) --
        float a = (target != null && !onScreen) ? 0f : alphaTarget;
        if (group != null) group.alpha = Mathf.MoveTowards(group.alpha, a, fadeSpeed * dt);

        if (alphaTarget <= 0f && (group == null || group.alpha <= 0.01f))
            target = null;
    }

    private void UpdateThresholdMarker()
    {
        if (thresholdMarker == null || target == null || frontFill == null) return;
        float tf = target.BarThresholdFraction;
        if (tf < 0f) { thresholdMarker.gameObject.SetActive(false); return; }  // No threshold line (e.g. enemies)
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