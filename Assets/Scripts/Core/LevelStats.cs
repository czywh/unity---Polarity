using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CORE Level stats: time / player deaths / robot deaths / robot energy used. The results screen reads from here.
///
/// Startup: LevelGoal.Awake() calls GetOrCreate() for this component at level start (via GameResultManager),
/// so timing starts on the first frame. You can also place one in the scene manually to tweak parameters.
///   - Time: accumulated from when this component is enabled (affected by timeScale); frozen after Stop()
///   - Deaths: subscribes to every CharacterDeathHandler.onDeath in the scene, counting player / robot separately by CharacterId
///   - Energy: reads the robot's EnergySystem.TotalDrained (only increases; charging doesn't offset it)
/// </summary>
[DisallowMultipleComponent]
public class LevelStats : MonoBehaviour
{
    public static LevelStats Instance { get; private set; }

    [Header("Stats Scope")]
    [Tooltip("Count energy used by the robot only (CharacterId = Robot); unchecked sums all characters in the scene with a CharacterId")]
    public bool energyRobotOnly = true;

    [Header("Debug (read-only at runtime)")]
    [SerializeField] private float elapsedReadout;
    [SerializeField] private int playerDeathsReadout;
    [SerializeField] private int robotDeathsReadout;
    [SerializeField] private float energyReadout;
    [SerializeField] private bool runningReadout;
    [SerializeField] private int hookedHandlersReadout;
    [SerializeField] private int energySystemsReadout;

    // -- Public read-only --
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
            if (energySystems.Count == 0) CollectEnergy();   // Safety net: read before collection had a chance to run
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

    /// <summary>Auto-create one if none exists in the scene</summary>
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
        // Collect in Awake: even if this component was AddComponent'ed in someone else's Awake, scene objects already exist
        HookDeaths();
        CollectEnergy();
    }

    private void Start()
    {
        // Once more, to cover the order issue where "this component Awakes before some characters" (HookDeaths dedupes internally)
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

    /// <summary>Freeze all counters on level clear / end</summary>
    public void Stop()
    {
        if (!running) return;
        frozenEnergy = EnergyUsed;   // Read first (still running while reading), then freeze
        running = false;
    }

    public void ResetStats()
    {
        elapsed = 0f; playerDeaths = 0; robotDeaths = 0; frozenEnergy = 0f; running = true;
    }

    /// <summary>Formatted time: mm:ss.ff</summary>
    public string FormatTime()
    {
        int m = (int)(elapsed / 60f);
        float s = elapsed - m * 60f;
        return $"{m:00}:{s:00.00}";
    }

    // -- Deaths --
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

    // -- Energy used --
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
