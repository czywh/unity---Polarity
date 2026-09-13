using UnityEngine;

/// <summary>
/// CORE　操作模式管理器（带调试日志版）。ConsoleStation 按 F 时调 Enter()：
///   · 保存 F 按下时的相机状态（位置 / 角度 / 投影 / 尺寸），用于 ESC 还原；
///   · 禁用 OrbitFollowCamera（相机脱离鼠标控制、保持不动）；
///   · 禁用 CharacterSwitcher（Q 切换失效）+ 锁住玩家 / 机器人所有控制脚本；
///   · 相机移到操作台机位，投影切 Orthographic + 指定 size。
/// 按 ESC 调 Exit()：还原相机（切回 Perspective + 原机位）、解冻控制、交回 Q 切换。
///
/// 调试：勾 verboseLog 后，进入/退出/点击各步骤都会在 Console 打印，方便定位"点击无效"等问题。
/// </summary>
public class OperationModeController : MonoBehaviour
{
    [Header("引用（留空自动查找）")]
    [SerializeField] private Camera cam;
    [SerializeField] private OrbitFollowCamera orbitCamera;
    [SerializeField] private CharacterSwitcher switcher;
    [Tooltip("操作模式下启用的机器人固定轴向移动组件（留空自动查找）")]
    [SerializeField] private RobotConsoleMover robotConsoleMover;

    [Header("输入")]
    public KeyCode exitKey = KeyCode.Escape;

    [Header("光标")]
    [Tooltip("操作模式下是否显示鼠标光标（点击物体需要开启）")]
    public bool showCursorInMode = true;

    [Header("操作模式点击")]
    [Tooltip("可点击物体所在层；建议只勾可操作物的层")]
    public LayerMask clickMask = ~0;
    [Tooltip("点击射线最大距离")]
    public float clickMaxDistance = 5000f;
    [Tooltip("在 Scene 视图画出点击射线，持续这么多秒（红=没中，绿=命中）")]
    public float debugRayDuration = 5f;
    [Tooltip("画射线的长度（仅可视化用，不影响实际检测距离）")]
    public float debugRayDrawLength = 300f;

    [Header("调试")]
    [Tooltip("打开后在 Console 打印进入/退出/点击的详细日志")]
    public bool verboseLog = true;

    [Header("调试（运行时只读）")]
    [SerializeField] private bool inOperationMode;

    public bool InOperationMode => inOperationMode;
    public ConsoleStation CurrentConsole { get; private set; }

    // 保存的相机 / 光标状态（F 按下时）
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

        // 启动自检：把关键引用是否就位打印出来
        Log($"Awake 自检 → cam={(cam ? cam.name : "null")}, " +
            $"orbitCamera={(orbitCamera ? "OK" : "null")}, " +
            $"switcher={(switcher ? "OK" : "null")}");
        if (cam == null)
            Debug.LogWarning("[操作模式] Camera.main 为空！确认 Main Camera 的 Tag = MainCamera，或手动把相机拖到 Cam 槽。", this);
    }

    private void Update()
    {
        if (!inOperationMode) return;

        if (Input.GetKeyDown(exitKey)) { Exit(); return; }

        if (Input.GetMouseButtonDown(0)) TryClickOperable();
    }

    // 从鼠标位置发射线，命中的可操作物调 Operate()
    private void TryClickOperable()
    {
        if (cam == null)
        {
            Debug.LogWarning("[操作模式] 点击失败：cam 为空（Camera.main 没找到）", this);
            return;
        }

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        Log($"点击射线：origin={ray.origin}, dir={ray.direction}, mouse={Input.mousePosition}");

        bool hitSomething = Physics.Raycast(ray, out RaycastHit hit, clickMaxDistance, clickMask, QueryTriggerInteraction.Ignore);

        // —— 可视化射线（Scene 视图可见）——
        //   命中 → 绿线画到命中点，命中点再画个小十字
        //   没中 → 红线沿方向画一段
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
                Log($"点中 <color=lime>{hit.collider.name}</color> @ {hit.point} → 找到 IConsoleOperable，调用 Operate()");
                op.Operate();
            }
            else
            {
                Debug.Log($"[操作模式] 点中 {hit.collider.name} @ {hit.point}，但它（及父级）没有 IConsoleOperable 组件", hit.collider);
            }
        }
        else
        {
            Debug.Log("[操作模式] 射线没打中任何东西 —— 看 Scene 视图里的红线朝向：若没穿过目标物体，就是机位/准星没对准；若穿过了却没中，就是该物体没碰撞体或层不在 Click Mask");
        }
    }

    // 在命中点画一个三轴小十字，便于在 Scene 里看清位置
    private static void DrawHitCross(Vector3 p, float size, Color c, float dur)
    {
        Debug.DrawLine(p - Vector3.right * size, p + Vector3.right * size, c, dur);
        Debug.DrawLine(p - Vector3.up * size, p + Vector3.up * size, c, dur);
        Debug.DrawLine(p - Vector3.forward * size, p + Vector3.forward * size, c, dur);
    }

    public void Enter(ConsoleStation console)
    {
        if (inOperationMode) { Log("Enter 被忽略：已在操作模式中"); return; }
        if (console == null) { Debug.LogWarning("[操作模式] Enter 失败：console 为空", this); return; }
        if (cam == null) { Debug.LogWarning("[操作模式] Enter 失败：cam 为空（Camera.main 没找到）", this); return; }

        inOperationMode = true;
        CurrentConsole = console;
        Log($"<color=cyan>进入操作模式</color>，操作台 = {console.name}");

        // 保存 F 按下时的相机状态（供 ESC 还原）
        savedPos = cam.transform.position;
        savedRot = cam.transform.rotation;
        savedOrtho = cam.orthographic;
        savedOrthoSize = cam.orthographicSize;
        savedFov = cam.fieldOfView;
        savedCursorLock = Cursor.lockState;
        savedCursorVisible = Cursor.visible;

        // 冻结：相机脱离鼠标控制、禁用 Q 切换、锁住玩家/机器人移动
        if (orbitCamera != null) orbitCamera.enabled = false;
        else Log("提示：orbitCamera 为空，相机不会被禁用（鼠标可能仍能转视角）");

        if (switcher != null)
        {
            switcher.SetExternallyFrozen(true);   // 外部冻结：期间即使角色死亡，控制也不会被误开
            switcher.enabled = false;             // 停掉 Q 检测
        }
        else Log("提示：switcher 为空，Q 切换 / 角色冻结不会生效");

        // 操作模式：启用机器人固定轴向移动（WASD 直接控世界轴）
        if (robotConsoleMover != null) robotConsoleMover.enabled = true;

        // 相机切到操作台机位 + 正交
        console.GetCameraPose(out Vector3 pos, out Quaternion rot, out float size);
        cam.transform.SetPositionAndRotation(pos, rot);
        cam.orthographic = true;
        cam.orthographicSize = size;
        Log($"相机切到机位 pos={pos}, euler={rot.eulerAngles}, orthoSize={size}");

        if (showCursorInMode)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        Log($"光标：lockState={Cursor.lockState}, visible={Cursor.visible}（showCursorInMode={showCursorInMode}）");
    }

    public void Exit()
    {
        if (!inOperationMode) { Log("Exit 被忽略：当前不在操作模式"); return; }
        if (cam == null) { Debug.LogWarning("[操作模式] Exit 失败：cam 为空", this); return; }

        Log("<color=cyan>退出操作模式</color>");

        // 还原相机（透视 + FOV + F 时的机位）
        cam.orthographic = savedOrtho;
        cam.orthographicSize = savedOrthoSize;
        cam.fieldOfView = savedFov;
        cam.transform.SetPositionAndRotation(savedPos, savedRot);

        // 还原光标
        Cursor.lockState = savedCursorLock;
        Cursor.visible = savedCursorVisible;

        // 关掉操作模式的固定轴向移动
        if (robotConsoleMover != null) robotConsoleMover.enabled = false;

        // 解冻
        if (orbitCamera != null) orbitCamera.enabled = true;
        if (switcher != null)
        {
            switcher.enabled = true;
            switcher.SetExternallyFrozen(false);   // 解除外部冻结（内部会 RefreshControlState 交回控制）
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
        if (verboseLog) Debug.Log($"[操作模式] {msg}", this);
    }
}