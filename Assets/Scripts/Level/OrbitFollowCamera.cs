using UnityEngine;

/// <summary>
/// 环绕跟随相机：合并了
///   · HorizontalCamera 的「平滑跟随目标 + SetTarget 切换焦点」
///   · FreeLookCamera 的「鼠标环绕(水平+垂直) + 滚轮缩放 + 遮挡规避」
///
/// 挂在 Main Camera 上，取代 HorizontalCamera。
/// 角色切换仍由 CharacterSwitcher 调用 SetTarget 完成（接口不变）。
/// </summary>
public class OrbitFollowCamera : MonoBehaviour
{
    [Header("跟随目标")]
    public Transform target;
    [Tooltip("注视点相对目标的偏移（一般抬到胸口/头部高度）")]
    public Vector3 targetOffset = new Vector3(0f, 1.5f, 0f);
    [Tooltip("焦点跟随平滑度：切换角色时镜头滑向新目标的速度")]
    public float followSmooth = 8f;

    [Header("鼠标环绕")]
    [Tooltip("勾上=按住右键拖动才环绕（推荐，避免和光标瞄准冲突）；取消=移动鼠标即环绕(FreeLook 风格)")]
    public bool holdRightMouseToRotate = true;
    public float xSpeed = 200f;     // 水平灵敏度
    public float ySpeed = 120f;     // 垂直灵敏度
    public float yMinLimit = -20f;  // 俯仰下限
    public float yMaxLimit = 70f;   // 俯仰上限
    public bool invertY = false;
    [Range(0f, 0.3f)] public float rotationSmoothTime = 0.08f;

    [Header("距离 / 缩放")]
    public float distance = 6f;
    public float minDistance = 3f;
    public float maxDistance = 12f;
    public float zoomSpeed = 2f;

    [Header("遮挡规避")]
    [Tooltip("相机与角色之间有障碍时，把相机拉到障碍前，避免穿墙")]
    public bool avoidOcclusion = true;
    [Tooltip("遮挡检测层；建议只勾环境/地形层，别勾角色层")]
    public LayerMask occlusionMask = ~0;
    public float occlusionPadding = 0.2f;

    [Header("瞄准模式（TPS，由 RobotAimController 控制）")]
    public bool aimMode = false;
    [Tooltip("瞄准时相机贴到角色身后的距离（更近）")]
    public float aimDistance = 3f;
    [Tooltip("进入瞄准时先平缓转到身后，转到此角度内视为到位（然后才拉近）")]
    public float aimAlignAngle = 3f;
    [Tooltip("进入瞄准转向的最长时间（兜底，防止一直转不到位）")]
    public float aimEnterMaxTime = 0.8f;

    [Header("开局初始视角（对应你想要的机位）")]
    [Tooltip("勾上：Start 时用下面的初始 yaw/pitch/距离，而不是读相机当前 Transform 的角度")]
    public bool useInitialView = false;
    [Tooltip("初始水平朝向（= 你想要机位的 Rotation Y）")]
    public float initialYaw = 180f;
    [Tooltip("初始俯仰（= 你想要机位的 Rotation X）")]
    public float initialPitch = 10.84f;
    [Tooltip("初始距离（相机离角色多远）")]
    public float initialDistance = 6f;

    // —— 内部状态 ——
    private float yaw, pitch;
    private float currentDistance, desiredDistance, distVel;
    private Quaternion currentRot, desiredRot;
    private Vector3 smoothedFocus;
    private bool focusInit;
    private bool aimEntering;
    private float aimEnterTimer;

    private void Start()
    {
        if (useInitialView)
        {
            yaw = initialYaw;
            pitch = initialPitch;
            desiredDistance = distance = initialDistance;
        }
        else
        {
            Vector3 e = transform.eulerAngles;
            yaw = e.y;
            pitch = e.x;
            desiredDistance = distance;
        }
        currentRot = desiredRot = Quaternion.Euler(pitch, yaw, 0f);
        currentDistance = desiredDistance;

        if (target != null)
        {
            smoothedFocus = FocusPoint();
            focusInit = true;
        }
    }

    private void LateUpdate()
    {
        if (target == null) return;
        HandleZoom();
        HandleRotation();
        UpdatePosition();
    }

    private Vector3 FocusPoint() => target.position + targetOffset;

    private void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.0001f)
            desiredDistance = Mathf.Clamp(desiredDistance - scroll * zoomSpeed, minDistance, maxDistance);
    }

    private void HandleRotation()
    {
        bool canRotate = !holdRightMouseToRotate || Input.GetMouseButton(1);
        if (canRotate)
        {
            float mx = Input.GetAxis("Mouse X");
            float my = Input.GetAxis("Mouse Y");
            yaw += mx * xSpeed * 0.02f;
            pitch -= my * ySpeed * 0.02f * (invertY ? -1f : 1f);
            pitch = Mathf.Clamp(pitch, yMinLimit, yMaxLimit);
        }

        desiredRot = Quaternion.Euler(pitch, yaw, 0f);

        // 帧率无关的平滑（rotationSmoothTime 越大越柔）
        float t = rotationSmoothTime > 0f ? 1f - Mathf.Exp(-Time.deltaTime / rotationSmoothTime) : 1f;
        currentRot = Quaternion.Slerp(currentRot, desiredRot, t);

        // 进入瞄准：先平缓转到身后，转到位（或超时）后才结束进入阶段
        if (aimEntering)
        {
            aimEnterTimer += Time.deltaTime;
            if (Quaternion.Angle(currentRot, desiredRot) <= aimAlignAngle || aimEnterTimer >= aimEnterMaxTime)
                aimEntering = false;
        }
    }

    private void UpdatePosition()
    {
        // 进入瞄准：先转到身后（保持原距离），转到位后再拉近到 aimDistance
        float distTarget = (aimMode && !aimEntering) ? aimDistance : desiredDistance;
        currentDistance = Mathf.SmoothDamp(currentDistance, distTarget, ref distVel, rotationSmoothTime + 0.05f);

        // 焦点平滑：切换目标时镜头滑过去（HorizontalCamera 的那套手感）
        Vector3 focusTarget = FocusPoint();
        if (!focusInit) { smoothedFocus = focusTarget; focusInit = true; }
        float ft = 1f - Mathf.Exp(-followSmooth * Time.deltaTime);
        smoothedFocus = Vector3.Lerp(smoothedFocus, focusTarget, ft);

        // 环绕定位：焦点 - 旋转×前方×距离（FreeLook 的那套数学）
        Vector3 desiredPos = smoothedFocus - currentRot * Vector3.forward * currentDistance;

        // 遮挡规避：射线从焦点打向相机，撞到障碍就把相机拉到障碍前
        if (avoidOcclusion)
        {
            Vector3 dir = desiredPos - smoothedFocus;
            float dist = dir.magnitude;
            if (dist > 0.001f &&
                Physics.Raycast(smoothedFocus, dir.normalized, out RaycastHit hit, dist,
                                occlusionMask, QueryTriggerInteraction.Ignore))
            {
                desiredPos = hit.point + hit.normal * occlusionPadding;
            }
        }

        transform.rotation = currentRot;
        transform.position = desiredPos;
    }

    /// <summary>
    /// 切换跟随目标（供 CharacterSwitcher 调用）。
    /// instant=true 立即对准（初始化）；false 平滑滑向新目标（角色切换）。
    /// </summary>
    public void SetTarget(Transform newTarget, bool instant = false)
    {
        target = newTarget;
        if (target == null) return;

        if (instant)
        {
            smoothedFocus = FocusPoint();
            focusInit = true;
            currentRot = desiredRot;
            currentDistance = desiredDistance;
            transform.rotation = currentRot;
            transform.position = smoothedFocus - currentRot * Vector3.forward * currentDistance;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (target == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(target.position + targetOffset, 0.25f);
    }

    /// 相机水平朝向（投影到地面）—— 供机器人瞄准时面向准星方向
    public Vector3 AimForward
    {
        get
        {
            Vector3 f = currentRot * Vector3.forward;
            f.y = 0f;
            return f.sqrMagnitude > 0.0001f ? f.normalized : transform.forward;
        }
    }

    /// 瞄准是否已转到位（到位后才拉近、才让角色朝向随相机）
    public bool AimReady => aimMode && !aimEntering;

    /// 进入瞄准：把目标朝向设到角色身后，让相机平缓 Slerp 转过去（不吸附），到位后自动拉近
    public void BeginAim(float behindYaw)
    {
        yaw = behindYaw;
        desiredRot = Quaternion.Euler(pitch, yaw, 0f);   // 只设目标，currentRot 平滑转过去
        aimMode = true;
        aimEntering = true;
        aimEnterTimer = 0f;
    }

    /// 退出瞄准
    public void EndAim()
    {
        aimMode = false;
        aimEntering = false;
    }
}