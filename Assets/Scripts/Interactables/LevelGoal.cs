using UnityEngine;

/// <summary>
/// PROP　关卡终点（final target）。继承 InteractableBase，走现有交互系统：
///   · 机器人靠近并对准 → InteractionPrompt 自动显示 interactVerb 的原文（默认 Press "F" to Complete）
///   · 按 F → OnInteract → GameResultManager.Win()
///
/// 挂到场景里的 final target 上即可。需要一个 Collider 让 Interactor 的范围 / 对准判定命中
/// （Reset 时若没有会自动加一个 Box Trigger）。
/// 默认只允许机器人交互（access = RobotOnly）。
/// </summary>
[DisallowMultipleComponent]
public class LevelGoal : InteractableBase
{
    [Header("终点")]
    [Tooltip("触发一次后自动关闭，避免重复弹结算")]
    public bool oneShot = true;
    [Tooltip("勾上则不用按 F：机器人一进入本物体的 Trigger 就直接胜利（需要 Collider.isTrigger）")]
    public bool winOnTouch = false;

    [Header("调试（运行时只读）")]
    [SerializeField] private bool triggeredReadout;

    private bool triggered;

    /// 关卡一开始就把 GameResultManager / LevelStats 拉起来，计时和死亡钩子从第一帧生效。
    /// 否则它们要到按 F 那一刻才诞生，结算全是 0。
    private void Awake()
    {
        GameResultManager.GetOrCreate();
    }

    private void Reset()
    {
        access = InteractAccess.RobotOnly;
        interactVerb = "Press \"F\" to Complete";
        if (GetComponent<Collider>() == null)
        {
            var box = gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(3f, 2f, 3f);
            box.center = new Vector3(0f, 1f, 0f);
        }
    }

    public override void OnInteract(Interactor interactor)
    {
        base.OnInteract(interactor);
        if (!CanBeUsedBy(interactor.type)) return;
        Trigger($"{interactor.name} 按下交互键");
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!winOnTouch) return;
        var id = other.GetComponentInParent<CharacterId>();
        if (id == null) return;
        var who = id.characterType == CharacterType.Robot ? InteractorType.Robot : InteractorType.Player;
        if (!CanBeUsedBy(who)) return;
        Trigger($"{id.name} 进入终点区域");
    }

    private void Trigger(string reason)
    {
        if (triggered && oneShot) return;
        triggered = true;
        triggeredReadout = true;
        if (oneShot) interactable = false;   // 提示立刻消失，别人也不能再触发

        Debug.Log($"[终点] 胜利：{reason}", this);
        GameResultManager.GetOrCreate().Win(this);
    }
}
