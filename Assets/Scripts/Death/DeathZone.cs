using UnityEngine;

/// <summary>
/// 死亡区域。玩家/机器人/敌人跌落进这个 Trigger 就触发死亡。
/// 一般做成关卡底部一大片看不见的 Box Collider。
///
/// 为什么同时处理 Enter 和 Stay：
/// CharacterController 高速下坠时，OnTriggerEnter 有概率被 PhysX 漏掉
/// （尤其本场景的 deadZone 只有 4 个单位厚）。OnTriggerStay 每个物理帧都会
/// 对"已重叠"的碰撞体回调一次，作为兜底；Die() 内部有 isDying 保护，重复调用无副作用。
/// </summary>
[RequireComponent(typeof(Collider))]
public class DeathZone : MonoBehaviour
{
    [Header("调试")]
    [Tooltip("打开后，每次判定死亡都会在 Console 打印是谁掉进来了")]
    public bool verboseLog = false;

    void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    void OnTriggerEnter(Collider other) => Kill(other, "Enter");

    void OnTriggerStay(Collider other) => Kill(other, "Stay");

    private void Kill(Collider other, string phase)
    {
        // 玩家 / 机器人：走角色死亡系统
        CharacterDeathHandler handler = other.GetComponentInParent<CharacterDeathHandler>();
        if (handler != null)
        {
            if (handler.IsDying) return;              // 已在死亡流程里，别重复触发
            if (verboseLog) Debug.Log($"[死亡区域] {phase}：{handler.name} 进入 {name}，触发死亡", this);
            handler.Die();
            return;
        }

        // 敌人：走敌人死亡
        EnemyDeath enemy = other.GetComponentInParent<EnemyDeath>();
        if (enemy != null)
        {
            if (verboseLog) Debug.Log($"[死亡区域] {phase}：敌人 {enemy.name} 进入 {name}，触发死亡", this);
            enemy.Kill();
        }
    }
}