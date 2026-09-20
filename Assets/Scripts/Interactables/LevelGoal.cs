using UnityEngine;

/// <summary>
/// PROP Level goal (final target). Inherits InteractableBase and uses the existing interaction system:
///   - Robot approaches and aims at it -> InteractionPrompt automatically shows the raw interactVerb text (default Press "F" to Complete)
///   - Press F -> OnInteract -> GameResultManager.Win()
///
/// Just attach it to the final target in the scene. It needs a Collider so the Interactor's range / aim check can hit it
/// (Reset adds a Box Trigger automatically if there is none).
/// By default only the robot can interact (access = RobotOnly).
/// </summary>
[DisallowMultipleComponent]
public class LevelGoal : InteractableBase
{
    [Header("Goal")]
    [Tooltip("Auto-disable after triggering once, to avoid showing the results screen repeatedly")]
    public bool oneShot = true;
    [Tooltip("If checked, no need to press F: the robot wins as soon as it enters this object's Trigger (requires Collider.isTrigger)")]
    public bool winOnTouch = false;

    [Header("Debug (read-only at runtime)")]
    [SerializeField] private bool triggeredReadout;

    private bool triggered;

    /// Bring up GameResultManager / LevelStats at level start, so the timer and death hooks work from the first frame.
    /// Otherwise they'd only be created when F is pressed, and the results would all be 0.
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
        Trigger($"{interactor.name} pressed interact");
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!winOnTouch) return;
        var id = other.GetComponentInParent<CharacterId>();
        if (id == null) return;
        var who = id.characterType == CharacterType.Robot ? InteractorType.Robot : InteractorType.Player;
        if (!CanBeUsedBy(who)) return;
        Trigger($"{id.name} entered the goal area");
    }

    private void Trigger(string reason)
    {
        if (triggered && oneShot) return;
        triggered = true;
        triggeredReadout = true;
        if (oneShot) interactable = false;   // Prompt disappears immediately and nobody else can trigger it

        Debug.Log($"[Goal] Victory: {reason}", this);
        GameResultManager.GetOrCreate().Win(this);
    }
}
