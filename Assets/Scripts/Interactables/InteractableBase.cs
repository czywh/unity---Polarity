using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Interactable base class: implements IInteractable, provides access control + focus state + global registry.
/// Charging docks, switches, levers, etc. just inherit from this class.
///
/// Key state IsFocused: true while focused by an "authorized interactor" --
/// continuous interactions like charging docks only need to check IsFocused in Update to work.
///
/// Interaction prompt: InteractVerb (action word) + UsesKeyPress (whether key-press based) let InteractionPrompt build the text.
/// </summary>
[DisallowMultipleComponent]
public abstract class InteractableBase : MonoBehaviour, IInteractable
{
    [Header("Interaction Access")]
    [Tooltip("Who can interact: player only / robot only / both")]
    public InteractAccess access = InteractAccess.Both;
    [Tooltip("Whether interaction is open; off = nobody can use it (e.g. not activated / broken)")]
    public bool interactable = true;

    [Header("Interaction Prompt")]
    [Tooltip("Action word in the prompt, e.g. Charge / Open / Pull")]
    [SerializeField] protected string interactVerb = "Interact";

    [Header("Interaction Prompt Position")]
    [Tooltip("Explicit anchor: drag in an empty child and the prompt sticks to it (most flexible). Empty = position on the bounding box using the ratio below")]
    [SerializeField] private Transform promptAnchor;
    [Range(0f, 1f)]
    [Tooltip("Without an explicit anchor, the prompt's position along the bounding box height: 0=bottom, 0.5=center, 1=top; lower-middle is about 0.3")]
    [SerializeField] private float promptVertical = 1f;
    [Tooltip("Extra world offset added to the final position (for fine-tuning)")]
    [SerializeField] private Vector3 promptWorldOffset = Vector3.zero;

    [Header("Debug (runtime read-only, handy for checking whether interaction triggers)")]
    [Tooltip("Whether currently focused by an authorized interactor = interactable is true")]
    [SerializeField] private bool isFocusedReadout;

    // -- Global registry for Interactor to iterate (same approach as ElectricField) --
    private static readonly List<InteractableBase> all = new List<InteractableBase>();
    public static IReadOnlyList<InteractableBase> All => all;

	// public game object Icoin 

    public InteractAccess Access => access;
    public bool IsInteractable => interactable && isActiveAndEnabled;
    public Transform InteractTransform => transform;

    // -- Interaction prompt public API --
    /// Action word in the prompt (Charge / Open ...)
    public string InteractVerb => interactVerb;
    /// Whether key-press interaction: true → prompt shows "Press [key] to <verb>"; false → shows only the verb (continuous/automatic)
    public virtual bool UsesKeyPress => true;

    // -- Interaction prompt position --
    /// Explicit anchor (nullable); when set, the prompt sticks to it
    public Transform PromptAnchor => promptAnchor;
    /// Positioning ratio along bounding box height: 0=bottom, 0.5=center, 1=top
    public float PromptVertical => promptVertical;
    /// Extra world offset
    public Vector3 PromptWorldOffset => promptWorldOffset;

    // -- Focus state --
    public bool IsFocused { get; private set; }
    public Interactor CurrentInteractor { get; private set; }

    protected virtual void OnEnable()
    {
        if (!all.Contains(this)) all.Add(this);
    }

    protected virtual void OnDisable()
    {
        all.Remove(this);
        if (IsFocused) ClearFocus();
    }

    // Mirror IsFocused into a visible field for watching in the Inspector whether interaction triggers
    protected virtual void LateUpdate()
    {
        isFocusedReadout = IsFocused;
    }

    public bool CanBeUsedBy(InteractorType who)
    {
        if (!IsInteractable) return false;
        switch (access)
        {
            case InteractAccess.Both: return true;
            case InteractAccess.PlayerOnly: return who == InteractorType.Player;
            case InteractAccess.RobotOnly: return who == InteractorType.Robot;
            default: return false;
        }
    }

    public virtual void OnFocusEnter(Interactor interactor)
    {
		
        IsFocused = true;
		// icon.setActive() = True;
        CurrentInteractor = interactor;
    }

    public virtual void OnFocusExit(Interactor interactor)
    {
		// icon.setActive() = false 
        ClearFocus();
    }

    public virtual void OnInteract(Interactor interactor) { }

    private void ClearFocus()
    {
        IsFocused = false;
        CurrentInteractor = null;
    }
}