using UnityEngine;

/// <summary>
/// COMMON　平台骑乘。挂在 player / robot 的根物体上（与 CharacterController 同物体）。
///
/// 解决的问题：CharacterController 不受移动中碰撞体的带动。操作模式下点击让
/// ConsoleOperable 平台平移 / 绕枢轴旋转时，站在上面的角色相当于被"抽走了地板"，
/// 会原地停住、被平台推挤，甚至直接落进死亡区域。
///
/// 做法：每帧向脚下探测当前踩着的物体，把角色当前位置换算到该物体【上一帧】的
/// 坐标系里，再用【这一帧】的变换还原成世界坐标，两者之差就是平台这一帧的刚体运动量，
/// 通过 controller.Move() 补给角色。
///   · 只补平台运动，不会抵消角色自己的移动（局部坐标每帧现算，不缓存旧位置）；
///   · 平移与绕枢轴旋转都能跟随；
///   · 站在静止地面上算出来的差值恒为 0，对普通地形零副作用，因此可以常驻启用。
///
/// 执行顺序 200：确保在 ConsoleOperable（平台自身移动）与各角色控制器
/// （RobotController / RobotConsoleMover / PassiveFall，均为默认 0）之后运行，
/// 补偿量基于本帧最终状态计算，不会出现延迟一帧造成的抖动或脚下打滑。
/// </summary>
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]   // 挂两份会把平台位移补偿两次
[RequireComponent(typeof(CharacterController))]
public class PlatformRider : MonoBehaviour
{
    [Header("脚下探测")]
    [Tooltip("向下探测距离。略大于 CharacterController 的 Skin Width 即可；台阶多的关卡可适当调大")]
    public float groundProbeDistance = 0.35f;
    [Tooltip("哪些层可以作为“可骑乘的地板”。默认全选；静止地面算出的位移恒为 0，不必特意排除")]
    public LayerMask groundMask = ~0;
    [Tooltip("接触面法线 y 分量的下限。低于此值视为墙面而非地板，不跟随")]
    [Range(0f, 1f)] public float minGroundNormalY = 0.5f;

    [Header("跟随内容")]
    [Tooltip("跟随平台的平移，以及绕平台枢轴旋转带来的位移")]
    public bool inheritPosition = true;
    [Tooltip("跟随平台的 Y 轴自转（角色自身也跟着转向）。\n操作模式下机器人朝向由鼠标接管，开了会互相打架，默认关闭")]
    public bool inheritYaw = false;

    [Header("调试")]
    [Tooltip("打开后，每次切换脚下平台都会在 Console 打印")]
    public bool verboseLog = false;
    [Header("调试（运行时只读）")]
    [SerializeField] private string standingOnReadout = "(无)";

    private CharacterController controller;
    private CharacterDeathHandler death;      // 可空

    private Transform platform;               // 当前踩着的物体
    private Vector3 lastPlatformPos;          // 上一帧该物体的世界位置
    private Quaternion lastPlatformRot;       // 上一帧该物体的世界旋转
    private readonly RaycastHit[] hits = new RaycastHit[8];

    /// <summary>当前踩着的物体（悬空时为 null）。供动画 / 音效等外部逻辑读取。</summary>
    public Transform StandingOn => platform;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        death = GetComponent<CharacterDeathHandler>();
    }

    private void OnDisable() => Release();

    private void Update()
    {
        // 死亡 / 复活传送期间位置由 CharacterDeathHandler 独占，这里不插手
        if ((death != null && death.IsDying) || !controller.enabled)
        {
            Release();
            return;
        }

        Transform found = ProbeGround();

        // 刚踩上新平台（或落地）：只记录基准，这一帧没有可用的位移量
        if (found != platform)
        {
            Attach(found);
            return;
        }
        if (platform == null) return;

        if (inheritPosition)
        {
            // 角色当前位置 →（平台上一帧坐标系）→（平台这一帧坐标系）→ 世界坐标
            Matrix4x4 prev = Matrix4x4.TRS(lastPlatformPos, lastPlatformRot, Vector3.one);
            Matrix4x4 now = Matrix4x4.TRS(platform.position, platform.rotation, Vector3.one);

            Vector3 local = prev.inverse.MultiplyPoint3x4(transform.position);
            Vector3 delta = now.MultiplyPoint3x4(local) - transform.position;

            if (delta.sqrMagnitude > 1e-10f) controller.Move(delta);
        }

        if (inheritYaw)
        {
            float yaw = Mathf.DeltaAngle(lastPlatformRot.eulerAngles.y, platform.rotation.eulerAngles.y);
            if (Mathf.Abs(yaw) > 0.0001f) transform.Rotate(0f, yaw, 0f, Space.World);
        }

        CacheBasis();
    }

    /// 从胶囊下半球中心向下做球形扫描，找脚下最近的、法线够平的物体
    private Transform ProbeGround()
    {
        float sideScale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));
        float upScale = Mathf.Abs(transform.lossyScale.y);

        float radius = Mathf.Max(0.01f, controller.radius * sideScale * 0.95f);   // 略缩，避免起点就与地面重叠
        float halfHeight = Mathf.Max(controller.height * upScale * 0.5f, radius);

        Vector3 center = transform.TransformPoint(controller.center);
        Vector3 origin = center - Vector3.up * (halfHeight - radius);

        int n = Physics.SphereCastNonAlloc(origin, radius, Vector3.down, hits,
            groundProbeDistance, groundMask, QueryTriggerInteraction.Ignore);

        Transform best = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < n; i++)
        {
            Collider col = hits[i].collider;
            if (col == null) continue;
            if (col.transform == transform || col.transform.IsChildOf(transform)) continue;  // 别踩到自己
            if (hits[i].distance <= 0f) continue;              // 起点重叠，法线无效
            if (hits[i].normal.y < minGroundNormalY) continue; // 墙面 / 陡坡，不算地板
            if (hits[i].distance < bestDistance)
            {
                bestDistance = hits[i].distance;
                best = col.transform;
            }
        }
        return best;
    }

    private void Attach(Transform t)
    {
        if (verboseLog && t != platform)
            Debug.Log($"[平台骑乘] {name} 脚下：{(platform ? platform.name : "(无)")} → {(t ? t.name : "(无)")}", this);

        platform = t;
        standingOnReadout = t != null ? t.name : "(无)";
        if (t != null) CacheBasis();
    }

    private void CacheBasis()
    {
        lastPlatformPos = platform.position;
        lastPlatformRot = platform.rotation;
    }

    private void Release()
    {
        if (platform == null) return;
        platform = null;
        standingOnReadout = "(无)";
    }

    private void OnDrawGizmosSelected()
    {
        CharacterController cc = controller != null ? controller : GetComponent<CharacterController>();
        if (cc == null) return;

        float sideScale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));
        float upScale = Mathf.Abs(transform.lossyScale.y);
        float radius = Mathf.Max(0.01f, cc.radius * sideScale * 0.95f);
        float halfHeight = Mathf.Max(cc.height * upScale * 0.5f, radius);

        Vector3 center = transform.TransformPoint(cc.center);
        Vector3 origin = center - Vector3.up * (halfHeight - radius);

        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.6f);
        Gizmos.DrawWireSphere(origin, radius);
        Gizmos.DrawWireSphere(origin + Vector3.down * groundProbeDistance, radius);
        Gizmos.DrawLine(origin + Vector3.right * radius,
                        origin + Vector3.right * radius + Vector3.down * groundProbeDistance);
        Gizmos.DrawLine(origin - Vector3.right * radius,
                        origin - Vector3.right * radius + Vector3.down * groundProbeDistance);
    }
}
