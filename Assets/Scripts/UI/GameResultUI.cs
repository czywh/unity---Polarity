using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// UI　通关结算面板。用 SCI-FI GUI Pack 的素材在运行时生成，不需要手搭预制体：
///   · 底：popup_03（9-slice 弹窗，底部青色亮条）
///   · 标题下横线：title_01（发光线）
///   · 按钮：btn_01_hover（暗）为常态，btn_01_normal（亮）为悬停/按下 —— SpriteSwap
///   · 分隔线：img_line_01（9-slice）
/// 显示：用时 / 死亡次数 / 耗电量，以及 RESTART 与 NEXT LEVEL 两个按钮。
///
/// 文本用 Legacy UI.Text（内置 Arial，中文可直接显示；字段里的文案随便改成中文）。
/// 画布 sortingOrder = 100，盖在操作面板 (50) 之上。timeScale = 0 时按钮照常可点（UI 不依赖 deltaTime）。
/// </summary>
[DisallowMultipleComponent]
public class GameResultUI : MonoBehaviour
{
    [Header("素材（留空自动从 SCI-FI GUI Pack 的 Resources 加载）")]
    public Sprite popupSprite;
    public Sprite titleLineSprite;
    public Sprite dividerSprite;
    public Sprite buttonNormalSprite;
    public Sprite buttonHighlightSprite;
    public string popupPath = "Sprites/9sliced/popup_03";
    public string titleLinePath = "Sprites/Title/title_01";
    public string dividerPath = "Sprites/Sliced Elements/img_line_01";
    public string buttonNormalPath = "Sprites/Button/btn_01_hover";     // 暗底：常态
    public string buttonHighlightPath = "Sprites/Button/btn_01_normal"; // 亮底：悬停/按下

    [Header("文案")]
    public string titleText = "MISSION COMPLETE";
    public string timeLabel = "TIME";
    public string playerDeathsLabel = "PLAYER DEATHS";
    public string robotDeathsLabel = "ROBOT DEATHS";
    public string energyLabel = "ENERGY USED";
    public string restartLabel = "RESTART";
    [Tooltip("由 GameResultManager 在 Show 时决定是 NEXT LEVEL 还是 MAIN MENU；这里是默认值")]
    public string nextLabel = "NEXT LEVEL";
    public string energyUnit = "";

    [Header("样式")]
    public Font uiFont;
    public Color textColor = new Color(0.62f, 0.96f, 1f, 1f);
    public Color valueColor = Color.white;
    public Color buttonTextColor = Color.white;
    public Color dimColor = new Color(0f, 0f, 0f, 0.62f);
    public Vector2 panelSize = new Vector2(760f, 620f);
    public Vector2 buttonSize = new Vector2(300f, 106f);
    public int titleFontSize = 44;
    public int labelFontSize = 26;
    public int valueFontSize = 30;
    public int buttonFontSize = 26;

    [Header("调试（运行时只读）")]
    [SerializeField] private bool visible;

    public bool Visible => visible;

    private Canvas canvas;
    private GameObject root;
    private Text titleT, timeV, playerDeathsV, robotDeathsV, energyV, nextBtnText;
    private Button restartBtn, nextBtn;

    public static GameResultUI CreateDefault()
    {
        var go = new GameObject("GameResultUI");
        return go.AddComponent<GameResultUI>();
    }

    /// <summary>显示结算。nextButtonLabel 由管理器传入（NEXT LEVEL / MAIN MENU）</summary>
    public void Show(LevelStats stats, string nextButtonLabel = null)
    {
        EnsureBuilt();

        timeV.text = stats != null ? stats.FormatTime() : "--:--";
        playerDeathsV.text = stats != null ? stats.PlayerDeaths.ToString() : "-";
        robotDeathsV.text  = stats != null ? stats.RobotDeaths.ToString()  : "-";
        energyV.text = stats != null ? $"{stats.EnergyUsed:F0}{energyUnit}" : "-";
        nextBtnText.text = string.IsNullOrEmpty(nextButtonLabel) ? nextLabel : nextButtonLabel;

        root.SetActive(true);
        visible = true;

        // 让手柄 / 键盘也能直接按：默认选中 RESTART
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(restartBtn.gameObject);
    }

    public void Hide()
    {
        if (root != null) root.SetActive(false);
        visible = false;
    }

    // ────────────────────────────────────────────────────────────────
    //  构建
    // ────────────────────────────────────────────────────────────────

    private void EnsureBuilt()
    {
        if (root != null) return;
        LoadDefaults();
        EnsureEventSystem();

        // Canvas
        var cgo = new GameObject("GameResultUI_Canvas", typeof(RectTransform));
        cgo.transform.SetParent(transform, false);
        canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = cgo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        cgo.AddComponent<GraphicRaycaster>();

        // 根（全屏遮罩）
        root = new GameObject("Root", typeof(RectTransform));
        root.transform.SetParent(cgo.transform, false);
        Stretch(root.GetComponent<RectTransform>());
        var dim = root.AddComponent<Image>();
        dim.color = dimColor;
        dim.raycastTarget = true;   // 挡住下面的一切点击

        // 弹窗底
        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(root.transform, false);
        var prt = panel.GetComponent<RectTransform>();
        prt.sizeDelta = panelSize;
        var pimg = panel.AddComponent<Image>();
        pimg.sprite = popupSprite;
        pimg.type = Image.Type.Sliced;
        pimg.color = Color.white;

        var v = panel.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(56, 56, 44, 52);
        v.spacing = 14f;
        v.childAlignment = TextAnchor.UpperCenter;
        v.childControlWidth = true;  v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;

        // 标题 + 发光线
        titleT = MakeText(panel.transform, "Title", titleText, titleFontSize, textColor, TextAnchor.MiddleCenter, FontStyle.Bold);
        SetPreferredHeight(titleT.gameObject, titleFontSize + 16f);
        var line = MakeImage(panel.transform, "TitleLine", titleLineSprite, Image.Type.Simple);
        line.preserveAspect = true;
        SetPreferredHeight(line.gameObject, 30f);

        Spacer(panel.transform, 8f);

        // 三行统计
        timeV         = MakeStatRow(panel.transform, timeLabel);
        playerDeathsV = MakeStatRow(panel.transform, playerDeathsLabel);
        robotDeathsV  = MakeStatRow(panel.transform, robotDeathsLabel);
        energyV       = MakeStatRow(panel.transform, energyLabel);

        Spacer(panel.transform, 6f);
        var div = MakeImage(panel.transform, "Divider", dividerSprite, Image.Type.Sliced);
        div.color = new Color(textColor.r, textColor.g, textColor.b, 0.7f);
        SetPreferredHeight(div.gameObject, 4f);
        Spacer(panel.transform, 10f);

        // 按钮行
        var row = new GameObject("Buttons", typeof(RectTransform));
        row.transform.SetParent(panel.transform, false);
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 28f;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = false; h.childControlHeight = false;
        h.childForceExpandWidth = false; h.childForceExpandHeight = false;
        SetPreferredHeight(row, buttonSize.y);

        restartBtn = MakeButton(row.transform, "Restart", restartLabel, out _);
        restartBtn.onClick.AddListener(() => { var m = GameResultManager.Instance; if (m != null) m.Restart(); });

        nextBtn = MakeButton(row.transform, "Next", nextLabel, out nextBtnText);
        nextBtn.onClick.AddListener(() => { var m = GameResultManager.Instance; if (m != null) m.NextLevel(); });

        // 键盘 / 手柄导航：左右互通
        var nr = restartBtn.navigation; nr.mode = Navigation.Mode.Explicit; nr.selectOnRight = nextBtn; nr.selectOnLeft = nextBtn; restartBtn.navigation = nr;
        var nn = nextBtn.navigation;    nn.mode = Navigation.Mode.Explicit; nn.selectOnLeft = restartBtn; nn.selectOnRight = restartBtn; nextBtn.navigation = nn;

        root.SetActive(false);
    }

    private Text MakeStatRow(Transform parent, string label)
    {
        var row = new GameObject("Row_" + label, typeof(RectTransform));
        row.transform.SetParent(parent, false);
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = true; h.childForceExpandHeight = false;
        SetPreferredHeight(row, valueFontSize + 14f);

        MakeText(row.transform, "Label", label, labelFontSize, textColor, TextAnchor.MiddleLeft, FontStyle.Normal);
        var val = MakeText(row.transform, "Value", "-", valueFontSize, valueColor, TextAnchor.MiddleRight, FontStyle.Bold);
        return val;
    }

    private Button MakeButton(Transform parent, string name, string label, out Text labelText)
    {
        var go = new GameObject("Btn_" + name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = buttonSize;

        var img = go.AddComponent<Image>();
        img.sprite = buttonNormalSprite;
        img.type = Image.Type.Simple;
        img.preserveAspect = false;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.transition = Selectable.Transition.SpriteSwap;
        var ss = btn.spriteState;
        ss.highlightedSprite = buttonHighlightSprite;
        ss.pressedSprite = buttonHighlightSprite;
        ss.selectedSprite = buttonHighlightSprite;
        btn.spriteState = ss;

        labelText = MakeText(go.transform, "Label", label, buttonFontSize, buttonTextColor, TextAnchor.MiddleCenter, FontStyle.Bold);
        Stretch(labelText.rectTransform);
        labelText.raycastTarget = false;
        return btn;
    }

    // ── 小工具 ──

    private Text MakeText(Transform parent, string name, string text, int size, Color color, TextAnchor align, FontStyle style)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.text = text; t.font = uiFont; t.fontSize = size; t.color = color;
        t.alignment = align; t.fontStyle = style;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    private static Image MakeImage(Transform parent, string name, Sprite sprite, Image.Type type)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = sprite; img.type = type; img.raycastTarget = false;
        return img;
    }

    private static void Spacer(Transform parent, float height)
    {
        var go = new GameObject("Spacer", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        SetPreferredHeight(go, height);
    }

    private static void SetPreferredHeight(GameObject go, float h)
    {
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.preferredHeight = h; le.minHeight = h;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private void LoadDefaults()
    {
        if (popupSprite == null) popupSprite = Resources.Load<Sprite>(popupPath);
        if (titleLineSprite == null) titleLineSprite = Resources.Load<Sprite>(titleLinePath);
        if (dividerSprite == null) dividerSprite = Resources.Load<Sprite>(dividerPath);
        if (buttonNormalSprite == null) buttonNormalSprite = Resources.Load<Sprite>(buttonNormalPath);
        if (buttonHighlightSprite == null) buttonHighlightSprite = Resources.Load<Sprite>(buttonHighlightPath);
        if (popupSprite == null || buttonNormalSprite == null)
            Debug.LogWarning("[结算UI] SCI-FI GUI Pack 的图没加载到，确认它的 Resources 目录还在，或手动拖 Sprite 到 Inspector", this);

        if (uiFont == null)
        {
            uiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (uiFont == null) uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        if (FindFirstObjectByType<EventSystem>() != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }
}
