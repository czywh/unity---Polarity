using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// CORE　通关流程编排（单例）。LevelGoal 触发 Win() 后：
///   1) LevelStats.Stop() 冻结用时 / 死亡 / 耗电；
///   2) CharacterSwitcher.SetExternallyFrozen(true) 收走所有控制；若在操作模式中先 Exit()；
///   3) 光标解锁；timeScale = 0 让敌人 / 平台 / 激光全部停下；
///   4) GameResultUI.Show() 弹结算面板。
/// 面板上两个按钮回调到这里：Restart() 重载本关，NextLevel() 进下一关（没有则回主菜单）。
///
/// 零配置：LevelGoal 会 GetOrCreate()。想改下一关的名字 / 主菜单名字，手动放一个到场景里改 Inspector。
/// </summary>
[DisallowMultipleComponent]
public class GameResultManager : MonoBehaviour
{
    public static GameResultManager Instance { get; private set; }

    [Header("关卡流转")]
    [Tooltip("下一关的场景名。留空 = 按 Build Settings 顺序取下一个；再没有就回主菜单")]
    public string nextSceneName = "";
    [Tooltip("主菜单场景名（没有下一关时 NEXT 按钮跳这里）")]
    public string mainMenuSceneName = "Start";

    [Header("胜利时")]
    [Tooltip("胜利后把 Time.timeScale 置 0（敌人、平台、激光全部停下）。切场景前会自动恢复为 1")]
    public bool pauseTimeOnWin = true;
    [Tooltip("胜利后解锁并显示鼠标（点结算按钮需要）")]
    public bool showCursorOnWin = true;

    [Header("引用（留空自动查找 / 自动创建）")]
    [SerializeField] private GameResultUI resultUI;
    [SerializeField] private CharacterSwitcher switcher;
    [SerializeField] private OperationModeController operationMode;

    [Header("调试（运行时只读）")]
    [SerializeField] private bool wonReadout;

    public bool HasWon { get; private set; }
    public LevelGoal WinningGoal { get; private set; }

    public static GameResultManager GetOrCreate()
    {
        if (Instance != null) return Instance;
        var found = FindFirstObjectByType<GameResultManager>(FindObjectsInactive.Include);
        if (found != null) return found;
        return new GameObject("GameResultManager").AddComponent<GameResultManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (switcher == null) switcher = FindFirstObjectByType<CharacterSwitcher>(FindObjectsInactive.Include);
        if (operationMode == null) operationMode = FindFirstObjectByType<OperationModeController>(FindObjectsInactive.Include);
        if (resultUI == null) resultUI = FindFirstObjectByType<GameResultUI>(FindObjectsInactive.Include);
        if (resultUI == null) resultUI = GameResultUI.CreateDefault();

        LevelStats.GetOrCreate();   // 确保统计从关卡一开始就在跑
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>胜利入口。LevelGoal 调；也可以手动调（比如调试）</summary>
    public void Win(LevelGoal goal = null)
    {
        if (HasWon) return;
        HasWon = true;
        wonReadout = true;
        WinningGoal = goal;

        var stats = LevelStats.GetOrCreate();
        stats.Stop();

        // 若还在操作模式里（理论上机器人在操作模式下够不到终点交互，但保险）
        if (operationMode != null && operationMode.InOperationMode) operationMode.Exit();

        // 收走所有角色控制（不禁用组件本身，只让切换器裁决为"全冻结"）
        if (switcher != null) switcher.SetExternallyFrozen(true);

        if (showCursorOnWin)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (resultUI != null)
            resultUI.Show(stats, HasNextScene(out _) ? "NEXT LEVEL" : "MAIN MENU");
        else
            Debug.LogWarning("[结算] 没有 GameResultUI，胜利了但没有面板", this);

        if (pauseTimeOnWin) Time.timeScale = 0f;
        Debug.Log($"[结算] 胜利  用时={stats.FormatTime()}  死亡={stats.Deaths}  耗电={stats.EnergyUsed:F1}", this);
    }

    // ── 按钮回调 ──

    /// <summary>重新开始本关</summary>
    public void Restart()
    {
        Time.timeScale = 1f;
        var active = SceneManager.GetActiveScene();
        SceneManager.LoadScene(active.buildIndex);
    }

    /// <summary>进入下一关；没有下一关就回主菜单</summary>
    public void NextLevel()
    {
        Time.timeScale = 1f;
        if (HasNextScene(out string next))
            SceneManager.LoadScene(next);
        else if (!string.IsNullOrEmpty(mainMenuSceneName) && Application.CanStreamedLevelBeLoaded(mainMenuSceneName))
            SceneManager.LoadScene(mainMenuSceneName);
        else
        {
            Debug.LogWarning("[结算] 既没有下一关也找不到主菜单场景，改为重载本关", this);
            Restart();
        }
    }

    /// 有没有下一关：显式名字 > Build Settings 下一个索引（跳过主菜单本身）
    private bool HasNextScene(out string sceneName)
    {
        sceneName = null;
        if (!string.IsNullOrEmpty(nextSceneName))
        {
            if (Application.CanStreamedLevelBeLoaded(nextSceneName)) { sceneName = nextSceneName; return true; }
            Debug.LogWarning($"[结算] nextSceneName \"{nextSceneName}\" 不在 Build Settings 里", this);
            return false;
        }
        int idx = SceneManager.GetActiveScene().buildIndex + 1;
        while (idx < SceneManager.sceneCountInBuildSettings)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(idx);
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (name != mainMenuSceneName) { sceneName = name; return true; }
            idx++;
        }
        return false;
    }
}
