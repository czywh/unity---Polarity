using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI-04 交互提示：浮在当前焦点可交互物上方，文本 = 物体的 InteractVerb 原文（不叠加前缀）。
///
/// 直接读交互者的 Interactor.Current（它已算好"范围内 + 相机对准 + 权限"），
/// 非空就显示、跟随其世界坐标；焦点消失即淡出。玩家/机器人各一个 Interactor，
/// 取"启用且有 Current"的那个（只有当前控制角色的 Interactor 是启用的）。
///
/// 文案完全由物体的 Interact Verb 决定：填什么显示什么（如 Press "F" to Charge）。
/// 位置由物体的 Prompt Anchor / Prompt Vertical / Prompt World Offset 决定。
///
/// 挂在自定义面板 prefab（panel_07 等）的根物体上。支持：
///   · TMP_Text 与 Legacy UI.Text 两种文本组件，Awake 自动识别
///   · 底板 / 装饰线跟随文字长度自适应宽度（panelsToFit）
///   · 可选 Animator，显示 / 隐藏时给弹出动画
/// </summary>
[DisallowMultipleComponent]
public class InteractionPrompt : MonoBehaviour
{
    [Header("交互者来源（留空自动查找场景中全部）")]
    [SerializeField] private Interactor[] interactors;

    [Header("操作模式回退来源（留空自动查找）")]
    [Tooltip("操作模式下 Interactor 被冻结，改由 RobotConsoleMover 提供\n" +
             "\"当前贴近的充电桩\"，判定条件与按 F 充电完全一致")]
    [SerializeField] private RobotConsoleMover[] consoleMovers;
    [Tooltip("关掉则操作模式下不显示任何提示")]
    [SerializeField] private bool showInOperationMode = true;

    [Header("引用")]
    [Tooltip("世界→屏幕换算相机；留空取 Camera.main")]
    [SerializeField] private Camera cam;
    [Tooltip("留空自动取本物体上的 CanvasGroup（没有会自动添加）")]
    [SerializeField] private CanvasGroup group;
    [Tooltip("留空取本物体的 RectTransform")]
    [SerializeField] private RectTransform rect;

    [Header("文本（两种任填其一，留空则自动在子物体里找）")]
    [Tooltip("TextMeshPro 文本")]
    [SerializeField] private TMP_Text tmpLabel;
    [Tooltip("Legacy UI 文本（prefab 里的 Text 若不是 TMP 就填这个）")]
    [SerializeField] private Text uguiLabel;

    [Header("自适应宽度（文案长短不一时用）")]
    [Tooltip("勾选：底板 / 装饰线的宽度跟着文字长度走")]
    [SerializeField] private bool autoFitWidth = true;
    [Tooltip("要跟着变宽的元素：popup_01 底板、img_line_* 装饰线等。\n留空则只改本物体自身的宽度")]
    [SerializeField] private RectTransform[] panelsToFit;
    [Tooltip("文字两侧留白（像素）")]
    [SerializeField] private float horizontalPadding = 48f;
    [Tooltip("宽度下限，避免短文案时面板缩得太小")]
    [SerializeField] private float minWidth = 160f;
    [Tooltip("宽度上限，超过则文字换行")]
    [SerializeField] private float maxWidth = 720f;

    [Header("世界跟随")]
    [Tooltip("普通模式（第三人称透视相机）下的屏幕偏移像素")]
    [SerializeField] private Vector2 screenPixelOffset = Vector2.zero;
    [Tooltip("操作模式（正交俯视相机）下改用这个偏移。\n" +
             "两种视角的构图完全不同，透视相机下调好的偏移在俯视图里会明显跑偏，\n" +
             "所以分开各调一套")]
    [SerializeField] private Vector2 operationScreenPixelOffset = new Vector2(0f, 100f);
    [Tooltip("操作模式下面板的等比缩放。1 = 不缩放，0.5 = 缩小一半。\n" +
             "俯视图视野更广，面板按原尺寸会显得过大")]
    [Range(0.1f, 2f)]
    [SerializeField] private float operationScale = 0.5f;
    [Tooltip("缩放 / 还原的过渡速度（每秒），0 = 瞬间切换")]
    [SerializeField] private float scaleLerpSpeed = 14f;
    [Tooltip("勾选：操作模式下把面板 Pivot 临时改成这个值（俯视图一般用居中更自然）")]
    [SerializeField] private bool overridePivotInOperationMode = false;
    [SerializeField] private Vector2 operationPivot = new Vector2(0.5f, 0f);

    [Header("淡入淡出")]
    [SerializeField] private float fadeSpeed = 12f;

    [Header("弹出动画（可选）")]
    [Tooltip("面板上的 Animator；显示 / 隐藏时设置 bool 参数")]
    [SerializeField] private Animator animator;
    [SerializeField] private string showBoolParam = "Show";

    private InteractableBase current;
    private Renderer[] rends;
    private RectTransform canvasRect;
    private Camera canvasCam;
    private string lastText;
    private bool lastShow;
    private bool viaConsole;          // 本帧的目标是否来自操作模式回退路径
    private Vector2 defaultPivot;
    private bool pivotOverridden;
    private Vector3 defaultScale = Vector3.one;

    private void Awake()
    {
        if (cam == null) cam = Camera.main;
        if (rect == null) rect = transform as RectTransform;

        if (group == null) group = GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        // 提示面板不该拦截鼠标（否则会挡住 TPS 瞄准 / 操作模式点击）
        group.blocksRaycasts = false;
        group.interactable = false;

        // 文本组件：优先 TMP，找不到再退回 Legacy Text
        if (tmpLabel == null && uguiLabel == null)
        {
            tmpLabel = GetComponentInChildren<TMP_Text>(true);
            if (tmpLabel == null) uguiLabel = GetComponentInChildren<Text>(true);
        }
        if (tmpLabel == null && uguiLabel == null)
            Debug.LogWarning("[InteractionPrompt] 面板里没找到 TMP_Text 或 UI.Text，提示不会显示文字", this);

        if (animator == null) animator = GetComponent<Animator>();

        if (interactors == null || interactors.Length == 0)
            interactors = FindObjectsByType<Interactor>(FindObjectsSortMode.None);

        // RobotConsoleMover 平时是禁用状态，必须带 Include 才找得到
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
            rends = current != null ? current.GetComponentsInChildren<Renderer>() : null;
        }

        ApplyModeStyle();

        bool show = false;
        if (current != null && cam != null)
        {
            // 文本 = InteractVerb 原文，不叠加任何前缀/按键
            string text = current.InteractVerb;
            if (!string.IsNullOrEmpty(text))
            {
                if (text != lastText) { SetText(text); lastText = text; }

                // 位置跟随
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

    /// 写入文字并按需自适应宽度
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
            // Legacy Text 用生成器估算：先设文本再取 preferredWidth
            uguiLabel.text = text;
            return uguiLabel.preferredWidth;
        }
        return minWidth;
    }

    // 操作模式下切换面板样式（缩放 / Pivot），退出时还原
    private void ApplyModeStyle()
    {
        if (rect == null) return;

        // —— 等比缩放：操作模式用 operationScale，普通模式还原 ——
        Vector3 targetScale = viaConsole ? defaultScale * operationScale : defaultScale;
        rect.localScale = scaleLerpSpeed > 0f
            ? Vector3.MoveTowards(rect.localScale, targetScale, scaleLerpSpeed * Time.deltaTime)
            : targetScale;

        // —— Pivot：只在状态翻转的那一帧改，避免每帧写 ——
        if (!overridePivotInOperationMode) return;
        if (viaConsole == pivotOverridden) return;
        rect.pivot = viaConsole ? operationPivot : defaultPivot;
        pivotOverridden = viaConsole;
    }

    /// 当前该给谁显示提示：优先持有控制权的 Interactor；
    /// 都没有（= 操作模式冻结中）则回退到 RobotConsoleMover 的贴近充电桩
    private InteractableBase FindTarget()
    {
        Interactor active = ActiveInteractor();
        if (active != null) { viaConsole = false; return active.Current; }

        viaConsole = false;
        if (!showInOperationMode || consoleMovers == null) return null;
        for (int i = 0; i < consoleMovers.Length; i++)
        {
            var cm = consoleMovers[i];
            if (cm == null || !cm.isActiveAndEnabled) continue;
            if (cm.NearestDock != null) { viaConsole = true; return cm.NearestDock; }
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

    // 位置由物体自身决定：有显式锚点用锚点；否则按包围盒高度比例定位，水平取中心
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