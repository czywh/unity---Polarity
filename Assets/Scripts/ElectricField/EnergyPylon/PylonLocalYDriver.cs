using UnityEngine;

/// <summary>
/// PROP　把某个 ElectricEntity（通常是吸电桩 EnergyPylon）的电量百分比，
/// 映射到目标 Transform 的【局部 Y 坐标】：
///   电量 0   → 目标 localPosition.y = yAtEmpty
///   电量 满  → 目标 localPosition.y = yAtFull
///   中间     → 按 Fraction 线性插值
///
/// 适合"吸电桩电量驱动子物体（如 Disruptor）沿 Y 轴升降"的表现。
/// X / Z 保持目标原有的局部坐标不变。
/// </summary>
[DisallowMultipleComponent]
public class PylonLocalYDriver : MonoBehaviour
{
    [Header("电量源（留空取本物体或父级的 ElectricEntity）")]
    [SerializeField] private ElectricEntity energySource;

    [Header("被驱动的目标（留空取本物体）")]
    [Tooltip("要升降的子物体，如 Disruptor")]
    [SerializeField] private Transform target;

    [Header("局部 Y 端点")]
    [Tooltip("电量为 0 时的局部 Y")]
    public float yAtEmpty = -1.5f;
    [Tooltip("电量充满时的局部 Y")]
    public float yAtFull = 0.4f;

    [Header("平滑（可选）")]
    [Tooltip("每秒最大移动速度；0 = 直接对齐（电量本已渐变，一般用 0）")]
    public float moveSpeed = 0f;

    [Header("调试（运行时只读）")]
    [SerializeField] private float fractionReadout;
    [SerializeField] private float currentY;

    private void Awake()
    {
        if (energySource == null) energySource = GetComponentInParent<ElectricEntity>();
        if (target == null) target = transform;
        if (energySource == null)
            Debug.LogWarning("PylonLocalYDriver: 未找到 ElectricEntity 电量源", this);
    }

    private void Start()
    {
        if (energySource != null && target != null)
            SetY(Mathf.Lerp(yAtEmpty, yAtFull, Mathf.Clamp01(energySource.Fraction)), instant: true);
    }

    // LateUpdate：读到本帧充放电后的最新电量
    private void LateUpdate()
    {
        if (energySource == null || target == null) return;

        float f = Mathf.Clamp01(energySource.Fraction);
        fractionReadout = f;

        float targetY = Mathf.Lerp(yAtEmpty, yAtFull, f);
        SetY(targetY, instant: moveSpeed <= 0f);
    }

    private void SetY(float y, bool instant)
    {
        Vector3 lp = target.localPosition;
        float prevY = lp.y;
        lp.y = instant ? y : Mathf.MoveTowards(lp.y, y, moveSpeed * Time.deltaTime);
        target.localPosition = lp;
        currentY = lp.y;

        // 位置有变化 → 通知网格进入高频扫描
        if (Mathf.Abs(lp.y - prevY) > 1e-5f && GridSystem.Instance != null)
            GridSystem.Instance.NotifyMoving();
    }
}