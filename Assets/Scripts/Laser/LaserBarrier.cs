using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 激光屏障玩法逻辑（与 Hovl_Laser 视觉分离）：激光沿 forward 常驻。
///   · 被 blockMask（bridge / 墙）挡住 → 光束在此截断，后面的目标安全。
///   · 命中【玩家 / 机器人】→ 沿"垂直于光束"方向击退一小步，无法穿越；不掉电。
///   · 命中【敌人】→ 不击退，可以直接穿过光束，但按每秒 enemyDrainPerSecond 持续掉电。
///
/// 两条规则的分工：对角色是"墙"，对敌人是"消耗"。
/// 敌人能走进光束，代价是电量流失；于是玩家可以把敌人引进激光里晾干，
/// 而不是被激光单纯挡住动弹不得。
///
/// 挂在激光塔上（与 Hovl_Laser 同物体、同朝向）。光束方向 = transform.forward。
/// </summary>
public class LaserBarrier : MonoBehaviour
{
    [Header("光束")]
    public float maxLength = 30f;
    [Tooltip("光束半径（判定目标是否接触的粗细）")]
    public float beamRadius = 0.35f;

    [Header("层")]
    [Tooltip("会挡住 / 截断光束的层（bridge、墙）——被它挡住后，后面的目标不受影响")]
    public LayerMask blockMask;
    [Tooltip("会被光束影响的目标层（Player / Robot / Enemy）")]
    public LayerMask knockbackMask;

    [Header("击退（仅玩家 / 机器人）")]
    [Tooltip("每次击退把角色推开的距离（一小步）")]
    public float knockbackDistance = 0.8f;
    [Tooltip("同一角色两次击退的最小间隔（秒），避免每帧狂推")]
    public float knockbackCooldown = 0.25f;
    [Tooltip("击退是否分帧平滑推（否则瞬移一步）")]
    public bool smooth = true;
    [Tooltip("平滑推速度（单位/秒）")]
    public float smoothSpeed = 12f;

    [Header("能量衰减（仅敌人）")]
    [Tooltip("敌人待在光束里时每秒扣除的电量。按实际接触时长连续结算，不是每次触碰扣一笔")]
    public float enemyDrainPerSecond = 10f;
    [Tooltip("勾选：被激光扣到 0 电时直接击杀敌人（走 EnemyDeath.Kill，有溶解表现）。\n取消：只是没电停摆，可被机器人的领域重新充电救活")]
    public bool killWhenDrained = false;

    [Header("减速（仅敌人）")]
    [Tooltip("敌人在光束里的移动速度倍率。0.333 = 降到原速的 1/3；1 = 不减速")]
    [Range(0.05f, 1f)]
    public float enemySlowMultiplier = 1f / 3f;
    [Tooltip("离开光束后减速还残留多久（秒）。给一点余量，避免敌人在光束边缘时速度反复闪烁")]
    public float slowLinger = 0.25f;

    [Header("能量衰减（电子物件：吸电桩等）")]
    [Tooltip("光束打在带 ElectricEntity 的物体上时，每秒扣除的电量。0 = 不影响物件。\n注意：物件的层必须在 blockMask 里，光束要能打到它身上并被截断")]
    public float entityDrainPerSecond = 30f;
    [Tooltip("勾选：只抽吸电桩（EnergyPylon）的电。\n取消：任何非 externallyDriven 的 ElectricEntity 都会被抽")]
    public bool onlyDrainPylons = true;

    [Header("调试")]
    public bool drawGizmo = true;
    [Tooltip("打印击退命中日志（掉电是每帧连续的，不打日志，看下面的只读字段）")]
    public bool verboseLog = false;
    [Tooltip("本帧正在被光束抽电的敌人数量")]
    [SerializeField] private int drainingCountReadout;
    [Tooltip("本帧正在被光束抽电的电子物件")]
    [SerializeField] private string entityDrainReadout = "(无)";

    private readonly Dictionary<int, float> cooldowns = new Dictionary<int, float>();
    private readonly Dictionary<Transform, Vector3> pendingPush = new Dictionary<Transform, Vector3>();
    private static readonly Collider[] buffer = new Collider[16];

    private void FixedUpdate()
    {
        Vector3 origin = transform.position;
        Vector3 dir = transform.forward;

        // ① 光束被 block 层截断的长度
        float length = maxLength;
        entityDrainReadout = "(无)";
        if (Physics.Raycast(origin, dir, out RaycastHit blockHit, maxLength, blockMask, QueryTriggerInteraction.Ignore))
        {
            length = blockHit.distance;
            // 命中的是转向器 → 激活它，从新角度再发一条
            var redirector = blockHit.collider.GetComponentInParent<LaserRedirector>();
            if (redirector != null) redirector.Hit(dir);

            // 命中的是电子物件（吸电桩等）→ 持续抽电
            var entity = blockHit.collider.GetComponentInParent<ElectricEntity>();
            if (entity != null) DrainEntity(entity);
            else entityDrainReadout = "(无)";
        }

        // ② 在 origin→截断点 的胶囊范围内找目标
        Vector3 end = origin + dir * length;
        int count = Physics.OverlapCapsuleNonAlloc(origin, end, beamRadius, buffer, knockbackMask, QueryTriggerInteraction.Ignore);

        int draining = 0;

        for (int i = 0; i < count; i++)
        {
            var col = buffer[i];
            if (col == null) continue;

            // 定位到"被射到的那个目标"本身：优先它自己的 CharacterController 所在物体，
            // 而不是 transform.root（多个敌人可能共用一个父物体，root 会指错对象）
            Transform target = ResolveTarget(col);
            if (target == null) continue;

            if (IsEnemy(target))
            {
                // —— 敌人：不击退，连续掉电 + 减速 ——
                if (DrainEnemy(target)) draining++;
                SlowEnemy(target);
                continue;
            }

            // —— 玩家 / 机器人：击退，不掉电 ——
            int id = target.GetInstanceID();
            if (cooldowns.TryGetValue(id, out float until) && Time.time < until) continue;
            cooldowns[id] = Time.time + knockbackCooldown;

            if (verboseLog) Debug.Log($"[LaserBarrier] 击退 {target.name}", target);

            // 击退方向：垂直于光束、从光束线指向该角色（推回它所在那侧）
            Vector3 toP = target.position - origin;
            Vector3 along = Vector3.Project(toP, dir);
            Vector3 perp = toP - along;
            perp.y = 0f;
            Vector3 pushDir = perp.sqrMagnitude > 0.0001f ? perp.normalized
                                                          : Vector3.Cross(dir, Vector3.up).normalized;

            ApplyPush(target, pushDir * knockbackDistance);
        }

        drainingCountReadout = draining;

        if (smooth && pendingPush.Count > 0) TickPending();
    }

    private static bool IsEnemy(Transform t)
    {
        return t.GetComponentInParent<EnemyChaser>() != null
            || t.GetComponentInParent<EnemyDeath>() != null;
    }

    /// 敌人持续掉电。返回是否确实扣到了电（供调试计数）
    private bool DrainEnemy(Transform enemy)
    {
        if (enemyDrainPerSecond <= 0f) return false;

        var es = enemy.GetComponentInParent<EnergySystem>();
        if (es == null) es = enemy.GetComponentInChildren<EnergySystem>();
        if (es == null) return false;

        if (!es.HasEnergy) return false;   // 已经空了，不用重复扣

        // FixedUpdate 是固定步长，乘 fixedDeltaTime 得到真实的"每秒 N 点"
        es.Drain(enemyDrainPerSecond * Time.fixedDeltaTime);

        // 被激光耗尽 → 可选直接击杀（走 EnemyDeath，有溶解表现）
        if (killWhenDrained && es.IsEmpty)
        {
            var death = enemy.GetComponentInParent<EnemyDeath>();
            if (death != null) death.Kill();
        }
        return true;
    }

    /// 电子物件（吸电桩等）持续掉电。走 DrainExternal，绕开 energyMode 限制 ——
    /// 吸电桩是 ChargeOnly（玩家抽不了它的电），但激光作为环境破坏可以
    private void DrainEntity(ElectricEntity entity)
    {
        if (entityDrainPerSecond <= 0f) { entityDrainReadout = "(无)"; return; }
        if (onlyDrainPylons && !(entity is EnergyPylon)) { entityDrainReadout = "(无)"; return; }

        entity.DrainExternal(entityDrainPerSecond * Time.fixedDeltaTime);
        entityDrainReadout = $"{entity.name}  {entity.CurrentEnergy:0}/{entity.MaxEnergy:0}";
    }

    /// 敌人减速。每个 FixedUpdate 续期一次，敌人离开光束 slowLinger 秒后自动恢复原速
    private void SlowEnemy(Transform enemy)
    {
        if (enemySlowMultiplier >= 1f) return;

        var chaser = enemy.GetComponentInParent<EnemyChaser>();
        if (chaser == null) return;

        chaser.ApplySlow(enemySlowMultiplier, slowLinger);
    }

    private void ApplyPush(Transform character, Vector3 push)
    {
        if (!smooth) { MoveCharacter(character, push); return; }
        pendingPush.TryGetValue(character, out Vector3 cur);
        pendingPush[character] = cur + push;
    }

    private void TickPending()
    {
        var keys = new List<Transform>(pendingPush.Keys);
        foreach (var t in keys)
        {
            if (t == null) { pendingPush.Remove(t); continue; }
            Vector3 remain = pendingPush[t];
            float step = smoothSpeed * Time.deltaTime;
            Vector3 move = Vector3.ClampMagnitude(remain, step);

            MoveCharacter(t, move);

            remain -= move;
            if (remain.sqrMagnitude < 0.0001f) pendingPush.Remove(t);
            else pendingPush[t] = remain;
        }
    }

    private static void MoveCharacter(Transform t, Vector3 delta)
    {
        var cc = t.GetComponent<CharacterController>();
        if (cc != null) cc.Move(delta);
        else t.position += delta;
    }

    // 从被命中的碰撞体定位到"该目标本身"，避免用共用父物体的 root
    private static Transform ResolveTarget(Collider col)
    {
        var cc = col.GetComponentInParent<CharacterController>();
        if (cc != null) return cc.transform;
        var chaser = col.GetComponentInParent<EnemyChaser>();
        if (chaser != null) return chaser.transform;
        return col.transform;   // 兜底：碰撞体自身
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmo) return;
        Vector3 origin = transform.position;
        Vector3 end = origin + transform.forward * maxLength;
        Gizmos.color = Color.red;
        Gizmos.DrawLine(origin, end);
        Gizmos.color = new Color(1f, 0.5f, 0f, 1f);
        Gizmos.DrawWireSphere(origin, beamRadius);
        Gizmos.DrawWireSphere(end, beamRadius);
    }
}