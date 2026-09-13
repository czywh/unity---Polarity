using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// UI-03 电量条池管理器：统一管理电子实体 + 敌人的电量条（同一套 EntityEnergyBar prefab 池化）。
///
/// 显示规则（按来源类型）：
///   · 电子实体(ElectricEntity)：瞄准注视 / 准星命中 / 在领域内 → 显示（原逻辑）。
///   · 敌人(EnemyBarSource)：操作模式下常驻显示（不需靠近）。
/// </summary>
[DisallowMultipleComponent]
public class EntityBarManager : MonoBehaviour
{
    [Header("血条池")]
    [SerializeField] private EntityEnergyBar barPrefab;
    [SerializeField] private int maxBars = 12;

    [Header("瞄准来源（留空自动查找）")]
    [SerializeField] private PlayerAimController playerAim;
    [SerializeField] private RobotAimController robotAim;
    [SerializeField] private OperationModeController operationMode;

    [Header("瞄准模式下的注视检测")]
    [SerializeField] private Camera cam;
    [SerializeField] private float viewDistance = 30f;
    [SerializeField] private float viewAngle = 8f;
    [SerializeField] private LayerMask occluders;

    [Header("其它")]
    [SerializeField] private float lingerTime = 0.4f;
    [SerializeField] private float rescanInterval = 1f;
    [Tooltip("敌人条显示距离（米）：相机在此范围内的敌人一直显示条")]
    [SerializeField] private float enemyShowDistance = 10f;

    private readonly List<EntityEnergyBar> bars = new List<EntityEnergyBar>();
    private readonly List<IBarSource> sources = new List<IBarSource>();
    private readonly Dictionary<ElectricEntity, ElectricEntityBarSource> entityWrappers = new Dictionary<ElectricEntity, ElectricEntityBarSource>();
    private readonly Dictionary<IBarSource, float> lastActive = new Dictionary<IBarSource, float>();
    private readonly HashSet<IBarSource> wantSet = new HashSet<IBarSource>();
    private float nextRescan;

    private void Start()
    {
        if (cam == null) cam = Camera.main;
        if (playerAim == null) playerAim = FindFirstObjectByType<PlayerAimController>();
        if (robotAim == null) robotAim = FindFirstObjectByType<RobotAimController>();
        if (operationMode == null) operationMode = FindFirstObjectByType<OperationModeController>();

        for (int i = 0; i < maxBars && barPrefab != null; i++)
        {
            var b = Instantiate(barPrefab, transform);
            b.gameObject.SetActive(true);
            bars.Add(b);
        }
        Rescan();
    }

    private void Update()
    {
        if (Time.time >= nextRescan) Rescan();
        if (cam == null) cam = Camera.main;

        bool playerAiming = playerAim != null && playerAim.isActiveAndEnabled && playerAim.IsAiming;
        bool robotAiming  = robotAim  != null && robotAim.isActiveAndEnabled  && robotAim.IsAiming;
        bool anyAiming    = playerAiming || robotAiming;
        bool inOpMode     = operationMode != null && operationMode.InOperationMode;

        float now = Time.time;
        wantSet.Clear();

        for (int i = 0; i < sources.Count; i++)
        {
            var s = sources[i];
            if (s == null || !s.BarAlive) continue;

            bool want;
            if (s is ElectricEntityBarSource es)
                want = WantEntity(es, anyAiming, playerAiming);
            else if (s is EnemyBarSource enemy)
                want = inOpMode || WantEnemy(enemy);   // 操作模式常驻；否则 10m 内一直显示
            else
                want = false;

            if (want) lastActive[s] = now;
            if (lastActive.TryGetValue(s, out float la) && now - la <= lingerTime)
                wantSet.Add(s);
        }

        foreach (var s in wantSet)
        {
            var bar = FindBarByTarget(s);
            if (bar == null) { bar = FindFreeBar(); if (bar != null) bar.Assign(s); }
            else bar.KeepShown();
        }

        for (int i = 0; i < bars.Count; i++)
        {
            var bar = bars[i];
            if (bar.Target != null && !wantSet.Contains(bar.Target)) bar.Hide();
        }
    }

    // 电子实体显示规则：领域内 或 瞄准注视 / 准星命中
    private bool WantEntity(ElectricEntityBarSource es, bool anyAiming, bool playerAiming)
    {
        if (es.Entity.IsCovered()) return true;   // 在领域内

        if (anyAiming)
        {
            if (playerAiming && playerAim.AimedEntity == es.Entity) return true;  // 玩家准星命中
            if (cam != null)
            {
                Vector3 to = es.BarWorldPosition - cam.transform.position;
                float dist = to.magnitude;
                if (dist <= viewDistance && Vector3.Angle(cam.transform.forward, to) <= viewAngle)
                    return !IsOccluded(cam.transform.position, to, dist);
            }
        }
        return false;
    }

    // 敌人显示规则：相机 enemyShowDistance 米内一直显示（待命也显示）
    private bool WantEnemy(EnemyBarSource enemy)
    {
        if (cam == null) return false;
        return Vector3.Distance(cam.transform.position, enemy.BarWorldPosition) <= enemyShowDistance;
    }

    private bool IsOccluded(Vector3 from, Vector3 dir, float dist)
    {
        if (occluders.value == 0) return false;
        return Physics.Raycast(from, dir.normalized, dist - 0.1f, occluders, QueryTriggerInteraction.Ignore);
    }

    private EntityEnergyBar FindBarByTarget(IBarSource s)
    {
        for (int i = 0; i < bars.Count; i++)
            if (bars[i].Target == s) return bars[i];
        return null;
    }

    private EntityEnergyBar FindFreeBar()
    {
        for (int i = 0; i < bars.Count; i++)
            if (bars[i].IsFree) return bars[i];
        return null;
    }

    // 重扫：合并 ElectricEntity（包装）+ EnemyBarSource
    private void Rescan()
    {
        sources.Clear();

        var entities = FindObjectsByType<ElectricEntity>(FindObjectsSortMode.None);
        foreach (var e in entities)
        {
            if (e == null) continue;
            if (!entityWrappers.TryGetValue(e, out var w))
            {
                w = new ElectricEntityBarSource(e);
                entityWrappers[e] = w;
            }
            sources.Add(w);
        }

        var enemies = FindObjectsByType<EnemyBarSource>(FindObjectsSortMode.None);
        foreach (var en in enemies)
            if (en != null) sources.Add(en);

        // 清理已销毁实体的包装缓存
        if (entityWrappers.Count > entities.Length * 2 + 8)
        {
            var dead = new List<ElectricEntity>();
            foreach (var kv in entityWrappers) if (kv.Key == null) dead.Add(kv.Key);
            foreach (var k in dead) entityWrappers.Remove(k);
        }

        nextRescan = Time.time + rescanInterval;
    }

    public void RefreshSources() => Rescan();
}