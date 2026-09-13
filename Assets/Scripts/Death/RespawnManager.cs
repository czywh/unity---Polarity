using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 复活点管理器（单例）。
/// 为每个角色类型单独保存一份复活点（位置 + 朝向），
/// player 和 robot 互不影响，各自记录自己上一次经过的检查点。
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

    // 每个角色一条复活记录；后到的检查点会覆盖旧的，实现“实时更新”
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

    /// <summary>更新某个角色的复活点。</summary>
    public void SetRespawnPoint(CharacterType type, Vector3 position, Quaternion rotation)
    {
        respawnPoints[type] = new RespawnData
        {
            position = position,
            rotation = rotation,
            valid = true
        };
    }

    /// <summary>尝试取出某个角色的复活点。</summary>
    public bool TryGetRespawnPoint(CharacterType type, out RespawnData data)
    {
        if (respawnPoints.TryGetValue(type, out data) && data.valid)
            return true;

        data = default;
        return false;
    }
}