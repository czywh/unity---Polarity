using UnityEngine;

/// <summary>
/// PROP　操作模式下可点击的物体：点击在 A、B 两状态间切换，并【平滑过渡】。
/// 可驱动位置、旋转，或两者同时：
///   · 位置：positionA ↔ positionB（如 path3 的 (49,-25.78,-64) ↔ (72,-25.78,-64)）
///   · 旋转：eulerA ↔ eulerB（如 path1 的 (0,0,0) ↔ (0,-90,0)）
/// 点击只切换目标状态，移动/旋转由 Update 逐帧逼近（"逐渐变成"）。
///
/// 触发方式：操作模式面板（ConsoleTogglePanel）上的 toggle 按钮调 Operate()；
/// 旧的"射线点中物体"方式仍保留在 OperationModeController.clickToOperate 开关下。
/// 状态变化通过 StateChanged 事件广播，面板据此切换 on/off 图。
/// </summary>
[DisallowMultipleComponent]
public class ConsoleOperable : MonoBehaviour, IConsoleOperable
{
    [Header("位置（勾选才驱动）")]
    public bool drivePosition = false;
    public bool positionIsLocal = false;   // 世界坐标 / 局部坐标
    public Vector3 positionA;
    public Vector3 positionB;
    [Tooltip("位移速度（单位/秒）")]
    public float moveSpeed = 8f;

    [Header("旋转（勾选才驱动）")]
    public bool driveRotation = false;
    public bool rotationIsLocal = true;
    public Vector3 eulerA;
    public Vector3 eulerB;
    [Tooltip("旋转速度（度/秒）")]
    public float rotateSpeed = 180f;

    [Header("开局吸附到 A")]
    public bool snapToAOnStart = true;

    [Header("操作台按钮（贴在物体旁的 on/off 开关）")]
    [Tooltip("按钮显示的名字；留空用物体名")]
    public string displayName;
    [Tooltip("按钮锚点：拖一个空子物体摆到想让按钮出现的位置。\n作为子物体会随本物体一起平移/旋转，按钮就跟着走。留空则用下面的局部偏移")]
    public Transform buttonAnchor;
    [Tooltip("没有锚点时用这个：相对本物体的局部偏移。\n用 TransformPoint 换算，所以物体旋转/移动时锚点跟着转")]
    public Vector3 buttonLocalOffset = new Vector3(0f, 2f, 0f);

    [Header("调试（运行时只读）")]
    [SerializeField] private int state;   // 0 = A，1 = B

    /// <summary>当前目标状态：0 = A，1 = B</summary>
    public int State => state;
    /// <summary>是否处于 B 状态（面板上显示为 ON）</summary>
    public bool IsAtB => state == 1;
    /// <summary>面板显示名</summary>
    public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

    /// <summary>按钮锚点的【实时】世界坐标（随物体当前位置/朝向变化）。仅 followTarget 时用</summary>
    public Vector3 ButtonAnchorWorld =>
        buttonAnchor != null ? buttonAnchor.position : transform.TransformPoint(buttonLocalOffset);

    /// <summary>
    /// 按钮锚点的【固定】世界坐标：Start() 里物体吸附到 A 状态之后算一次，之后再也不变。
    /// 无论物体之后移到 B 还是转到哪，按钮都钉在这个位置 —— 这是默认使用的锚点。
    /// </summary>
    public Vector3 ButtonAnchorFixed { get; private set; }
    private bool anchorCached;

    /// <summary>目标状态改变时触发（参数是自己）。按钮订阅它来刷新 on/off 图</summary>
    public event System.Action<ConsoleOperable> StateChanged;

    private void Start()
    {
        state = 0;
        if (snapToAOnStart) SnapToState(0);
        CacheFixedAnchor();
    }

    /// 记录按钮的固定落点。默认在 Start（已吸附到 A）时调用；改了锚点想重算可手动再调
    public void CacheFixedAnchor()
    {
        ButtonAnchorFixed = ButtonAnchorWorld;
        anchorCached = true;
    }

    /// 面板取锚点用：还没跑过 Start（比如刚 Instantiate）就临时用实时值兜底
    public Vector3 GetButtonAnchor(bool follow) =>
        follow || !anchorCached ? ButtonAnchorWorld : ButtonAnchorFixed;

    // 被触发：切换到另一状态（Update 会平滑过渡过去）
    public void Operate()
    {
        SetState(1 - state);
    }

    /// <summary>直接指定目标状态（0 = A，1 = B）。相同则不触发事件</summary>
    public void SetState(int s)
    {
        s = Mathf.Clamp(s, 0, 1);
        if (s == state) return;
        state = s;
        StateChanged?.Invoke(this);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        bool moved = false;

        if (drivePosition)
        {
            Vector3 tp = state == 0 ? positionA : positionB;
            if (positionIsLocal)
            {
                Vector3 prev = transform.localPosition;
                transform.localPosition = Vector3.MoveTowards(prev, tp, moveSpeed * dt);
                if ((transform.localPosition - prev).sqrMagnitude > 1e-8f) moved = true;
            }
            else
            {
                Vector3 prev = transform.position;
                transform.position = Vector3.MoveTowards(prev, tp, moveSpeed * dt);
                if ((transform.position - prev).sqrMagnitude > 1e-8f) moved = true;
            }
        }

        if (driveRotation)
        {
            Quaternion tr = Quaternion.Euler(state == 0 ? eulerA : eulerB);
            if (rotationIsLocal)
            {
                Quaternion prev = transform.localRotation;
                transform.localRotation = Quaternion.RotateTowards(prev, tr, rotateSpeed * dt);
                if (Quaternion.Angle(prev, transform.localRotation) > 0.001f) moved = true;
            }
            else
            {
                Quaternion prev = transform.rotation;
                transform.rotation = Quaternion.RotateTowards(prev, tr, rotateSpeed * dt);
                if (Quaternion.Angle(prev, transform.rotation) > 0.001f) moved = true;
            }
        }

        // 移动/旋转中通知网格进入高频扫描
        if (moved && GridSystem.Instance != null) GridSystem.Instance.NotifyMoving();
    }

    private void SnapToState(int s)
    {
        if (drivePosition)
        {
            Vector3 p = s == 0 ? positionA : positionB;
            if (positionIsLocal) transform.localPosition = p; else transform.position = p;
        }
        if (driveRotation)
        {
            Quaternion r = Quaternion.Euler(s == 0 ? eulerA : eulerB);
            if (rotationIsLocal) transform.localRotation = r; else transform.rotation = r;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (drivePosition)
        {
            Vector3 a = positionIsLocal && transform.parent ? transform.parent.TransformPoint(positionA) : positionA;
            Vector3 b = positionIsLocal && transform.parent ? transform.parent.TransformPoint(positionB) : positionB;
            Gizmos.color = Color.green; Gizmos.DrawWireSphere(a, 0.3f);
            Gizmos.color = Color.cyan;  Gizmos.DrawWireSphere(b, 0.3f);
            Gizmos.color = Color.yellow; Gizmos.DrawLine(a, b);
        }
    }
}