using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 敌人死亡：统一入口 Kill()。供任何杀敌方式调用（追到目标同归于尽、掉死亡区、以后导弹/攻击等）。
/// 死亡效果可选溶解（复用 DissolvingController），默认延迟后销毁 / 失活。
/// CharacterType 只有 Player/Robot，敌人是第三方，所以不走玩家的复活系统。
/// </summary>
public class EnemyDeath : MonoBehaviour
{
    [Header("死亡表现（可选）")]
    [Tooltip("溶解表现；留空则直接销毁/失活")]
    public DissolvingController dissolvingController;
    [Tooltip("死亡时要禁用的组件（如 EnemyChaser、碰撞等），避免死亡中还在动/伤人")]
    public MonoBehaviour[] componentsToDisable;

    [Header("死亡后处理")]
    [Tooltip("勾选：死亡效果结束后 Destroy 整个物体；不勾：只 SetActive(false)")]
    public bool destroyOnDeath = true;
    [Tooltip("无溶解时，多久后销毁/失活")]
    public float fallbackDelay = 0.3f;

    [Header("事件")]
    public UnityEvent onDeath;

    public bool IsDead { get; private set; }

    /// 统一杀敌入口：可被死亡区 / 导弹 / 攻击等任意方式调用（幂等）
    public void Kill()
    {
        if (IsDead) return;
        IsDead = true;

        // 禁用行为组件，死亡中不再移动 / 伤人
        if (componentsToDisable != null)
            foreach (var c in componentsToDisable) if (c != null) c.enabled = false;

        onDeath?.Invoke();

        if (dissolvingController != null)
            dissolvingController.Dissolve(Finish);   // 溶解结束回调再清理
        else
            Invoke(nameof(Finish), fallbackDelay);
    }

    private void Finish()
    {
        if (destroyOnDeath) Destroy(gameObject);
        else gameObject.SetActive(false);
    }
}