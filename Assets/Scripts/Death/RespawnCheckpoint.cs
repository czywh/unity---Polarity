using UnityEngine;

/// <summary>
/// Respawn checkpoint. Place a Trigger collider over a "specific area";
/// when a character enters, its own respawn point is updated to respawnPoint's position.
/// The player entering updates the player's, the robot entering updates the robot's; they don't interfere.
/// </summary>
[RequireComponent(typeof(Collider))]
public class RespawnCheckpoint : MonoBehaviour
{
    [Tooltip("Manually set respawn position. Leave empty to use this object's Transform.")]
    public Transform respawnPoint;

    [Tooltip("Only affect a specific character? When checked, only characters of targetType can refresh this checkpoint.")]
    public bool restrictToType = false;
    public CharacterType targetType = CharacterType.Player;

    void Reset()
    {
        // Convenience: automatically set to Trigger when dragged into the scene
        GetComponent<Collider>().isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        // The collider may be on a child object, so search upward for CharacterId
        CharacterId id = other.GetComponentInParent<CharacterId>();
        if (id == null) return;

        if (restrictToType && id.characterType != targetType) return;

        if (RespawnManager.Instance == null)
        {
            Debug.LogWarning("[RespawnCheckpoint] No RespawnManager in the scene.");
            return;
        }

        Transform point = respawnPoint != null ? respawnPoint : transform;
        RespawnManager.Instance.SetRespawnPoint(id.characterType, point.position, point.rotation);
    }
}