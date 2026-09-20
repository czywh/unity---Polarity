using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// CORE  Level-clear flow orchestration (singleton). After LevelGoal triggers Win():
///   1) LevelStats.Stop() freezes time / deaths / energy used;
///   2) CharacterSwitcher.SetExternallyFrozen(true) takes away all control; if in Operation Mode, Exit() first;
///   3) Unlock the cursor; timeScale = 0 so enemies / platforms / lasers all stop;
///   4) GameResultUI.Show() pops up the results panel.
/// The panel's two buttons call back here: Restart() reloads this level, NextLevel() goes to the next level (or main menu if none).
///
/// Zero config: LevelGoal calls GetOrCreate(). To change the next level's name / main menu name, place one in the scene manually and edit it in the Inspector.
/// </summary>
[DisallowMultipleComponent]
public class GameResultManager : MonoBehaviour
{
    public static GameResultManager Instance { get; private set; }

    [Header("Level Flow")]
    [Tooltip("Scene name of the next level. Empty = take the next one in Build Settings order; if none, go back to the main menu")]
    public string nextSceneName = "";
    [Tooltip("Main menu scene name (the NEXT button goes here when there's no next level)")]
    public string mainMenuSceneName = "Start";

    [Header("On Victory")]
    [Tooltip("Set Time.timeScale to 0 after victory (enemies, platforms, lasers all stop). Automatically restored to 1 before switching scenes")]
    public bool pauseTimeOnWin = true;
    [Tooltip("Unlock and show the mouse after victory (needed to click the result buttons)")]
    public bool showCursorOnWin = true;

    [Header("References (auto-found / auto-created if left empty)")]
    [SerializeField] private GameResultUI resultUI;
    [SerializeField] private CharacterSwitcher switcher;
    [SerializeField] private OperationModeController operationMode;

    [Header("Debug (runtime read-only)")]
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

        LevelStats.GetOrCreate();   // Make sure stats are running from the very start of the level
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Victory entry point. Called by LevelGoal; can also be called manually (e.g. for debugging)</summary>
    public void Win(LevelGoal goal = null)
    {
        if (HasWon) return;
        HasWon = true;
        wonReadout = true;
        WinningGoal = goal;

        var stats = LevelStats.GetOrCreate();
        stats.Stop();

        // If still in Operation Mode (in theory the robot can't reach the goal interaction in Operation Mode, but just in case)
        if (operationMode != null && operationMode.InOperationMode) operationMode.Exit();

        // Take away all character control (don't disable the components themselves, just let the switcher rule "all frozen")
        if (switcher != null) switcher.SetExternallyFrozen(true);

        if (showCursorOnWin)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (resultUI != null)
            resultUI.Show(stats, HasNextScene(out _) ? "NEXT LEVEL" : "MAIN MENU");
        else
            Debug.LogWarning("[Result] No GameResultUI; won but there's no panel", this);

        if (pauseTimeOnWin) Time.timeScale = 0f;
        Debug.Log($"[Result] Victory  time={stats.FormatTime()}  deaths={stats.Deaths}  energyUsed={stats.EnergyUsed:F1}", this);
    }

    // -- Button callbacks --

    /// <summary>Restart this level</summary>
    public void Restart()
    {
        Time.timeScale = 1f;
        var active = SceneManager.GetActiveScene();
        SceneManager.LoadScene(active.buildIndex);
    }

    /// <summary>Go to the next level; if there's none, go back to the main menu</summary>
    public void NextLevel()
    {
        Time.timeScale = 1f;
        if (HasNextScene(out string next))
            SceneManager.LoadScene(next);
        else if (!string.IsNullOrEmpty(mainMenuSceneName) && Application.CanStreamedLevelBeLoaded(mainMenuSceneName))
            SceneManager.LoadScene(mainMenuSceneName);
        else
        {
            Debug.LogWarning("[Result] No next level and no main menu scene found; reloading this level instead", this);
            Restart();
        }
    }

    /// Whether there's a next level: explicit name > next Build Settings index (skipping the main menu itself)
    private bool HasNextScene(out string sceneName)
    {
        sceneName = null;
        if (!string.IsNullOrEmpty(nextSceneName))
        {
            if (Application.CanStreamedLevelBeLoaded(nextSceneName)) { sceneName = nextSceneName; return true; }
            Debug.LogWarning($"[Result] nextSceneName \"{nextSceneName}\" is not in Build Settings", this);
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
