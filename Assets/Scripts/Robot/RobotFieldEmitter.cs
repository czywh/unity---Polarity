using UnityEngine;

/// <summary>
/// ROBOT-04　电子领域发射器：挂在机器人上，实现 IFieldEmitter。
/// 机器人视角下按 E → ToggleField()：以机器人为中心生成 / 销毁即时电子领域。
///
/// 电量联动（CORE-06）：
///  · 开领域前检查电量，空电则拒绝开启；
///  · 领域开启期间持续耗电（即使切到本体，领域仍在，照常耗电）；
///  · 电量耗尽 → 自动关闭领域。
///
/// 领域视觉（可选）：在下面配 fieldVfxPrefab（如 shield 粒子），
/// 生成领域时通过 ElectricField.SetVisualPrefab 传入；留空则回退到调试球。
/// </summary>
public class RobotFieldEmitter : MonoBehaviour, IFieldEmitter
{
    [Header("领域参数")]
    public float fieldRadius = 3f;
    [Tooltip("领域中心；留空用机器人自身")]
    public Transform fieldOrigin;
    [Tooltip("可选自定义领域预制体；留空则运行时生成")]
    public ElectricField fieldPrefab;

    [Header("电量消耗")]
    [Tooltip("领域开启时每秒消耗的电量")]
    public float drainPerSecond = 10f;

    [Header("领域视觉 VFX")]
    [Tooltip("领域的能量场 VFX 预制体（如 shield 粒子）。留空则用下面的调试球")]
    public GameObject fieldVfxPrefab;
    [Tooltip("VFX 预制体的设计半径。shield 的 Start Size=7 → 先填 3.5，再对着线框球微调")]
    public float vfxDesignRadius = 2.1f;
    [Tooltip("把 VFX 自动缩放到 fieldRadius")]
    public bool autoScaleVfx = true;

    [Header("调试可视化")]
    public bool showDebugSphere = true;
    [Tooltip("领域材质；留空自动用半透明调试材质")]
    public Material debugMaterial;

    private ElectricField activeField;
    private EnergySystem energy;
    public bool IsActive => activeField != null;

    private void Awake()
    {
        energy = GetComponent<EnergySystem>();
    }

    // 由 RobotController 在按下 E 时调用
    public void ToggleField()
    {
        if (activeField != null) Close();
        else Open();
    }

    private void Open()
    {
        // 没电不能开
        if (energy != null && energy.IsEmpty)
        {
            Debug.Log("[Field] 电量耗尽，无法开启领域", this);
            return;
        }

        Transform origin = fieldOrigin != null ? fieldOrigin : transform;

        if (fieldPrefab != null)
        {
            activeField = Instantiate(fieldPrefab, origin.position, Quaternion.identity, origin);
        }
        else
        {
            var go = new GameObject("ElectricField (Instant)");
            go.transform.SetParent(origin, false);
            go.transform.localPosition = Vector3.zero;
            activeField = go.AddComponent<ElectricField>();
        }

        activeField.radius = fieldRadius;
        activeField.showDebugSphere = showDebugSphere;
        if (debugMaterial != null) activeField.debugMaterial = debugMaterial;

        // 配了 VFX 才覆盖领域视觉；留空则尊重领域预制体自带的设置 / 回退到调试球
        if (fieldVfxPrefab != null)
        {
            activeField.SetVisualPrefab(fieldVfxPrefab);
            activeField.prefabDesignRadius = vfxDesignRadius;
            activeField.autoScaleToRadius = autoScaleVfx;
        }
    }

    private void Update()
    {
        // 领域开启期间持续耗电；耗尽则自动关闭
        if (activeField != null && energy != null)
        {
            energy.Drain(drainPerSecond * Time.deltaTime);
            if (energy.IsEmpty) Close();
        }
    }

    private void Close()
    {
        if (activeField != null) Destroy(activeField.gameObject);
        activeField = null;
    }
}