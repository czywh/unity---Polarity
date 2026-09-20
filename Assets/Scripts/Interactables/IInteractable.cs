using UnityEngine;

/// <summary>Interactor type: who is currently initiating the interaction</summary>
public enum InteractorType { Player, Robot }

/// <summary>Who can interact with this object</summary>
public enum InteractAccess { PlayerOnly, RobotOnly, Both }

/// <summary>
/// Interactable interface. Every interactable in the scene (charging docks, switches, levers...) implements it.
/// In practice, usually just inherit InteractableBase (implements this interface + focus management).
/// </summary>
public interface IInteractable
{
    /// Who can interact (Player / Robot / Both)
    InteractAccess Access { get; }

    /// Whether the object is currently open for interaction (can be temporarily disabled, e.g. inactive / broken)
    bool IsInteractable { get; }

    /// Reference point for distance / facing
    Transform InteractTransform { get; }

    /// Overall check: whether this type of interactor can use this object right now
    bool CanBeUsedBy(InteractorType who);

    /// Gained focus (in range + aimed + permission)
    void OnFocusEnter(Interactor interactor);

    /// Lost focus
    void OnFocusExit(Interactor interactor);

    /// Fired when the interact key is pressed (for press-style interactions; continuous ones like charging can just check IsFocused)
    void OnInteract(Interactor interactor);
}