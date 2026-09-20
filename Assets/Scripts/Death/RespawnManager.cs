using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Respawn point manager (singleton).
/// Stores a separate respawn point (position + rotation) per character type;
/// player and robot don't affect each other, each recording the last checkpoint it passed.
/// </summary>
public class RespawnManager : MonoBehaviour
{
    public static RespawnManager Instance { get; private set; }

    public struct RespawnData
    {
        public Vector3 position;
        public Quaternion rotation;
        public bool valid;
    }

    // One respawn record per character; a later checkpoint overwrites the old one, giving "live updates"
    private readonly Dictionary<CharacterType, RespawnData> respawnPoints
        = new Dictionary<CharacterType, RespawnData>();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>Update a character's respawn point.</summary>
    public void SetRespawnPoint(CharacterType type, Vector3 position, Quaternion rotation)
    {
        respawnPoints[type] = new RespawnData
        {
            position = position,
            rotation = rotation,
            valid = true
        };
    }

    /// <summary>Try to get a character's respawn point.</summary>
    public bool TryGetRespawnPoint(CharacterType type, out RespawnData data)
    {
        if (respawnPoints.TryGetValue(type, out data) && data.valid)
            return true;

        data = default;
        return false;
    }
}