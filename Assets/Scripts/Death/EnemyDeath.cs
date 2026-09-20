using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Enemy death: single entry point Kill(). Called by any kill method (suicide on reaching target, falling into a death zone, later missiles/attacks, etc.).
/// Death effect can optionally dissolve (reuses DissolvingController); by default destroys / deactivates after a delay.
/// CharacterType is only Player/Robot; enemies are a third party, so they don't use the player respawn system.
/// </summary>
public class EnemyDeath : MonoBehaviour
{
    [Header("Death Presentation (optional)")]
    [Tooltip("Dissolve effect; empty = destroy/deactivate directly")]
    public DissolvingController dissolvingController;
    [Tooltip("Components to disable on death (e.g. EnemyChaser, colliders), so it doesn't move/hurt while dying")]
    public MonoBehaviour[] componentsToDisable;

    [Header("Post-Death Handling")]
    [Tooltip("Checked: Destroy the whole object after the death effect; unchecked: only SetActive(false)")]
    public bool destroyOnDeath = true;
    [Tooltip("Without dissolve, how long before destroying/deactivating")]
    public float fallbackDelay = 0.3f;

    [Header("Events")]
    public UnityEvent onDeath;

    public bool IsDead { get; private set; }

    /// Single kill entry point: can be called by death zones / missiles / attacks, etc. (idempotent)
    public void Kill()
    {
        if (IsDead) return;
        IsDead = true;

        // Disable behavior components so it no longer moves / hurts while dying
        if (componentsToDisable != null)
            foreach (var c in componentsToDisable) if (c != null) c.enabled = false;

        onDeath?.Invoke();

        if (dissolvingController != null)
            dissolvingController.Dissolve(Finish);   // clean up in the dissolve-finished callback
        else
            Invoke(nameof(Finish), fallbackDelay);
    }

    private void Finish()
    {
        if (destroyOnDeath) Destroy(gameObject);
        else gameObject.SetActive(false);
    }
}