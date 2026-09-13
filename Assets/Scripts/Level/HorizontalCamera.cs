using UnityEngine;

/// <summary>
/// 2.5D 跟拍相机：平滑跟随一个目标 (target)，按住右键可上下俯仰。
/// 只负责"怎么跟"，不关心当前控制的是谁——切换焦点由 CharacterSwitcher 调用 SetTarget 完成。
/// 挂在 Main Camera 上。
/// </summary>
public class HorizontalCamera : MonoBehaviour
{
    [Header("基础跟随设置")]
    public Transform target;                            // 跟随目标（一般是角色身上的焦点空物体）
    public float smoothSpeed = 5f;                      // 位置 / 注视点跟随平滑度（切换时也用它滑过去）
    public Vector3 baseOffset = new Vector3(0, 0, -5f); // 基础偏移（x 用于侧移，z 在下方由距离覆盖）

    [Header("鼠标俯仰控制")]
    public bool mouseTiltEnabled = true;    // 是否启用鼠标俯仰（按住右键）
    public float mouseSensitivity = 1f;     // 鼠标灵敏度
    public float minPitchAngle = -15f;      // 最小俯仰角（向下）
    public float maxPitchAngle = 45f;       // 最大俯仰角（向上）
    public float rotationSmoothness = 5f;   // 俯仰过渡平滑度

    [Header("距离设置")]
    public float followDistance = 5f;       // 固定跟随距离
    public float heightOffset = 2f;         // 相机高度偏移

    [Header("边界限制")]
    public bool useBounds = false;
    public float minX = -10f, maxX = 10f;
    public float minY = -5f, maxY = 5f;
    public float minZ = -20f, maxZ = 20f;

    private float currentPitchAngle = 0f;
    private Vector3 currentOffset;
    private Vector3 currentLookPoint;

    void Start()
    {
        if (target == null) return;
        currentPitchAngle = 0f;
        currentOffset = CalculateOffset(currentPitchAngle);
        currentLookPoint = GetLookPoint();
    }

    void LateUpdate()
    {
        if (target == null) return;
        HandleMouseInput();
        UpdateCameraPosition();
    }

    void HandleMouseInput()
    {
        if (!mouseTiltEnabled) return;

        // 按住右键滑动鼠标控制俯仰（左键留给机器人导弹瞄准，不冲突）
        if (Input.GetMouseButton(1))
        {
            float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;
            currentPitchAngle = Mathf.Clamp(currentPitchAngle + mouseY, minPitchAngle, maxPitchAngle);
        }
    }

    Vector3 CalculateOffset(float pitchAngle)
    {
        float verticalOffset = Mathf.Sin(pitchAngle * Mathf.Deg2Rad) * followDistance;
        float horizontalOffset = Mathf.Cos(pitchAngle * Mathf.Deg2Rad) * followDistance;
        return new Vector3(baseOffset.x, heightOffset + verticalOffset, -horizontalOffset);
    }

    Vector3 GetLookPoint()
    {
        return target.position + Vector3.up * heightOffset * 0.5f;
    }

    void UpdateCameraPosition()
    {
        Vector3 targetOffset = CalculateOffset(currentPitchAngle);
        currentOffset = Vector3.Lerp(currentOffset, targetOffset, rotationSmoothness * Time.deltaTime);

        Vector3 targetPosition = target.position + currentOffset;

        if (useBounds)
        {
            targetPosition.x = Mathf.Clamp(targetPosition.x, minX, maxX);
            targetPosition.y = Mathf.Clamp(targetPosition.y, minY, maxY);
            targetPosition.z = Mathf.Clamp(targetPosition.z, minZ, maxZ);
        }

        // 位置平滑跟随：切换目标时，相机就是靠这一步从旧角色滑向新角色
        transform.position = Vector3.Lerp(transform.position, targetPosition, smoothSpeed * Time.deltaTime);

        // 注视点也平滑，避免切换瞬间镜头硬转
        currentLookPoint = Vector3.Lerp(currentLookPoint, GetLookPoint(), smoothSpeed * Time.deltaTime);
        transform.LookAt(currentLookPoint);
    }

    /// <summary>
    /// 切换跟随目标。instant=true 立即对准（用于初始化）；
    /// instant=false 保持平滑过渡（角色切换时镜头滑向新目标）。
    /// </summary>
    public void SetTarget(Transform newTarget, bool instant = false)
    {
        target = newTarget;
        if (target == null) return;

        if (instant)
        {
            currentOffset = CalculateOffset(currentPitchAngle);
            transform.position = target.position + currentOffset;
            currentLookPoint = GetLookPoint();
            transform.LookAt(currentLookPoint);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (!useBounds) return;
        Gizmos.color = Color.cyan;
        Vector3 center = new Vector3((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);
        Vector3 size = new Vector3(maxX - minX, maxY - minY, maxZ - minZ);
        Gizmos.DrawWireCube(center, size);
    }
}