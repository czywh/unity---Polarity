using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// UI  Operation Mode floating toggles: after entering Operation Mode, for each ConsoleOperable of the current console, a
/// SCI-FI GUI Pack toggle_02 switch (off/on sprites) is spawned [next to the object]; clicking it calls Operate().
///
/// Not a fixed menu bar, but one toggle beside each object. How the position is derived:
///   - On entering Operation Mode, the object's [fixed anchor] ConsoleOperable.ButtonAnchorFixed
///     is projected to screen with [the Operation Mode camera], and the toggle is pinned there;
///   - The fixed anchor is the world position recorded when the object snaps to state A in Start(); moving to B or rotating afterward doesn't affect it --
///     so no matter when you enter Operation Mode or what state the bridge is in, the button is in the same place;
///   - With an orthographic camera WorldToScreenPoint aligns exactly, so the toggle lands at the object's actual screen position,
///     not a hardcoded screen coordinate -- so changing level, camera position or resolution won't misalign it (the fix for parallax issues).
///   - To make toggles follow the object, check followTarget.
///
/// Zero config: the Canvas and each toggle are generated in code at runtime; toggle sprites are loaded via Resources.Load from
/// the SCI-FI GUI Pack Resources folder. To adjust where a toggle appears, go to the corresponding ConsoleOperable and
/// set buttonAnchor (drag in an empty child) or change buttonLocalOffset.
///
/// Class name stays ConsoleTogglePanel for compatibility with OperationModeController's reference; it now does floating toggles, not a panel.
/// </summary>
[DisallowMultipleComponent]
public class ConsoleTogglePanel : MonoBehaviour
{
    [Header("Toggle Sprites (empty = auto-load from SCI-FI GUI Pack Resources)")]
    public Sprite toggleOffSprite;
    public Sprite toggleOnSprite;
    public string toggleOffResourcePath = "Sprites/Button/toggle_02_off";
    public string toggleOnResourcePath = "Sprites/Button/toggle_02_on";

    [Header("Toggle Appearance")]
    [Tooltip("Toggle display size (pixels); source image is 394x164, scaled to about 1/3 by default")]
    public Vector2 toggleSize = new Vector2(132f, 55f);
    [Tooltip("Extra pixel offset on top of the object's screen projection (fine-tune, avoids covering the object)")]
    public Vector2 screenPixelOffset = new Vector2(0f, 0f);
    [Tooltip("Hide the toggle when the object is behind the camera / out of view")]
    public bool hideWhenOffscreen = true;

    [Header("Follow")]
    [Tooltip("Off by default: position is computed once on entering Operation Mode and stays fixed.\nWhen checked, it is recomputed every frame and the toggle moves / rotates with the object")]
    public bool followTarget = false;

    [Header("Hotkeys")]
    [Tooltip("While the panel is shown, number keys 1~9 trigger the Nth toggle")]
    public bool numberHotkeys = true;

    [Header("Debug (runtime read-only)")]
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
    //  Public API
    // ────────────────────────────────────────────────────────────────────

    /// <summary>Called by OperationModeController when the scene has no instance of this component</summary>
    public static ConsoleTogglePanel CreateDefault()
    {
        var go = new GameObject("ConsoleToggleButtons");
        return go.AddComponent<ConsoleTogglePanel>();
    }

    /// <summary>Show toggles. cam is the Operation Mode camera (used to project objects to screen)</summary>
    public void Show(ConsoleStation console, Camera projectionCamera)
    {
        EnsureBuilt();
        ClearFollowers();

        CurrentConsole = console;
        cam = projectionCamera != null ? projectionCamera : Camera.main;
        if (cam == null) Debug.LogWarning("[Console Toggle] No camera available; toggles can't be positioned", this);

        var ops = console != null ? console.GetOperables() : new ConsoleOperable[0];
        for (int i = 0; i < ops.Length; i++) followers.Add(BuildFollower(ops[i], i));

        buttonCount = followers.Count;
        canvas.gameObject.SetActive(true);
        visible = true;
        Reposition();   // compute position once and pin it (with followTarget off this is final)
    }

    /// <summary>Hide toggles and unsubscribe</summary>
    public void Hide()
    {
        ClearFollowers();
        if (canvas != null) canvas.gameObject.SetActive(false);
        CurrentConsole = null;
        visible = false;
        buttonCount = 0;
    }

    // ────────────────────────────────────────────────────────────────────
    //  Hotkeys / optional follow
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

    // No follow by default: position is computed once in Show() and fixed; nothing to do here.
    // Only recomputed per frame when followTarget is checked.
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

            // followTarget off (default): use the fixed anchor recorded in state A; the button stays put however the bridge moves
            Vector3 sp = cam.WorldToScreenPoint(f.operable.GetButtonAnchor(followTarget));
            bool onscreen = sp.z > 0f &&
                            (!hideWhenOffscreen ||
                             (sp.x >= -toggleSize.x && sp.x <= Screen.width + toggleSize.x &&
                              sp.y >= -toggleSize.y && sp.y <= Screen.height + toggleSize.y));

            f.root.gameObject.SetActive(onscreen);
            if (!onscreen) continue;

            // Screen pixels → Overlay canvas local coords (canvas uses ConstantPixelSize, scaleFactor=1, so they match)
            f.root.position = new Vector3(sp.x + screenPixelOffset.x, sp.y + screenPixelOffset.y, 0f);
        }
    }

    private void OnDisable() => ClearFollowers();

    // ────────────────────────────────────────────────────────────────────
    //  Single floating toggle
    // ────────────────────────────────────────────────────────────────────

    private Follower BuildFollower(ConsoleOperable op, int index)
    {
        var f = new Follower { operable = op };

        var go = new GameObject($"Toggle_{index + 1}_{op.name}", typeof(RectTransform));
        go.transform.SetParent(canvasRect, false);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = toggleSize;
        rt.pivot = new Vector2(0.5f, 0.5f);   // centered on the object's projected point
        f.root = rt;

        f.off = CreateFullImage(rt, "off", toggleOffSprite);
        f.on = CreateFullImage(rt, "on", toggleOnSprite);

        var btn = go.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.targetGraphic = f.off;
        // Exclude from keyboard / gamepad navigation: otherwise arrow keys select it and Submit (Space/Enter) triggers it
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        var captured = op;
        btn.onClick.AddListener(() =>
        {
            captured.Operate();
            // Deselect right after clicking. UGUI remembers the just-clicked Button as the "current selection",
            // and pressing Submit afterwards (Space by default = jump) triggers it again -- this is why "pressing Space moved the bridge"
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
    //  Canvas (built once)
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
        // ConstantPixelSize + scaleFactor 1: canvas local coords == screen pixels, Reposition uses projected pixels directly
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
            Debug.LogWarning($"[Console Toggle] Toggle sprites failed to load: {toggleOffResourcePath} / {toggleOnResourcePath}. " +
                             "Make sure the SCI-FI GUI Pack Resources folder still exists, or drag the two Sprites into the Inspector manually", this);
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        if (FindFirstObjectByType<EventSystem>() != null) return;
        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        Debug.Log("[Console Toggle] No EventSystem in scene; created one automatically (needed for UI clicks)", es);
    }
}
