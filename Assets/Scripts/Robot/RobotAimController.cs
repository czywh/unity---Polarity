using UnityEngine;

/// <summary>
/// ROBOT-05（瞄准）　机器人第三人称射击瞄准控制器。
///
/// 右键切换「瞄准 / 移动」两种模式：
///  · 瞄准模式：相机贴到机器人身后、屏幕中央十字准星、机器人朝向随鼠标（=相机朝向）、
///    左键朝准星方向发射导弹。
///  · 移动模式：恢复原来的环绕相机 + 转向移动方向。
/// 只有瞄准模式能发射。切走机器人时自动退出瞄准。
///
/// 建议加进 CharacterSwitcher 的 robotControlScripts，使其只在控制机器人时生效。
/// 前提：相机用 OrbitFollowCamera，且其 holdRightMouseToRotate 取消勾选（右键留给瞄准切换）。
/// </summary>
[RequireComponent(typeof(RobotController))]
public class RobotAimController : MonoBehaviour
{
    [Header("引用（留空自动获取）")]
    public OrbitFollowCamera cam;
    [Tooltip("发射口 / 瞄准方向起点；留空用自身")]
    public Transform muzzle;

    [Header("瞄准射线")]
    [Tooltip("准星命中检测层；建议排除玩家 / 机器人")]
    public LayerMask aimMask = ~0;
    public float maxAimDistance = 1000f;

    [Header("十字准星（可选，留空用内置简易准星）")]
    public GameObject crosshair;

    private IMissileLauncher launcher;
    private Camera cameraComp;
    private Transform muzzleT;
    private RobotController robot;   // 用其 enabled 判断"当前是否在控制机器人"

    public bool IsAiming { get; private set; }

    private void Awake()
    {
        robot = GetComponent<RobotController>();
        launcher = GetComponent<IMissileLauncher>();
        if (cam == null && Camera.main != null)
            cam = Camera.main.GetComponent<OrbitFollowCamera>();
        cameraComp = cam != null ? cam.GetComponent<Camera>() : Camera.main;
        muzzleT = muzzle != null ? muzzle : transform;
        if (crosshair != null) crosshair.SetActive(false);
    }

    private void Update()
    {
        // 只有"控制机器人"时才允许瞄准 / 发射；切到玩家（RobotController 被禁用）则强制退出、忽略输入
        if (robot == null || !robot.enabled)
        {
            if (IsAiming) SetAiming(false);
            return;
        }

        // 右键切换瞄准 / 移动
        if (Input.GetMouseButtonDown(1)) SetAiming(!IsAiming);

        if (!IsAiming) return;

        // 左键发射（仅瞄准状态）
        if (Input.GetMouseButtonDown(0)) Fire();
    }

    private void LateUpdate()
    {
        // 相机转到位后，角色朝向才跟随相机（避免进入瞄准的转向过程中角色被带着摆动）
        if (IsAiming && cam != null && cam.AimReady)
        {
            Vector3 f = cam.AimForward;
            if (f.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(f, Vector3.up);
        }
    }

    private void SetAiming(bool on)
    {
        IsAiming = on;

        if (cam != null)
        {
            if (on) cam.BeginAim(transform.eulerAngles.y);   // 平缓转到身后，不吸附
            else cam.EndAim();
        }
        if (crosshair != null) crosshair.SetActive(on);

        // 机器人游戏中始终锁定隐藏光标（移动 / 瞄准都只用相机，不需要可见光标）
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Fire()
    {
        if (launcher == null) return;
        launcher.TryFire(ComputeAimDirection());
    }

    // 从屏幕中央射线求瞄准点，再算发射方向
    private Vector3 ComputeAimDirection()
    {
        if (cameraComp == null) return muzzleT.forward;

        Ray ray = cameraComp.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 aimPoint = Physics.Raycast(ray, out RaycastHit hit, maxAimDistance, aimMask, QueryTriggerInteraction.Ignore)
            ? hit.point
            : ray.GetPoint(maxAimDistance);

        Vector3 dir = aimPoint - muzzleT.position;
        return dir.sqrMagnitude > 0.0001f ? dir.normalized : muzzleT.forward;
    }

    private void OnDisable()
    {
        // 切走机器人 → 退出瞄准状态并复位相机，但不动光标：
        // 光标交给 CharacterSwitcher 统一设置（玩家=锁定隐藏），避免两处抢
        if (IsAiming)
        {
            IsAiming = false;
            if (cam != null) cam.EndAim();
            if (crosshair != null) crosshair.SetActive(false);
        }
    }

    // 内置简易十字准星（未指定 crosshair 时用）
    private void OnGUI()
    {
        if (!IsAiming || crosshair != null) return;

        float cx = Screen.width * 0.5f;
        float cy = Screen.height * 0.5f;
        const float len = 10f, thick = 2f;

        Color old = GUI.color;
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(cx - len, cy - thick * 0.5f, len * 2f, thick), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - thick * 0.5f, cy - len, thick, len * 2f), Texture2D.whiteTexture);
        GUI.color = old;
    }
}