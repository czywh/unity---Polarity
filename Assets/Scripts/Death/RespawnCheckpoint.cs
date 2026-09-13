using UnityEngine;

/// <summary>
/// 复活检查点。放一个 Trigger 碰撞体在“特定区域”，
/// 角色一进入，就把它自己的复活点更新为 respawnPoint 的位置。
/// player 进来更新 player 的，robot 进来更新 robot 的，互不干扰。
/// </summary>
[RequireComponent(typeof(Collider))]
public class RespawnCheckpoint : MonoBehaviour
{
    [Tooltip("手动设置的复活位置。留空则使用本物体的 Transform。")]
    public Transform respawnPoint;

    [Tooltip("只对指定角色生效？勾选后只有 targetType 的角色能刷新此检查点。")]
    public bool restrictToType = false;
    public CharacterType targetType = CharacterType.Player;

    void Reset()
    {
        // 方便：拖到场景里自动设成 Trigger
        GetComponent<Collider>().isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        // 碰撞体可能挂在子物体上，往上找 CharacterId
        CharacterId id = other.GetComponentInParent<CharacterId>();
        if (id == null) return;

        if (restrictToType && id.characterType != targetType) return;

        if (RespawnManager.Instance == null)
        {
            Debug.LogWarning("[RespawnCheckpoint] 场景里没有 RespawnManager。");
            return;
        }

        Transform point = respawnPoint != null ? respawnPoint : transform;
        RespawnManager.Instance.SetRespawnPoint(id.characterType, point.position, point.rotation);
    }
}