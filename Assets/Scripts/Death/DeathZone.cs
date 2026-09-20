using UnityEngine;

/// <summary>
/// Death zone. Player/robot/enemy falling into this Trigger triggers death.
/// Usually a large invisible Box Collider at the bottom of the level.
///
/// Why handle both Enter and Stay:
/// When a CharacterController falls fast, PhysX may occasionally miss OnTriggerEnter
/// (especially since this scene's deadZone is only 4 units thick). OnTriggerStay fires every physics frame
/// for "already overlapping" colliders as a fallback; Die() is guarded by isDying, so repeated calls have no side effects.
/// </summary>
[RequireComponent(typeof(Collider))]
public class DeathZone : MonoBehaviour
{
    [Header("Debug")]
    [Tooltip("When on, logs to the Console who fell in every time death is triggered")]
    public bool verboseLog = false;

    void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    void OnTriggerEnter(Collider other) => Kill(other, "Enter");

    void OnTriggerStay(Collider other) => Kill(other, "Stay");

    private void Kill(Collider other, string phase)
    {
        // Player / robot: go through the character death system
        CharacterDeathHandler handler = other.GetComponentInParent<CharacterDeathHandler>();
        if (handler != null)
        {
            if (handler.IsDying) return;              // Already in the death flow, don't trigger again
            if (verboseLog) Debug.Log($"[DeathZone] {phase}: {handler.name} entered {name}, triggering death", this);
            handler.Die();
            return;
        }

        // Enemy: go through enemy death
        EnemyDeath enemy = other.GetComponentInParent<EnemyDeath>();
        if (enemy != null)
        {
            if (verboseLog) Debug.Log($"[DeathZone] {phase}: enemy {enemy.name} entered {name}, triggering death", this);
            enemy.Kill();
        }
    }
}