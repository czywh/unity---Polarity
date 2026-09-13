using UnityEngine;

/// <summary>
/// 激光转向器：被激光射中后，从本装置再发射一条【可调角度 / 可调长度】的新激光。
/// 输出激光【不作为本装置子物体】，方向用"初始朝向"或"入射方向"计算，
/// 因此本装置即使自转（SpinY 装饰），输出激光方向也不受影响。
///
/// 调试：与 LaserTower 一致，OnDrawGizmos 常驻画出输出射线的方向与长度（红线 + 黄色枪口球），
/// 不用选中、不用进 Play 就能对着调 redirectAngle。
/// </summary>
public class LaserRedirector : MonoBehaviour
{
    [Header("输出激光 prefab（带 Hovl_Laser + LaserBarrier）")]
    public GameObject laserPrefab;

    [Header("转向角度")]
    [Tooltip("相对入射方向偏转的角度（度）")]
    public float redirectAngle = 90f;
    [Tooltip("绕哪个轴偏转（一般世界 Up 做水平转向）")]
    public Vector3 rotateAxis = Vector3.up;
    [Tooltip("勾选：忽略入射方向，用【初始 forward】作为固定输出方向（自转也不变）")]
    public bool useFixedDirection = false;

    [Header("输出位置 / 参数")]
    [Tooltip("输出起点相对本装置的偏移（按初始朝向，自转不影响）")]
    public Vector3 muzzleLocalOffset = Vector3.zero;
    [Tooltip("输出激光长度（实时可改）")]
    public float maxLength = 40f;
    public Vector3 laserScale = Vector3.one;

    [Header("持续判定")]
    [Tooltip("被击中后，若这么久没再被击中则关闭输出（秒）")]
    public float sustainTime = 0.2f;

    [Header("调试")]
    [Tooltip("Scene 视图常驻画出输出射线的方向与长度")]
    public bool drawGizmo = true;

    [Header("运行时（只读）")]
    [SerializeField] private bool active;
    [SerializeField] private Vector3 lastIncomingDir;

    private GameObject outputInstance;
    private Hovl_Laser outHovl;
    private LaserBarrier outBarrier;
    private float offUntil;

    // 初始朝向 / 位置（自转不改变它们）
    private Quaternion initialRotation;
    private Vector3 initialForward;
    private bool initialized;

    private void Awake()
    {
        initialRotation = transform.rotation;
        initialForward = transform.forward;
        lastIncomingDir = initialForward;
        initialized = true;
    }

    /// 由 LaserBarrier 在命中本装置时调用，传入入射激光方向（世界）
    public void Hit(Vector3 incomingDir)
    {
        lastIncomingDir = incomingDir.sqrMagnitude > 0.0001f ? incomingDir.normalized : InitialForward;
        offUntil = Time.time + sustainTime;
        if (!active) { active = true; SpawnOutput(); }
        UpdateOutput();
    }

    private void Update()
    {
        if (active && Time.time >= offUntil) { active = false; DespawnOutput(); }
        else if (active) UpdateOutput();
    }

    // ── 方向 / 位置（编辑期 Awake 还没跑，统一用当前 transform 兜底）──

    private Vector3 InitialForward => initialized ? initialForward : transform.forward;
    private Quaternion InitialRotation => initialized ? initialRotation : transform.rotation;

    /// 入射方向：运行时用真实记录值，编辑期用本物体 forward
    private Vector3 IncomingDirection()
    {
        if (initialized && lastIncomingDir.sqrMagnitude > 0.0001f) return lastIncomingDir.normalized;
        return transform.forward;
    }

    /// 输出方向（Gizmo 与运行时共用同一份计算）
    public Vector3 OutputDirection()
    {
        if (useFixedDirection) return InitialForward;   // 固定方向，自转不影响
        Vector3 axis = rotateAxis.sqrMagnitude > 0.0001f ? rotateAxis.normalized : Vector3.up;
        return (Quaternion.AngleAxis(redirectAngle, axis) * IncomingDirection()).normalized;
    }

    /// 输出起点（用初始朝向算偏移，自转时枪口不乱跑）
    public Vector3 MuzzlePosition() => transform.position + InitialRotation * muzzleLocalOffset;

    // 方向与世界 Up 平行时换个 up，避免 LookRotation 刷警告
    private static Quaternion SafeLook(Vector3 dir)
    {
        Vector3 up = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.999f ? Vector3.forward : Vector3.up;
        return Quaternion.LookRotation(dir, up);
    }

    // ── 输出激光实例 ──

    private void SpawnOutput()
    {
        if (laserPrefab == null) { Debug.LogWarning("[LaserRedirector] 未指定 laserPrefab", this); active = false; return; }
        // 不设父物体 → 不继承本装置的自转 / 缩放
        outputInstance = Instantiate(laserPrefab, MuzzlePosition(), SafeLook(OutputDirection()), null);
        outputInstance.transform.localScale = laserScale;
        outputInstance.SetActive(true);
        outHovl = outputInstance.GetComponentInChildren<Hovl_Laser>();
        outBarrier = outputInstance.GetComponentInChildren<LaserBarrier>();
        UpdateOutput();
    }

    private void UpdateOutput()
    {
        if (outputInstance == null) return;
        outputInstance.transform.position = MuzzlePosition();
        outputInstance.transform.rotation = SafeLook(OutputDirection());
        outputInstance.transform.localScale = laserScale;
        // 每帧同步长度 → 运行时改 maxLength 立即生效
        if (outHovl != null) outHovl.MaxLength = maxLength;
        if (outBarrier != null) outBarrier.maxLength = maxLength;
    }

    private void DespawnOutput()
    {
        if (outputInstance != null) Destroy(outputInstance);
        outputInstance = null; outHovl = null; outBarrier = null;
    }

    private void OnDestroy() => DespawnOutput();

    // ── Gizmo：与 LaserTower 同一套画法 ──

    private void OnDrawGizmos()
    {
        if (!drawGizmo) return;

        Vector3 pos = MuzzlePosition();
        Vector3 fwd = OutputDirection();

        Gizmos.color = Color.red;
        Gizmos.DrawLine(pos, pos + fwd * maxLength);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(pos, 0.15f);
    }
}