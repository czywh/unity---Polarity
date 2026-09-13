using UnityEngine;

/// <summary>
/// PROP　操作模式下可点击的物体：点击在 A、B 两状态间切换，并【平滑过渡】。
/// 可驱动位置、旋转，或两者同时：
///   · 位置：positionA ↔ positionB（如 path3 的 (49,-25.78,-64) ↔ (72,-25.78,-64)）
///   · 旋转：eulerA ↔ eulerB（如 path1 的 (0,0,0) ↔ (0,-90,0)）
/// 点击只切换目标状态，移动/旋转由 Update 逐帧逼近（"逐渐变成"）。
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

    [Header("调试（运行时只读）")]
    [SerializeField] private int state;   // 0 = A，1 = B

    private void Start()
    {
        state = 0;
        if (snapToAOnStart) SnapToState(0);
    }

    // 被点击：切换到另一状态（Update 会平滑过渡过去）
    public void Operate()
    {
        state = 1 - state;
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