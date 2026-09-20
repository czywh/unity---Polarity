using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CORE　本关统计：用时 / 玩家死亡 / 机器人死亡 / 机器人耗电量。结算界面从这里取数。
///
/// 启动时机：LevelGoal.Awake() 会在关卡一开始就 GetOrCreate() 出本组件（经 GameResultManager），
/// 所以计时从第一帧开始。也可以手动放一个到场景里调参数。
///   · 用时：从本组件启用起累计（受 timeScale 影响）；Stop() 后冻结
///   · 死亡：订阅场景里所有 CharacterDeathHandler.onDeath，按 CharacterId 分开记玩家 / 机器人
///   · 耗电：读机器人 EnergySystem.TotalDrained（只加不减，充电不抵扣）
/// </summary>
[DisallowMultipleComponent]
public class LevelStats : MonoBehaviour
{
    public static LevelStats Instance { get; private set; }

    [Header("统计范围")]
    [Tooltip("耗电量只算机器人（CharacterId = Robot）；不勾则把场上所有带 CharacterId 的角色都加起来")]
    public bool energyRobotOnly = true;

    [Header("调试（运行时只读）")]
    [SerializeField] private float elapsedReadout;
    [SerializeField] private int playerDeathsReadout;
    [SerializeField] private int robotDeathsReadout;
    [SerializeField] private float energyReadout;
    [SerializeField] private bool runningReadout;
    [SerializeField] private int hookedHandlersReadout;
    [SerializeField] private int energySystemsReadout;

    // —— 对外只读 ——
    public float ElapsedSeconds => elapsed;
    public int PlayerDeaths => playerDeaths;
    public int RobotDeaths => robotDeaths;
    public int Deaths => playerDeaths + robotDeaths;
    public bool Running => running;
    public float EnergyUsed
    {
        get
        {
            if (!running) return frozenEnergy;
            if (energySystems.Count == 0) CollectEnergy();   // 兜底：还没来得及收集就被读
            float sum = 0f;
            foreach (var e in energySystems) if (e != null) sum += e.TotalDrained;
            return sum;
        }
    }

    private float elapsed;
    private int playerDeaths, robotDeaths;
    private float frozenEnergy;
    private bool running = true;
    private readonly List<EnergySystem> energySystems = new List<EnergySystem>();
    private readonly List<(CharacterDeathHandler h, UnityEngine.Events.UnityAction a)> deathHooks
        = new List<(CharacterDeathHandler, UnityEngine.Events.UnityAction)>();

    /// <summary>场景里没有时自动造一个</summary>
    public static LevelStats GetOrCreate()
    {
        if (Instance != null) return Instance;
        var found = FindFirstObjectByType<LevelStats>(FindObjectsInactive.Include);
        if (found != null) return found;
        return new GameObject("LevelStats").AddComponent<LevelStats>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        // Awake 里就收集：即使本组件是在别人 Awake 里被 AddComponent 出来的，场景对象也都已经存在
        HookDeaths();
        CollectEnergy();
    }

    private void Start()
    {
        // 再来一次，兜住"本组件比某些角色更早 Awake"的顺序问题（HookDeaths 内部会去重）
        HookDeaths();
        CollectEnergy();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        UnhookDeaths();
    }

    private void Update()
    {
        if (running) elapsed += Time.deltaTime;
        elapsedReadout = elapsed;
        playerDeathsReadout = playerDeaths;
        robotDeathsReadout = robotDeaths;
        energyReadout = EnergyUsed;
        runningReadout = running;
        hookedHandlersReadout = deathHooks.Count;
        energySystemsReadout = energySystems.Count;
    }

    /// <summary>通关 / 结束时冻结所有计数</summary>
    public void Stop()
    {
        if (!running) return;
        frozenEnergy = EnergyUsed;   // 先读（读的时候还是 running），再冻结
        running = false;
    }

    public void ResetStats()
    {
        elapsed = 0f; playerDeaths = 0; robotDeaths = 0; frozenEnergy = 0f; running = true;
    }

    /// <summary>格式化用时：mm:ss.ff</summary>
    public string FormatTime()
    {
        int m = (int)(elapsed / 60f);
        float s = elapsed - m * 60f;
        return $"{m:00}:{s:00.00}";
    }

    // ── 死亡 ──
    private void HookDeaths()
    {
        foreach (var h in FindObjectsByType<CharacterDeathHandler>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (h == null || h.onDeath == null) continue;
            bool already = false;
            foreach (var (hh, _) in deathHooks) if (hh == h) { already = true; break; }
            if (already) continue;

            var id = h.GetComponent<CharacterId>();
            bool isRobot = id != null && id.characterType == CharacterType.Robot;
            UnityEngine.Events.UnityAction a = isRobot ? (UnityEngine.Events.UnityAction)OnRobotDeath : OnPlayerDeath;
            h.onDeath.AddListener(a);
            deathHooks.Add((h, a));
        }
    }

    private void UnhookDeaths()
    {
        foreach (var (h, a) in deathHooks)
            if (h != null && h.onDeath != null) h.onDeath.RemoveListener(a);
        deathHooks.Clear();
    }

    private void OnPlayerDeath() { if (running) playerDeaths++; }
    private void OnRobotDeath()  { if (running) robotDeaths++; }

    // ── 耗电 ──
    private void CollectEnergy()
    {
        energySystems.Clear();
        foreach (var id in FindObjectsByType<CharacterId>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (energyRobotOnly && id.characterType != CharacterType.Robot) continue;
            var e = id.GetComponent<EnergySystem>();
            if (e != null) energySystems.Add(e);
        }
    }
}
