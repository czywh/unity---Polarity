using UnityEngine;

/// <summary>
/// PLAYER（瞄准）　玩家第三人称瞄准控制器，逻辑与 RobotAimController 一致：
///  · 右键切换「瞄准 / 移动」；瞄准时相机平缓绕到身后再拉近、屏幕中央十字准星、
///    角色朝向随相机（准星方向）。
///  · 只在控制玩家时生效，切走玩家自动退出瞄准。
///
/// 与机器人不同：玩家不发射导弹，而是持续记录准星对准的【第一个物体】(AimedTarget)，
/// 供后续逻辑处理（当前只做检测 + 调试显示，不对物体做任何操作）。
///
/// 建议加进 CharacterSwitcher 的 playerControlScripts。
/// 前提：相机用 OrbitFollowCamera，且 holdRightMouseToRotate 取消勾选。
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class PlayerAimController : MonoBehaviour
{
    [Header("引用（留空自动获取）")]
    public OrbitFollowCamera cam;

    [Header("瞄准射线")]
    [Tooltip("准星命中检测层；之后可缩到只含需要处理的物体")]
    public LayerMask aimMask = ~0;
    public float maxAimDistance = 1000f;

    [Header("放电（对准电子实体按住左键 = 放电）")]
    [Tooltip("每秒放电量（设计单位/秒，与物体电量同单位）")]
    public float dischargeRate = 50f;

    [Header("十字准星（可选，留空用内置简易准星）")]
    public GameObject crosshair;

    [Header("调试（运行时只读）")]
    [SerializeField] private bool aiming;
    [Tooltip("当前是否正在对准物体放电（按住左键且对准可放电实体）")]
    [SerializeField] private bool discharging;
    [Tooltip("当前准星对准的第一个物体名字")]
    [SerializeField] private string aimedTargetName = "(无)";

    private PlayerController player;
    private Camera cameraComp;

    public bool IsAiming { get; private set; }

    // —— 供后续处理：当前准星对准的第一个物体 ——
    public Transform AimedTarget { get; private set; }
    public bool HasAimHit { get; private set; }
    public RaycastHit LastHit { get; private set; }
    // 当前对准的电子实体（用于放电），没有则 null
    public ElectricEntity AimedEntity { get; private set; }
    // 当前对准的敌人电量（用于吸电），没有则 null
    public EnergySystem AimedEnemyEnergy { get; private set; }
    // 放电锁定目标：按住左键期间即使射线脱离也继续吸的对象
    private ElectricEntity dischargeTarget;
    private EnergySystem dischargeEnemyEnergy;

    private void Awake()
    {
        player = GetComponent<PlayerController>();
        if (cam == null && Camera.main != null)
            cam = Camera.main.GetComponent<OrbitFollowCamera>();
        cameraComp = cam != null ? cam.GetComponent<Camera>() : Camera.main;
        if (crosshair != null) crosshair.SetActive(false);
    }

    private void Update()
    {
        // 只有"控制玩家"时才允许瞄准；切走（PlayerController 被禁用）则强制退出、忽略输入
        if (player == null || !player.enabled)
        {
            if (IsAiming) SetAiming(false);
            return;
        }

        // 右键切换瞄准 / 移动
        if (Input.GetMouseButtonDown(1)) SetAiming(!IsAiming);

        if (!IsAiming) { ClearTarget(); return; }

        // 每帧刷新准星对准的第一个物体
        UpdateAimTarget();

        // 放电：按住左键期间锁定一个可放电目标，持续吸到 Min Energy 或松手为止。
        // 锁定不限于按下那一帧——握着把准星扫到目标上也能锁；锁定后即使目标跌破阈值、
        // 碰撞关闭（射线打不到了）也继续吸，直到吸满或松手。
        if (Input.GetMouseButton(0))
        {
            if (dischargeTarget == null && AimedEntity != null && AimedEntity.CanDischarge)
                dischargeTarget = AimedEntity;

            // 敌人电量：锁定后持续吸（同样握住扫到就锁）
            if (dischargeEnemyEnergy == null && AimedEnemyEnergy != null)
                dischargeEnemyEnergy = AimedEnemyEnergy;

            if (dischargeTarget != null)
                dischargeTarget.Discharge(dischargeRate * Time.deltaTime);
            if (dischargeEnemyEnergy != null)
                dischargeEnemyEnergy.Drain(dischargeRate * Time.deltaTime);

            discharging = dischargeTarget != null || dischargeEnemyEnergy != null;
        }
        else
        {
            dischargeTarget = null;
            dischargeEnemyEnergy = null;
            discharging = false;
        }
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
        aiming = on;

        if (cam != null)
        {
            if (on) cam.BeginAim(transform.eulerAngles.y);   // 平缓转到身后，不吸附
            else cam.EndAim();
        }
        if (crosshair != null) crosshair.SetActive(on);

        if (!on)
        {
            ClearTarget();
            dischargeTarget = null;   // 退出瞄准释放放电锁定
            dischargeEnemyEnergy = null;
        }
    }

    // 从屏幕中央射线求"对准的第一个物体"
    private void UpdateAimTarget()
    {
        if (cameraComp == null) { ClearTarget(); return; }

        Ray ray = cameraComp.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        // 命中 Trigger：这样跌破阈值变 Trigger（可穿过）的电子实体仍能被选中继续吸电
        if (Physics.Raycast(ray, out RaycastHit hit, maxAimDistance, aimMask, QueryTriggerInteraction.Collide))
        {
            HasAimHit = true;
            LastHit = hit;
            AimedTarget = hit.transform;
            AimedEntity = hit.transform.GetComponentInParent<ElectricEntity>();
            // 只把"敌人"(带 EnemyChaser)的 EnergySystem 当作可吸电目标，避免误吸机器人自己
            var enemy = hit.transform.GetComponentInParent<EnemyChaser>();
            AimedEnemyEnergy = enemy != null ? enemy.GetComponent<EnergySystem>() : null;
            aimedTargetName = hit.transform.name;
        }
        else
        {
            ClearTarget();
        }
    }

    private void ClearTarget()
    {
        HasAimHit = false;
        AimedTarget = null;
        AimedEntity = null;
        AimedEnemyEnergy = null;
        aimedTargetName = "(无)";
        discharging = false;
    }

    private void OnDisable()
    {
        // 切走玩家 → 退出瞄准并复位相机（光标由 CharacterSwitcher 统一管，这里不碰）
        if (IsAiming)
        {
            IsAiming = false;
            aiming = false;
            if (cam != null) cam.EndAim();
            if (crosshair != null) crosshair.SetActive(false);
            ClearTarget();
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