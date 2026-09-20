using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// UI　操作模式浮动开关：进操作模式后，为当前操作台的每个 ConsoleOperable 在【物体旁边】
/// 生成一个 SCI-FI GUI Pack 的 toggle_02 开关（off/on 两张图），点击即调 Operate()。
///
/// 不是固定菜单栏，而是每个物体旁边一个开关。位置怎么来的：
///   · 进操作模式时把该物体的【固定锚点】ConsoleOperable.ButtonAnchorFixed
///     用【操作模式那台相机】投影到屏幕，开关就钉在那儿；
///   · 固定锚点是物体 Start() 吸附到 A 状态时记下的世界坐标，之后物体移到 B、转到哪都不影响 ——
///     所以无论什么时候进操作模式、桥在哪个状态，按钮都在同一个地方；
///   · 正交相机下 WorldToScreenPoint 精确对位，开关落点就是物体在屏幕上的实际位置，
///     不是写死的屏幕坐标 —— 所以换关卡、换机位、换分辨率都不会错位（视差问题的解法）。
///   · 想让开关跟着物体动，勾 followTarget。
///
/// 零配置：Canvas 和每个开关都在运行时用代码生成，toggle 图从 SCI-FI GUI Pack 的
/// Resources 目录 Resources.Load 出来。想调开关出现的位置，去对应 ConsoleOperable 上
/// 填 buttonAnchor（拖个空子物体）或改 buttonLocalOffset。
///
/// 类名沿用 ConsoleTogglePanel 以兼容 OperationModeController 的引用；它现在做的是浮动开关，不是面板。
/// </summary>
[DisallowMultipleComponent]
public class ConsoleTogglePanel : MonoBehaviour
{
    [Header("Toggle 图（留空自动从 SCI-FI GUI Pack 的 Resources 加载）")]
    public Sprite toggleOffSprite;
    public Sprite toggleOnSprite;
    public string toggleOffResourcePath = "Sprites/Button/toggle_02_off";
    public string toggleOnResourcePath = "Sprites/Button/toggle_02_on";

    [Header("开关外观")]
    [Tooltip("开关显示尺寸（像素）；原图 394×164，默认缩到约 1/3")]
    public Vector2 toggleSize = new Vector2(132f, 55f);
    [Tooltip("在物体屏幕投影点基础上，再加一点像素偏移（微调，避免正好压在物体上）")]
    public Vector2 screenPixelOffset = new Vector2(0f, 0f);
    [Tooltip("物体跑到相机背后 / 视野外时隐藏开关")]
    public bool hideWhenOffscreen = true;

    [Header("跟随")]
    [Tooltip("默认关：位置在进入操作模式时算一次就固定。\n勾上则每帧重算，开关跟着物体平移 / 旋转一起动")]
    public bool followTarget = false;

    [Header("快捷键")]
    [Tooltip("面板显示期间，数字键 1~9 触发第 N 个开关")]
    public bool numberHotkeys = true;

    [Header("调试（运行时只读）")]
    [SerializeField] private bool visible;
    [SerializeField] private int buttonCount;

    public bool Visible => visible;
    public ConsoleStation CurrentConsole { get; private set; }

    private Canvas canvas;
    private RectTransform canvasRect;
    private Camera cam;
    private readonly List<Follower> followers = new List<Follower>();

    private class Follower
    {
        public ConsoleOperable operable;
        public RectTransform root;
        public Image off;
        public Image on;
        public System.Action<ConsoleOperable> handler;
    }

    // ────────────────────────────────────────────────────────────────────
    //  对外接口
    // ────────────────────────────────────────────────────────────────────

    /// <summary>场景里没有本组件时由 OperationModeController 调用</summary>
    public static ConsoleTogglePanel CreateDefault()
    {
        var go = new GameObject("ConsoleToggleButtons");
        return go.AddComponent<ConsoleTogglePanel>();
    }

    /// <summary>显示开关。cam 传操作模式用的那台相机（用于把物体投影到屏幕）</summary>
    public void Show(ConsoleStation console, Camera projectionCamera)
    {
        EnsureBuilt();
        ClearFollowers();

        CurrentConsole = console;
        cam = projectionCamera != null ? projectionCamera : Camera.main;
        if (cam == null) Debug.LogWarning("[操作开关] 没有可用相机，开关无法定位", this);

        var ops = console != null ? console.GetOperables() : new ConsoleOperable[0];
        for (int i = 0; i < ops.Length; i++) followers.Add(BuildFollower(ops[i], i));

        buttonCount = followers.Count;
        canvas.gameObject.SetActive(true);
        visible = true;
        Reposition();   // 算一次位置并钉在那儿（followTarget 关时这就是最终位置）
    }

    /// <summary>隐藏开关并解除订阅</summary>
    public void Hide()
    {
        ClearFollowers();
        if (canvas != null) canvas.gameObject.SetActive(false);
        CurrentConsole = null;
        visible = false;
        buttonCount = 0;
    }

    // ────────────────────────────────────────────────────────────────────
    //  快捷键 / 可选跟随
    // ────────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (!visible || !numberHotkeys) return;
        for (int i = 0; i < 9 && i < followers.Count; i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
            {
                if (followers[i].operable != null) followers[i].operable.Operate();
                break;
            }
    }

    // 默认不跟随：位置在 Show() 里算过一次就固定，这里什么都不做。
    // 勾了 followTarget 才逐帧重算。
    private void LateUpdate()
    {
        if (visible && followTarget) Reposition();
    }

    private void Reposition()
    {
        if (cam == null) return;
        foreach (var f in followers)
        {
            if (f.operable == null || f.root == null) continue;

            // followTarget 关（默认）：用物体在 A 状态时记下的固定锚点，桥怎么动按钮都不动
            Vector3 sp = cam.WorldToScreenPoint(f.operable.GetButtonAnchor(followTarget));
            bool onscreen = sp.z > 0f &&
                            (!hideWhenOffscreen ||
                             (sp.x >= -toggleSize.x && sp.x <= Screen.width + toggleSize.x &&
                              sp.y >= -toggleSize.y && sp.y <= Screen.height + toggleSize.y));

            f.root.gameObject.SetActive(onscreen);
            if (!onscreen) continue;

            // 屏幕像素 → Overlay 画布局部坐标（画布用 ConstantPixelSize，scaleFactor=1，二者一致）
            f.root.position = new Vector3(sp.x + screenPixelOffset.x, sp.y + screenPixelOffset.y, 0f);
        }
    }

    private void OnDisable() => ClearFollowers();

    // ────────────────────────────────────────────────────────────────────
    //  单个浮动开关
    // ────────────────────────────────────────────────────────────────────

    private Follower BuildFollower(ConsoleOperable op, int index)
    {
        var f = new Follower { operable = op };

        var go = new GameObject($"Toggle_{index + 1}_{op.name}", typeof(RectTransform));
        go.transform.SetParent(canvasRect, false);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = toggleSize;
        rt.pivot = new Vector2(0.5f, 0.5f);   // 以物体投影点为中心
        f.root = rt;

        f.off = CreateFullImage(rt, "off", toggleOffSprite);
        f.on = CreateFullImage(rt, "on", toggleOnSprite);

        var btn = go.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.targetGraphic = f.off;
        // 不参与键盘 / 手柄导航：否则按方向键会选中它，Submit 键（空格/回车）就会触发
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        var captured = op;
        btn.onClick.AddListener(() =>
        {
            captured.Operate();
            // 点完立刻取消选中。UGUI 会把刚点过的 Button 记为"当前选中项"，
            // 之后按 Submit（默认空格 = 跳跃键）会再次触发它 —— 这就是"按空格桥会动"的原因
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        });

        f.handler = _ => RefreshFollower(f);
        op.StateChanged += f.handler;
        RefreshFollower(f);

        return f;
    }

    private static void RefreshFollower(Follower f)
    {
        if (f.operable == null) return;
        bool on = f.operable.IsAtB;
        if (f.on != null) f.on.enabled = on;
        if (f.off != null) f.off.enabled = !on;
    }

    private void ClearFollowers()
    {
        foreach (var f in followers)
        {
            if (f.operable != null && f.handler != null) f.operable.StateChanged -= f.handler;
            if (f.root != null) Destroy(f.root.gameObject);
        }
        followers.Clear();
    }

    // ────────────────────────────────────────────────────────────────────
    //  画布（只建一次）
    // ────────────────────────────────────────────────────────────────────

    private void EnsureBuilt()
    {
        if (canvas != null) return;
        LoadDefaults();
        EnsureEventSystem();

        var cgo = new GameObject("ConsoleToggle_Canvas", typeof(RectTransform));
        cgo.transform.SetParent(transform, false);
        canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        // ConstantPixelSize + scaleFactor 1：画布局部坐标 == 屏幕像素，Reposition 直接用投影像素
        var scaler = cgo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1f;
        cgo.AddComponent<GraphicRaycaster>();
        canvasRect = cgo.GetComponent<RectTransform>();
        canvas.gameObject.SetActive(false);
    }

    private static Image CreateFullImage(Transform parent, string name, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;
        img.raycastTarget = true;
        return img;
    }

    private void LoadDefaults()
    {
        if (toggleOffSprite == null) toggleOffSprite = Resources.Load<Sprite>(toggleOffResourcePath);
        if (toggleOnSprite == null) toggleOnSprite = Resources.Load<Sprite>(toggleOnResourcePath);
        if (toggleOffSprite == null || toggleOnSprite == null)
            Debug.LogWarning($"[操作开关] toggle 图没加载到：{toggleOffResourcePath} / {toggleOnResourcePath}。" +
                             "确认 SCI-FI GUI Pack 的 Resources 目录还在，或手动把两张 Sprite 拖到 Inspector", this);
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        if (FindFirstObjectByType<EventSystem>() != null) return;
        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        Debug.Log("[操作开关] 场景里没有 EventSystem，已自动创建一个（UI 点击需要它）", es);
    }
}
