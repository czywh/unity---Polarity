using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 角色死亡 / 复活流程编排。挂在 player 和 robot 各自的根物体上。
/// 流程：Die() → 禁用本角色控制/冻结物理 → 播放溶解 → 等 respawnDelay 秒
///       → 传送到该角色自己的复活点 → 重新显形 → 交回 CharacterSwitcher 裁决控制权。
///
/// 关键：复活时【不自己开控制】，而是调用 characterSwitcher.RefreshControlState()，
/// 由切换器按“当前角色”决定谁能动。这样死亡期间切到另一个角色，复活后也不会误开本角色。
/// </summary>
[RequireComponent(typeof(CharacterId))]
public class CharacterDeathHandler : MonoBehaviour
{
    [Header("引用")]
    public DissolvingController dissolvingController;
    [Tooltip("角色切换器。填了之后，控制权由切换器统一裁决（推荐）")]
    public CharacterSwitcher characterSwitcher;

    [Header("复活流程")]
    [Tooltip("溶解结束后到复活的等待时间（需求里的 1 秒）")]
    public float respawnDelay = 1f;
    [Tooltip("复活后是否播放“重新显形”动画（1→0）")]
    public bool appearOnRespawn = true;

    [Header("死亡时要禁用的控制脚本（未接 CharacterSwitcher 时的后备）")]
    public MonoBehaviour[] componentsToDisable;

    [Header("事件回调")]
    public UnityEvent onDeath;
    public UnityEvent onRespawn;

    // 供 CharacterSwitcher 判断：死亡中的角色不应被启用控制
    public bool IsDying => isDying;

    private CharacterId id;
    private Rigidbody rb;
    private CharacterController cc;
    private bool originalKinematic;
    private bool isDying;

    void Awake()
    {
        id = GetComponent<CharacterId>();
        rb = GetComponent<Rigidbody>();
        cc = GetComponent<CharacterController>();
        if (rb != null) originalKinematic = rb.isKinematic;

        // 没手动拖就自动找场景里的切换器，避免漏拖导致复活走错分支重新开控制
        if (characterSwitcher == null)
            characterSwitcher = FindObjectOfType<CharacterSwitcher>();

        // 反向注册回切换器，两个死亡处理器的槽也不用手动拖
        if (characterSwitcher != null)
            characterSwitcher.RegisterDeathHandler(id.characterType == CharacterType.Player, this);
    }

    void Start()
    {
        // 游戏开始时若还没有复活点，用初始出生位置兜底
        if (RespawnManager.Instance != null &&
            !RespawnManager.Instance.TryGetRespawnPoint(id.characterType, out _))
        {
            RespawnManager.Instance.SetRespawnPoint(id.characterType, transform.position, transform.rotation);
        }
    }

    /// <summary>触发死亡。DeathZone 会调用它，也可手动调用。</summary>
    public void Die()
    {
        if (isDying) return;
        isDying = true;          // 先置死亡态，SuppressControl 才会禁用本角色
        StartCoroutine(DeathSequence());
    }

    IEnumerator DeathSequence()
    {
        onDeath?.Invoke();
        SuppressControl();
        FreezePhysics(true);

        // 1) 播放溶解并等待完成
        bool dissolveDone = (dissolvingController == null);
        if (dissolvingController != null)
            dissolvingController.Dissolve(() => dissolveDone = true);
        while (!dissolveDone) yield return null;

        // 2) 等待
        yield return new WaitForSeconds(respawnDelay);

        // 3) 复活
        Respawn();
    }

    void Respawn()
    {
        Vector3 pos = transform.position;
        Quaternion rot = transform.rotation;

        if (RespawnManager.Instance != null &&
            RespawnManager.Instance.TryGetRespawnPoint(id.characterType, out var data))
        {
            pos = data.position;
            rot = data.rotation;
        }
        else
        {
            Debug.LogWarning($"[CharacterDeathHandler] {id.characterType} 没有可用复活点，原地复活。");
        }

        TeleportTo(pos, rot);

        if (appearOnRespawn && dissolvingController != null)
            dissolvingController.Appear(FinishRespawn);
        else
        {
            dissolvingController?.SetDissolveAmount(0f);
            FinishRespawn();
        }
    }

    void FinishRespawn()
    {
        FreezePhysics(false);
        isDying = false;          // 先解除死亡态，恢复控制才会生效
        RestoreControl();
        onRespawn?.Invoke();
    }

    // —— 控制权：一律交给切换器裁决；没有切换器时才用后备列表 ——
    void SuppressControl()
    {
        if (characterSwitcher != null) characterSwitcher.RefreshControlState(); // 死亡态下禁用本角色
        else SetControlEnabled(false);
    }

    void RestoreControl()
    {
        if (characterSwitcher != null) characterSwitcher.RefreshControlState(); // 按当前角色重新裁决
        else SetControlEnabled(true);
    }

    void SetControlEnabled(bool on)
    {
        if (componentsToDisable != null)
            foreach (var c in componentsToDisable)
                if (c != null) c.enabled = on;
    }

    // —— 物理 ——
    void FreezePhysics(bool freeze)
    {
        if (rb == null) return;
        if (freeze) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        rb.isKinematic = freeze ? true : originalKinematic;
    }

    void TeleportTo(Vector3 pos, Quaternion rot)
    {
        // CharacterController 会锁死 transform，必须先关掉再传送
        if (cc != null) cc.enabled = false;
        if (rb != null) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; } // Unity6 改名 linearVelocity
        transform.SetPositionAndRotation(pos, rot);
        Physics.SyncTransforms();
        if (cc != null) cc.enabled = true;
    }
}