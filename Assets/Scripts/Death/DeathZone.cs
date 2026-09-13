using UnityEngine;

/// <summary>
/// 死亡区域。玩家/机器人/敌人跌落进这个 Trigger 就触发死亡。
/// 一般做成关卡底部一大片看不见的 Box Collider。
/// </summary>
[RequireComponent(typeof(Collider))]
public class DeathZone : MonoBehaviour
{
    void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        // 玩家 / 机器人：走角色死亡系统
        CharacterDeathHandler handler = other.GetComponentInParent<CharacterDeathHandler>();
        if (handler != null) { handler.Die(); return; }

        // 敌人：走敌人死亡
        EnemyDeath enemy = other.GetComponentInParent<EnemyDeath>();
        if (enemy != null) enemy.Kill();
    }
}