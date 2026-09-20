using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI-04 Interaction prompt: floats above the currently focused interactable; text = the object's InteractVerb verbatim (no prefix added).
///
/// Reads the interactor's Interactor.Current directly (it already accounts for "in range + camera aimed + permission"),
/// shows and follows its world position when non-null; fades out when focus is lost. Player/robot each have an Interactor;
/// uses the one that is "enabled and has Current" (only the currently controlled character's Interactor is enabled).
///
/// Text is fully determined by the object's Interact Verb: whatever you enter is shown (e.g. Press "F" to Charge).
/// Position is determined by the object's Prompt Anchor / Prompt Vertical / Prompt World Offset.
///
/// Attach to the root of a custom panel prefab (panel_07 etc.). Supports:
///   - Both TMP_Text and Legacy UI.Text text components, auto-detected in Awake
///   - Backing plate / decorative lines auto-fit width to the text length (panelsToFit)
///   - Optional Animator for pop-in animation on show / hide
/// </summary>
[DisallowMultipleComponent]
public class InteractionPrompt : MonoBehaviour
{
    [Header("Interactor Sources (empty = auto-find all in scene)")]
    [SerializeField] private Interactor[] interactors;

    [Header("Operation Mode Fallback Source (empty = auto-find)")]
    [Tooltip("In Operation Mode the Interactor is frozen; RobotConsoleMover provides the\n" +
             "\"currently nearby interactable\" (charging dock / goal...) instead, with exactly the same conditions as pressing F")]
    [SerializeField] private RobotConsoleMover[] consoleMovers;
    [Tooltip("Uncheck to show no prompts in Operation Mode")]
    [SerializeField] private bool showInOperationMode = true;

    [Header("References")]
    [Tooltip("Camera for world-to-screen conversion; empty = Camera.main")]
    [SerializeField] private Camera cam;
    [Tooltip("Empty = use the CanvasGroup on this object (added automatically if missing)")]
    [SerializeField] private CanvasGroup group;
    [Tooltip("Empty = use this object's RectTransform")]
    [SerializeField] private RectTransform rect;

    [Header("Text (fill either one; empty = auto-find in children)")]
    [Tooltip("TextMeshPro text")]
    [SerializeField] private TMP_Text tmpLabel;
    [Tooltip("Legacy UI text (use this if the prefab's Text is not TMP)")]
    [SerializeField] private Text uguiLabel;

    [Header("Auto Width (for text of varying length)")]
    [Tooltip("Checked: backing plate / decorative line widths follow the text length")]
    [SerializeField] private bool autoFitWidth = true;
    [Tooltip("Elements that should widen: popup_01 backing plate, img_line_* decorative lines, etc.\nEmpty = only resize this object itself")]
    [SerializeField] private RectTransform[] panelsToFit;
    [Tooltip("Padding on each side of the text (pixels)")]
    [SerializeField] private float horizontalPadding = 48f;
    [Tooltip("Minimum width, so the panel does not shrink too much for short text")]
    [SerializeField] private float minWidth = 160f;
    [Tooltip("Maximum width; text wraps beyond this")]
    [SerializeField] private float maxWidth = 720f;

    [Header("World Follow")]
    [Tooltip("Screen offset in pixels in normal mode (third-person perspective camera)")]
    [SerializeField] private Vector2 screenPixelOffset = Vector2.zero;
    [Tooltip("Offset used instead in Operation Mode (orthographic top-down camera).\n" +
             "The two views frame things completely differently; an offset tuned for the perspective camera is clearly off in top-down view,\n" +
             "so each is tuned separately")]
    [SerializeField] private Vector2 operationScreenPixelOffset = new Vector2(0f, 100f);
    [Tooltip("Uniform panel scale in Operation Mode. 1 = no scaling, 0.5 = half size.\n" +
             "The top-down view has a wider field of view, so the panel looks too big at its original size")]
    [Range(0.1f, 2f)]
    [SerializeField] private float operationScale = 0.5f;
    [Tooltip("Transition speed for scale / restore (per second), 0 = instant switch")]
    [SerializeField] private float scaleLerpSpeed = 14f;
    [Tooltip("Checked: temporarily set the panel Pivot to this value in Operation Mode (centered usually looks more natural in top-down)")]
    [SerializeField] private bool overridePivotInOperationMode = false;
    [SerializeField] private Vector2 operationPivot = new Vector2(0.5f, 0f);

    [Header("Fade In/Out")]
    [SerializeField] private float fadeSpeed = 12f;

    [Header("Pop-in Animation (optional)")]
    [Tooltip("Animator on the panel; sets a bool parameter on show / hide")]
    [SerializeField] private Animator animator;
    [SerializeField] private string showBoolParam = "Show";

    private InteractableBase current;
    private Renderer[] rends;
    private float rendsRefreshAt;
    private const float rendsRefreshInterval = 0.5f;

    // Renderers that define the object's on-screen size. Electric field visuals (VFX / debug sphere under an ElectricField)
    // and particle systems are excluded: an Energy Relay's field can be many units wide and would push the prompt off screen.
    private static Renderer[] GatherRenderers(InteractableBase it)
    {
        var all = it.GetComponentsInChildren<Renderer>();
        var list = new System.Collections.Generic.List<Renderer>(all.Length);
        foreach (var r in all)
        {
            if (r == null) continue;
            if (r is ParticleSystemRenderer) continue;
            if (r.GetComponentInParent<ElectricField>() != null) continue;
            list.Add(r);
        }
        return list.ToArray();
    }
    private RectTransform canvasRect;
    private Camera canvasCam;
    private string lastText;
    private bool lastShow;
    private bool viaConsole;          // Whether this frame's target came from the Operation Mode fallback path
    private Vector2 defaultPivot;
    private bool pivotOverridden;
    private Vector3 defaultScale = Vector3.one;

    private void Awake()
    {
        if (cam == null) cam = Camera.main;
        if (rect == null) rect = transform as RectTransform;

        if (group == null) group = GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        // The prompt panel should not block the mouse (otherwise it blocks TPS aiming / Operation Mode clicks)
        group.blocksRaycasts = false;
        group.interactable = false;

        // Text component: prefer TMP, fall back to Legacy Text
        if (tmpLabel == null && uguiLabel == null)
        {
            tmpLabel = GetComponentInChildren<TMP_Text>(true);
            if (tmpLabel == null) uguiLabel = GetComponentInChildren<Text>(true);
        }
        if (tmpLabel == null && uguiLabel == null)
            Debug.LogWarning("[InteractionPrompt] No TMP_Text or UI.Text found in the panel, the prompt will show no text", this);

        if (animator == null) animator = GetComponent<Animator>();

        if (interactors == null || interactors.Length == 0)
            interactors = FindObjectsByType<Interactor>(FindObjectsSortMode.None);

        // RobotConsoleMover is normally disabled, so it must be found with Include
        if (consoleMovers == null || consoleMovers.Length == 0)
            consoleMovers = FindObjectsByType<RobotConsoleMover>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

        var canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            canvasRect = canvas.transform as RectTransform;
            canvasCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        }

        if (rect != null) { defaultPivot = rect.pivot; defaultScale = rect.localScale; }
        group.alpha = 0f;
    }

    private void LateUpdate()
    {
        InteractableBase target = FindTarget();

        if (target != current)
        {
            current = target;
            rends = current != null ? GatherRenderers(current) : null;
            rendsRefreshAt = Time.time + rendsRefreshInterval;
        }
        else if (current != null && Time.time >= rendsRefreshAt)
        {
            // Children can appear / disappear later (e.g. an Energy Relay opening its field) -> re-gather now and then
            rends = GatherRenderers(current);
            rendsRefreshAt = Time.time + rendsRefreshInterval;
        }

        ApplyModeStyle();

        bool show = false;
        if (current != null && cam != null)
        {
            // Text = InteractVerb verbatim, no prefix/key added
            string text = current.InteractVerb;
            if (!string.IsNullOrEmpty(text))
            {
                if (text != lastText) { SetText(text); lastText = text; }

                // Follow position
                Vector3 sp = cam.WorldToScreenPoint(GetWorldAnchor());
                if (sp.z > 0f && canvasRect != null)
                {
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        canvasRect, sp, canvasCam, out Vector2 local);
                    rect.anchoredPosition = local +
                        (viaConsole ? operationScreenPixelOffset : screenPixelOffset);
                    show = true;
                }
            }
        }

        if (show != lastShow)
        {
            lastShow = show;
            if (animator != null && !string.IsNullOrEmpty(showBoolParam))
                animator.SetBool(showBoolParam, show);
        }

        group.alpha = Mathf.MoveTowards(group.alpha, show ? 1f : 0f, fadeSpeed * Time.deltaTime);
    }

    /// Write the text and auto-fit width if needed
    private void SetText(string text)
    {
        if (tmpLabel != null) tmpLabel.text = text;
        if (uguiLabel != null) uguiLabel.text = text;
        if (!autoFitWidth) return;

        float textWidth = MeasureTextWidth(text);
        float width = Mathf.Clamp(textWidth + horizontalPadding, minWidth, maxWidth);

        if (panelsToFit != null && panelsToFit.Length > 0)
        {
            for (int i = 0; i < panelsToFit.Length; i++)
            {
                if (panelsToFit[i] == null) continue;
                var sd = panelsToFit[i].sizeDelta;
                sd.x = width;
                panelsToFit[i].sizeDelta = sd;
            }
        }
        else if (rect != null)
        {
            var sd = rect.sizeDelta;
            sd.x = width;
            rect.sizeDelta = sd;
        }
    }

    private float MeasureTextWidth(string text)
    {
        if (tmpLabel != null)
            return tmpLabel.GetPreferredValues(text, maxWidth, 0f).x;

        if (uguiLabel != null)
        {
            // Legacy Text uses the generator to estimate: set the text first, then read preferredWidth
            uguiLabel.text = text;
            return uguiLabel.preferredWidth;
        }
        return minWidth;
    }

    // Switch panel style (scale / Pivot) in Operation Mode, restore on exit
    private void ApplyModeStyle()
    {
        if (rect == null) return;

        // -- Uniform scale: operationScale in Operation Mode, restored in normal mode --
        Vector3 targetScale = viaConsole ? defaultScale * operationScale : defaultScale;
        rect.localScale = scaleLerpSpeed > 0f
            ? Vector3.MoveTowards(rect.localScale, targetScale, scaleLerpSpeed * Time.deltaTime)
            : targetScale;

        // -- Pivot: only changed on the frame the state flips, to avoid writing every frame --
        if (!overridePivotInOperationMode) return;
        if (viaConsole == pivotOverridden) return;
        rect.pivot = viaConsole ? operationPivot : defaultPivot;
        pivotOverridden = viaConsole;
    }

    /// Who the prompt should be shown for: prefer the Interactor that has control;
    /// if none (= frozen in Operation Mode), fall back to RobotConsoleMover's nearby interactable (charging dock / goal...)
    private InteractableBase FindTarget()
    {
        // A carried Energy Relay always shows its "place" hint (it is not focusable by interactors while carried)
        if (EnergyRelay.CarriedRelay != null) { viaConsole = false; return EnergyRelay.CarriedRelay; }

        Interactor active = ActiveInteractor();
        if (active != null) { viaConsole = false; return active.Current; }

        viaConsole = false;
        if (!showInOperationMode || consoleMovers == null) return null;
        for (int i = 0; i < consoleMovers.Length; i++)
        {
            var cm = consoleMovers[i];
            if (cm == null || !cm.isActiveAndEnabled) continue;
            if (cm.NearestInteractable != null) { viaConsole = true; return cm.NearestInteractable; }
        }
        return null;
    }

    private Interactor ActiveInteractor()
    {
        if (interactors == null) return null;
        for (int i = 0; i < interactors.Length; i++)
        {
            var it = interactors[i];
            if (it != null && it.isActiveAndEnabled && it.Current != null) return it;
        }
        return null;
    }

    // Position is determined by the object itself: use an explicit anchor if present; otherwise place by a fraction of the bounds height, horizontally centered
    private Vector3 GetWorldAnchor()
    {
        if (current.PromptAnchor != null)
            return current.PromptAnchor.position + current.PromptWorldOffset;

        if (rends != null && rends.Length > 0)
        {
            bool has = false; Bounds b = default;
            foreach (var r in rends)
            {
                if (r == null) continue;
                if (!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }
            if (has)
            {
                float y = Mathf.Lerp(b.min.y, b.max.y, current.PromptVertical);
                return new Vector3(b.center.x, y, b.center.z) + current.PromptWorldOffset;
            }
        }
        return current.InteractTransform.position + current.PromptWorldOffset;
    }
}