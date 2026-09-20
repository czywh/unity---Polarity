using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// UI - Level-clear results panel. Built at runtime from SCI-FI GUI Pack assets, no hand-built prefab needed:
///   - Background: popup_03 (9-slice popup, cyan highlight bar at the bottom)
///   - Line under title: title_01 (glowing line)
///   - Buttons: btn_01_hover (dark) as normal, btn_01_normal (bright) as hover/pressed -- SpriteSwap
///   - Divider: img_line_01 (9-slice)
/// Shows: time / death count / energy used, plus RESTART and NEXT LEVEL buttons.
///
/// Text uses Legacy UI.Text (built-in Arial, can display Chinese directly; the text fields can be changed to Chinese freely).
/// Canvas sortingOrder = 100, above the operation panel (50). Buttons stay clickable when timeScale = 0 (UI doesn't depend on deltaTime).
/// </summary>
[DisallowMultipleComponent]
public class GameResultUI : MonoBehaviour
{
    [Header("Assets (empty = auto-load from SCI-FI GUI Pack's Resources)")]
    public Sprite popupSprite;
    public Sprite titleLineSprite;
    public Sprite dividerSprite;
    public Sprite buttonNormalSprite;
    public Sprite buttonHighlightSprite;
    public string popupPath = "Sprites/9sliced/popup_03";
    public string titleLinePath = "Sprites/Title/title_01";
    public string dividerPath = "Sprites/Sliced Elements/img_line_01";
    public string buttonNormalPath = "Sprites/Button/btn_01_hover";     // Dark: normal
    public string buttonHighlightPath = "Sprites/Button/btn_01_normal"; // Bright: hover/pressed

    [Header("Text")]
    public string titleText = "MISSION COMPLETE";
    public string timeLabel = "TIME";
    public string playerDeathsLabel = "PLAYER DEATHS";
    public string robotDeathsLabel = "ROBOT DEATHS";
    public string energyLabel = "ENERGY USED";
    public string restartLabel = "RESTART";
    [Tooltip("GameResultManager decides NEXT LEVEL or MAIN MENU on Show; this is the default")]
    public string nextLabel = "NEXT LEVEL";
    public string energyUnit = "";

    [Header("Style")]
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

    [Header("Debug (runtime, read-only)")]
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

    /// <summary>Show results. nextButtonLabel is passed in by the manager (NEXT LEVEL / MAIN MENU)</summary>
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

        // Allow gamepad / keyboard to press directly: RESTART selected by default
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(restartBtn.gameObject);
    }

    public void Hide()
    {
        if (root != null) root.SetActive(false);
        visible = false;
    }

    // ----------------------------------------------------------------
    //  Build
    // ----------------------------------------------------------------

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

        // Root (full-screen overlay)
        root = new GameObject("Root", typeof(RectTransform));
        root.transform.SetParent(cgo.transform, false);
        Stretch(root.GetComponent<RectTransform>());
        var dim = root.AddComponent<Image>();
        dim.color = dimColor;
        dim.raycastTarget = true;   // Block all clicks underneath

        // Popup background
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

        // Title + glowing line
        titleT = MakeText(panel.transform, "Title", titleText, titleFontSize, textColor, TextAnchor.MiddleCenter, FontStyle.Bold);
        SetPreferredHeight(titleT.gameObject, titleFontSize + 16f);
        var line = MakeImage(panel.transform, "TitleLine", titleLineSprite, Image.Type.Simple);
        line.preserveAspect = true;
        SetPreferredHeight(line.gameObject, 30f);

        Spacer(panel.transform, 8f);

        // Three stat rows
        timeV         = MakeStatRow(panel.transform, timeLabel);
        playerDeathsV = MakeStatRow(panel.transform, playerDeathsLabel);
        robotDeathsV  = MakeStatRow(panel.transform, robotDeathsLabel);
        energyV       = MakeStatRow(panel.transform, energyLabel);

        Spacer(panel.transform, 6f);
        var div = MakeImage(panel.transform, "Divider", dividerSprite, Image.Type.Sliced);
        div.color = new Color(textColor.r, textColor.g, textColor.b, 0.7f);
        SetPreferredHeight(div.gameObject, 4f);
        Spacer(panel.transform, 10f);

        // Button row
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

        // Keyboard / gamepad navigation: left and right link to each other
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

    // -- Helpers --

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
            Debug.LogWarning("[ResultUI] SCI-FI GUI Pack sprites failed to load; make sure its Resources folder still exists, or drag Sprites into the Inspector manually", this);

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
