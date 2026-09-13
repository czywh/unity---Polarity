using UnityEngine;

/// <summary>
/// ROBOT-05　导弹发射器：挂在机器人上，实现 IMissileLauncher。
/// 机器人视角下左键 → RobotController 调 TryFire()：向前发射一枚导弹。
///
/// 约束改为【电量】：每次发射一次性消耗 energyCost 点电，电量不足则发不出。
/// 与开领域共享同一块 EnergySystem，所有能力统一受电量约束。
///
/// 两个可选预制体：
///  · missilePrefab   —— 导弹飞行体（模型 / VFX / 拖尾）。可带 Missile 组件，也可纯视觉。
///  · fieldVfxPrefab  —— 导弹展开出的领域视觉 VFX。
/// 都留空则退回代码生成的小球 + 调试球。
/// </summary>
public class MissileLauncher : MonoBehaviour, IMissileLauncher
{
    [Header("发射")]
    [Tooltip("发射口；留空用本物体。前方 = 该 Transform 的 forward")]
    public Transform muzzle;
    public float missileSpeed = 20f;
    [Tooltip("飞多远后展开成领域")]
    public float travelDistance = 10f;
    [Tooltip("撞到这些层会提前展开；建议只勾环境层，排除玩家 / 机器人")]
    public LayerMask obstacleMask = ~0;
    [Tooltip("从发射口再往前多少米生成导弹，避免一出生就撞到机器人自己")]
    public float spawnForwardOffset = 1f;

    [Header("预制体")]
    [Tooltip("导弹飞行体预制体（模型 / VFX / 拖尾）。可带 Missile 组件，也可纯视觉；留空用代码小球")]
    public GameObject missilePrefab;

    [Header("电量消耗")]
    [Tooltip("每发射一枚导弹消耗的电量")]
    public float energyCost = 50f;

    [Header("展开出的领域")]
    public float fieldRadius = 4f;
    public float holdDuration = 8f;
    public float fadeDuration = 4f;
    [Tooltip("调试球颜色（配了领域 VFX 后忽略）")]
    public Color fieldColor = new Color(0.85f, 0.4f, 0.95f, 0.28f);

    [Header("领域视觉 VFX")]
    [Tooltip("领域用的 VFX 预制体（如 shield 粒子）。留空用调试球")]
    public GameObject fieldVfxPrefab;
    [Tooltip("领域 VFX 预制体的设计半径（Start Size=7 → 3.5），再对着线框微调")]
    public float fieldVfxDesignRadius = 2.2f;
    [Tooltip("把领域 VFX 自动缩放到 fieldRadius")]
    public bool autoScaleFieldVfx = true;

    private EnergySystem energy;

    private void Awake()
    {
        energy = GetComponent<EnergySystem>();
    }

    /// 电量是否够发一枚
    public bool CanFire => energy == null || energy.CurrentEnergy >= energyCost;

    // 朝正前方发射（供无瞄准时使用）
    public void TryFire()
    {
        Transform m = muzzle != null ? muzzle : transform;
        TryFire(m.forward);
    }

    // 朝指定方向发射（瞄准状态下用准星方向）
    public void TryFire(Vector3 direction)
    {
        if (!CanFire)
        {
            Debug.Log($"[Missile] 电量不足（需 {energyCost}），无法发射", this);
            return;
        }

        // 扣电（EnergySystem 会同步褪色）
        if (energy != null) energy.Drain(energyCost);

        Transform m = muzzle != null ? muzzle : transform;
        Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : m.forward;
        Vector3 pos = m.position + dir * spawnForwardOffset;

        CreateMissile(pos, dir);
    }

    private void CreateMissile(Vector3 pos, Vector3 dir)
    {
        Quaternion rot = Quaternion.LookRotation(dir);

        GameObject go;
        Missile missile;

        if (missilePrefab != null)
        {
            go = Instantiate(missilePrefab, pos, rot);
            missile = go.GetComponent<Missile>();
            if (missile == null) missile = go.AddComponent<Missile>();  // 纯视觉预制体也能用
        }
        else
        {
            // 回退：代码生成一个小球（去碰撞、去阴影）
            go = new GameObject("Missile");
            go.transform.SetPositionAndRotation(pos, rot);

            var vis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            vis.transform.SetParent(go.transform, false);
            vis.transform.localScale = Vector3.one * 0.3f;
            var col = vis.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var mr = vis.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            missile = go.AddComponent<Missile>();
        }

        // 注入飞行参数
        missile.speed = missileSpeed;
        missile.travelDistance = travelDistance;
        missile.obstacleMask = obstacleMask;

        // 注入领域参数
        missile.fieldRadius = fieldRadius;
        missile.holdDuration = holdDuration;
        missile.fadeDuration = fadeDuration;
        missile.fieldColor = fieldColor;

        // 注入领域 VFX
        missile.fieldVfxPrefab = fieldVfxPrefab;
        missile.fieldVfxDesignRadius = fieldVfxDesignRadius;
        missile.autoScaleFieldVfx = autoScaleFieldVfx;
    }
}
