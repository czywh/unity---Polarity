using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// CORE  Operation Mode manager (with debug logging). ConsoleStation calls Enter() when F is pressed:
///   - Saves the camera state at the moment F was pressed (position / rotation / projection / size) so ESC can restore it;
///   - Disables OrbitFollowCamera (camera detaches from mouse control and stays put);
///   - Disables CharacterSwitcher (Q switching stops working) + locks all player / robot control scripts;
///   - Moves the camera to the console viewpoint, switches projection to Orthographic + the given size.
///   - Shows ConsoleTogglePanel: a column of toggle buttons on the right side of the screen, one per ConsoleOperable; clicking triggers it.
/// ESC calls Exit(): hides the panel, restores the camera (back to Perspective + original viewpoint), unfreezes controls, hands Q switching back.
///
/// Two ways to trigger operables:
///   - Panel buttons (default, recommended) -- handled by ConsoleTogglePanel;
///   - Raycast-clicking objects (legacy) -- only enabled when clickToOperate is checked, and never fires while the mouse is over UI.
///
/// Debug: with verboseLog checked, every enter/exit/click step is printed to the Console, handy for tracking down "click does nothing" issues.
/// </summary>
public class OperationModeController : MonoBehaviour
{
    [Header("References (auto-found if left empty)")]
    [SerializeField] private Camera cam;
    [SerializeField] private OrbitFollowCamera orbitCamera;
    [SerializeField] private CharacterSwitcher switcher;
    [Tooltip("Robot fixed-axis movement component enabled in Operation Mode (auto-found if left empty)")]
    [SerializeField] private RobotConsoleMover robotConsoleMover;
    [Tooltip("Toggle panel shown in Operation Mode (auto-found if empty; if the scene has none, a default one is created at runtime)")]
    [SerializeField] private ConsoleTogglePanel togglePanel;

    [Header("Input")]
    public KeyCode exitKey = KeyCode.Escape;

    [Header("Cursor")]
    [Tooltip("Whether to show the mouse cursor in Operation Mode (required for clicking objects)")]
    public bool showCursorInMode = true;

    [Header("Operation Mode Click (legacy: raycast objects)")]
    [Tooltip("Allow clicking operables in the scene directly with a mouse raycast. Off by default; use the panel buttons instead")]
    public bool clickToOperate = false;
    [Tooltip("Layers of clickable objects; ideally only check the operables' layers")]
    public LayerMask clickMask = ~0;
    [Tooltip("Max click ray distance")]
    public float clickMaxDistance = 5000f;
    [Tooltip("Draw the click ray in the Scene view for this many seconds (red = miss, green = hit)")]
    public float debugRayDuration = 5f;
    [Tooltip("Length of the drawn ray (visualization only, does not affect the actual detection distance)")]
    public float debugRayDrawLength = 300f;

    [Header("Debug")]
    [Tooltip("When on, prints detailed enter/exit/click logs to the Console")]
    public bool verboseLog = true;

    [Header("Debug (runtime read-only)")]
    [SerializeField] private bool inOperationMode;

    public bool InOperationMode => inOperationMode;
    public ConsoleStation CurrentConsole { get; private set; }

    // Saved camera / cursor state (when F was pressed)
    private Vector3 savedPos;
    private Quaternion savedRot;
    private bool savedOrtho;
    private float savedOrthoSize;
    private float savedFov;
    private CursorLockMode savedCursorLock;
    private bool savedCursorVisible;

    private void Awake()
    {
        if (cam == null) cam = Camera.main;
        if (orbitCamera == null && cam != null) orbitCamera = cam.GetComponent<OrbitFollowCamera>();
        if (switcher == null) switcher = FindFirstObjectByType<CharacterSwitcher>();
        if (robotConsoleMover == null) robotConsoleMover = FindFirstObjectByType<RobotConsoleMover>(FindObjectsInactive.Include);
        if (togglePanel == null) togglePanel = FindFirstObjectByType<ConsoleTogglePanel>(FindObjectsInactive.Include);
        if (togglePanel == null) togglePanel = ConsoleTogglePanel.CreateDefault();

        // Startup self-check: print whether key references are in place
        Log($"Awake self-check -> cam={(cam ? cam.name : "null")}, " +
            $"orbitCamera={(orbitCamera ? "OK" : "null")}, " +
            $"switcher={(switcher ? "OK" : "null")}");
        if (cam == null)
            Debug.LogWarning("[OperationMode] Camera.main is null! Make sure Main Camera's Tag = MainCamera, or drag the camera into the Cam slot manually.", this);
    }

    private void Update()
    {
        if (!inOperationMode) return;

        if (Input.GetKeyDown(exitKey)) { Exit(); return; }

        // Legacy: raycast objects. Skip when the mouse is over UI, so clicking a panel button doesn't also hit a scene object
        if (clickToOperate && Input.GetMouseButtonDown(0) && !IsPointerOverUI())
            TryClickOperable();
    }

    private static bool IsPointerOverUI()
        => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    // Cast a ray from the mouse position; call Operate() on the operable it hits
    private void TryClickOperable()
    {
        if (cam == null)
        {
            Debug.LogWarning("[OperationMode] Click failed: cam is null (Camera.main not found)", this);
            return;
        }

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        Log($"Click ray: origin={ray.origin}, dir={ray.direction}, mouse={Input.mousePosition}");

        bool hitSomething = Physics.Raycast(ray, out RaycastHit hit, clickMaxDistance, clickMask, QueryTriggerInteraction.Ignore);

        // -- Visualize the ray (visible in Scene view) --
        //   Hit -> green line to the hit point, plus a small cross at the hit point
        //   Miss -> red line along the direction
        if (hitSomething)
        {
            Debug.DrawLine(ray.origin, hit.point, Color.green, debugRayDuration);
            DrawHitCross(hit.point, 1.5f, Color.green, debugRayDuration);
        }
        else
        {
            Debug.DrawRay(ray.origin, ray.direction * debugRayDrawLength, Color.red, debugRayDuration);
        }

        if (hitSomething)
        {
            var op = hit.collider.GetComponentInParent<IConsoleOperable>();
            if (op != null)
            {
                Log($"Hit <color=lime>{hit.collider.name}</color> @ {hit.point} -> found IConsoleOperable, calling Operate()");
                op.Operate();
            }
            else
            {
                Debug.Log($"[OperationMode] Hit {hit.collider.name} @ {hit.point}, but it (and its parents) has no IConsoleOperable component", hit.collider);
            }
        }
        else
        {
            Debug.Log("[OperationMode] Ray hit nothing -- check the red line's direction in the Scene view: if it doesn't pass through the target, the viewpoint/crosshair is misaligned; if it passes through but misses, the object has no collider or its layer isn't in Click Mask");
        }
    }

    // Draw a small 3-axis cross at the hit point to make it easy to see in the Scene
    private static void DrawHitCross(Vector3 p, float size, Color c, float dur)
    {
        Debug.DrawLine(p - Vector3.right * size, p + Vector3.right * size, c, dur);
        Debug.DrawLine(p - Vector3.up * size, p + Vector3.up * size, c, dur);
        Debug.DrawLine(p - Vector3.forward * size, p + Vector3.forward * size, c, dur);
    }

    public void Enter(ConsoleStation console)
    {
        if (inOperationMode) { Log("Enter ignored: already in Operation Mode"); return; }
        if (console == null) { Debug.LogWarning("[OperationMode] Enter failed: console is null", this); return; }
        if (cam == null) { Debug.LogWarning("[OperationMode] Enter failed: cam is null (Camera.main not found)", this); return; }

        inOperationMode = true;
        CurrentConsole = console;
        Log($"<color=cyan>Entering Operation Mode</color>, console = {console.name}");

        // Save the camera state at the moment F was pressed (for ESC restore)
        savedPos = cam.transform.position;
        savedRot = cam.transform.rotation;
        savedOrtho = cam.orthographic;
        savedOrthoSize = cam.orthographicSize;
        savedFov = cam.fieldOfView;
        savedCursorLock = Cursor.lockState;
        savedCursorVisible = Cursor.visible;

        // Freeze: detach camera from mouse control, disable Q switching, lock player/robot movement
        if (orbitCamera != null) orbitCamera.enabled = false;
        else Log("Note: orbitCamera is null, the camera won't be disabled (mouse may still rotate the view)");

        if (switcher != null)
        {
            switcher.SetExternallyFrozen(true);   // External freeze: controls won't be re-enabled by mistake even if a character dies meanwhile
            switcher.enabled = false;             // Stop Q detection
        }
        else Log("Note: switcher is null, Q switching / character freeze won't take effect");

        // Operation Mode: enable robot fixed-axis movement (WASD drives world axes directly)
        if (robotConsoleMover != null) robotConsoleMover.enabled = true;

        // Move camera to the console viewpoint + orthographic
        console.GetCameraPose(out Vector3 pos, out Quaternion rot, out float size);
        cam.transform.SetPositionAndRotation(pos, rot);
        cam.orthographic = true;
        cam.orthographicSize = size;
        Log($"Camera moved to viewpoint pos={pos}, euler={rot.eulerAngles}, orthoSize={size}");

        if (showCursorInMode)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        Log($"Cursor: lockState={Cursor.lockState}, visible={Cursor.visible} (showCursorInMode={showCursorInMode})");

        // Show the toggle panel
        if (togglePanel != null) togglePanel.Show(console, cam);
        else Log("Note: togglePanel is null, the operation panel won't be shown");
    }

    public void Exit()
    {
        if (!inOperationMode) { Log("Exit ignored: not currently in Operation Mode"); return; }
        if (cam == null) { Debug.LogWarning("[OperationMode] Exit failed: cam is null", this); return; }

        Log("<color=cyan>Exiting Operation Mode</color>");

        // Hide the panel first
        if (togglePanel != null) togglePanel.Hide();

        // Restore camera (perspective + FOV + viewpoint from when F was pressed)
        cam.orthographic = savedOrtho;
        cam.orthographicSize = savedOrthoSize;
        cam.fieldOfView = savedFov;
        cam.transform.SetPositionAndRotation(savedPos, savedRot);

        // Restore cursor
        Cursor.lockState = savedCursorLock;
        Cursor.visible = savedCursorVisible;

        // Turn off Operation Mode's fixed-axis movement
        if (robotConsoleMover != null) robotConsoleMover.enabled = false;

        // Unfreeze
        if (orbitCamera != null) orbitCamera.enabled = true;
        if (switcher != null)
        {
            switcher.enabled = true;
            switcher.SetExternallyFrozen(false);   // Release external freeze (internally calls RefreshControlState to hand control back)
        }

        inOperationMode = false;
        CurrentConsole = null;
    }

    private static void SetArray(MonoBehaviour[] arr, bool on)
    {
        if (arr == null) return;
        foreach (var s in arr) if (s != null) s.enabled = on;
    }

    private void Log(string msg)
    {
        if (verboseLog) Debug.Log($"[OperationMode] {msg}", this);
    }
}