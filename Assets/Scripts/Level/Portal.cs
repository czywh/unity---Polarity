using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// LEVEL　双向传送门。挂在两面 Energy wall 上，互相把对方拖进 linkedPortal，
/// 角色（玩家 / 机器人 / 敌人，任何带 CharacterController 或 Rigidbody 的物体）穿过 A 就出现在 B，反之亦然。
///
/// 工作方式：
///   · 本物体需要一个【Trigger】碰撞体（脚本会在运行时自动把第一个 Collider 设成 isTrigger）；
///   · 进入触发器 → 传送到对方门的出口点（exitPoint，留空则用对方门位置 + 对方 forward × exitOffset）；
///   · 出口方式两种（exitMode）：KeepDirection = 朝向不变、从 B 的另一侧出来继续往前走（默认，适合两面墙朝向相同）；
///     MirrorThroughPortal = 从 A 正面进就从 B 正面出，朝向随门的相对角度旋转（《传送门》式）；
///   · 传送后对目标加一个短暂冷却，防止落在对方门里立刻被传回来（乒乓）；
///   · CharacterController 传送前要先关掉再开（与 CharacterDeathHandler.TeleportTo 同一做法）；
///   · 主角传送后相机瞬移过去（OrbitFollowCamera.SetTarget(target, instant:true)），不然会飞一段。
///
/// 挂法：两面墙各挂一个 Portal，把对方拖进 Linked Portal；也可以只在其中一个上填，另一个会自动回填。
/// </summary>
[DisallowMultipleComponent]
public class Portal : MonoBehaviour
{
    [Header("连接")]
    [Tooltip("对面的门。只填一边也行，Awake 时会自动把另一边连回来")]
    public Portal linkedPortal;

    [Header("出口")]
    [Tooltip("出口点（可选）：拖一个空子物体摆到希望角色出现的位置和朝向。留空则用本门位置 + forward × exitOffset")]
    public Transform exitPoint;
    [Tooltip("没有 exitPoint 时，从门中心沿 forward 推出去多远（避免出生时还在触发器里）")]
    public float exitOffset = 1.5f;
    public enum ExitMode
    {
        [InspectorName("保持方向（穿过 A 后从 B 另一侧出来，继续往前走）")] KeepDirection,
        [InspectorName("镜像（从 A 正面进 → 从 B 正面出，朝向随门旋转）")] MirrorThroughPortal,
    }
    [Tooltip("KeepDirection：角色世界朝向不变，从 B 的\"前进方向那一侧\"出来，适合两面墙朝向相同的关卡；\nMirrorThroughPortal：像《传送门》那样，朝向随两扇门的相对角度旋转")]
    public ExitMode exitMode = ExitMode.KeepDirection;

    [Header("过滤")]
    [Tooltip("哪些层的物体可以传送；默认全部")]
    public LayerMask affectedLayers = ~0;
    [Tooltip("只传送根物体（碰撞体在子物体上也能找到根上的 CharacterController / Rigidbody）")]
    public bool useRootObject = true;

    [Header("防乒乓")]
    [Tooltip("传送后多久内不再被任何门传送（秒）")]
    public float cooldown = 0.5f;

    [Header("相机")]
    [Tooltip("传送的是主相机跟随目标时，让相机瞬移。留空自动取 Main Camera 上的 OrbitFollowCamera")]
    public OrbitFollowCamera followCamera;

    [Header("调试")]
    public bool verboseLog = false;

    // 全局冷却表：物体 → 可再次传送的时间。两个门共用，才能防乒乓
    private static readonly Dictionary<Transform, float> cooldownUntil = new Dictionary<Transform, float>();

    private void Awake()
    {
        // 触发器保证
        var col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
        {
            col.isTrigger = true;
            Log("已把碰撞体设为 Trigger");
        }
        else if (col == null)
        {
            Debug.LogWarning($"[Portal] {name} 上没有 Collider，传送门不会触发。加一个 BoxCollider 并勾 Is Trigger", this);
        }

        // 自动回填对方
        if (linkedPortal != null && linkedPortal.linkedPortal == null)
            linkedPortal.linkedPortal = this;

        if (followCamera == null && Camera.main != null)
            followCamera = Camera.main.GetComponent<OrbitFollowCamera>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (linkedPortal == null) { Log("linkedPortal 为空，忽略"); return; }
        if ((affectedLayers.value & (1 << other.gameObject.layer)) == 0) return;

        Transform t = useRootObject ? other.transform.root : other.transform;

        // 冷却中（刚从对面传过来）
        if (cooldownUntil.TryGetValue(t, out float until) && Time.time < until) return;

        // 死亡流程中不传送，避免和复活传送打架
        var death = t.GetComponent<CharacterDeathHandler>();
        if (death != null && death.IsDying) return;

        // 角色是从门的哪一侧进来的（forward 那一侧算正面），以及它的前进方向（指向门心）
        Vector3 toDoor = transform.position - t.position; toDoor.y = 0f;
        bool fromFront = Vector3.Dot(-toDoor, transform.forward) >= 0f;
        linkedPortal.Receive(t, this, fromFront, toDoor.normalized);
    }

    /// <summary>把物体放到本门的出口。由对面的门调用。fromFront：是否从入口门的正面进入；travelDir：进门时的水平前进方向</summary>
    public void Receive(Transform t, Portal from, bool fromFront, Vector3 travelDir)
    {
        Vector3 pos;
        Quaternion rot = t.rotation;

        if (exitPoint != null)
        {
            // 指定了出口点：位置用它；镜像模式下朝向也按它算
            pos = exitPoint.position;
            if (exitMode == ExitMode.MirrorThroughPortal)
                rot = FlattenYaw(exitPoint.rotation * Quaternion.Inverse(from.transform.rotation * Quaternion.Euler(0f, 180f, 0f)) * t.rotation);
        }
        else if (exitMode == ExitMode.KeepDirection)
        {
            // 世界朝向不变；出口选在 B 的"前进方向那一侧"，角色继续往前走就自然离开门
            float side = Vector3.Dot(travelDir, transform.forward) >= 0f ? 1f : -1f;
            pos = transform.position + transform.forward * (exitOffset * side);
        }
        else
        {
            // 镜像：从 A 正面进 → 从 B 正面出；从 A 背面进 → 从 B 背面出。
            // 朝向映射：转 180°（进门方向 = 出门方向的反面），于是朝 -A.forward 走进去 → 朝 +B.forward 走出来
            float side = fromFront ? 1f : -1f;
            pos = transform.position + transform.forward * (exitOffset * side);
            Quaternion delta = transform.rotation * Quaternion.Euler(0f, 180f, 0f) * Quaternion.Inverse(from.transform.rotation);
            rot = FlattenYaw(delta * t.rotation);
        }

        // 高度：保持角色相对"门"的高度差不变，两扇门放在不同高度的地面上也不会卡进地里
        pos.y = (exitPoint != null ? exitPoint.position.y : transform.position.y)
                + (t.position.y - from.transform.position.y);

        var cc = t.GetComponent<CharacterController>();

        // —— 冷却，先登记再传送（防止落地瞬间被对方门捕获）——
        cooldownUntil[t] = Time.time + cooldown;

        // —— 传送（CharacterController 要先关再开）——
        var rb = t.GetComponent<Rigidbody>();
        if (cc != null) cc.enabled = false;
        if (rb != null)
        {
            // 速度方向也跟着门旋转
            Quaternion vDelta = rot * Quaternion.Inverse(t.rotation);
            rb.velocity = vDelta * rb.velocity;
            rb.angularVelocity = Vector3.zero;
        }
        t.SetPositionAndRotation(pos, rot);
        Physics.SyncTransforms();
        if (cc != null) cc.enabled = true;

        // —— 相机瞬移 ——
        if (followCamera != null && followCamera.target != null && followCamera.target.IsChildOf(t))
            followCamera.SetTarget(followCamera.target, instant: true);

        Log($"{t.name}: {from.name} → {name} @ {pos}");
    }

    // 只保留绕 Y 轴的旋转（角色不应该被门歪着放）
    private static Quaternion FlattenYaw(Quaternion q)
    {
        Vector3 fwd = q * Vector3.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) return Quaternion.identity;
        return Quaternion.LookRotation(fwd.normalized, Vector3.up);
    }

    private void Log(string msg)
    {
        if (verboseLog) Debug.Log($"[Portal] {msg}", this);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.9f);
        Vector3 exit = exitPoint != null ? exitPoint.position : transform.position + transform.forward * exitOffset;
        Gizmos.DrawWireSphere(exit, 0.3f);
        Gizmos.DrawLine(transform.position, exit);
        if (linkedPortal != null)
        {
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.6f);
            Gizmos.DrawLine(transform.position, linkedPortal.transform.position);
        }
    }
}
