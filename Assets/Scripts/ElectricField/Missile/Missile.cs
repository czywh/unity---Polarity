using UnityEngine;

/// <summary>
/// ROBOT-06　导弹飞行体：向前直飞，飞够 travelDistance 或撞到障碍后，
/// 在当前位置展开成一个带生命周期的电子领域（影响玩家重力），随后销毁自身。
/// 参数由 MissileLauncher 在生成时注入。
///
/// 领域视觉：若注入了 fieldVfxPrefab，展开的领域用该 VFX（定时领域会走褪色缩小收场）；
/// 留空则回退到调试球（用 fieldColor 上色）。
/// </summary>
public class Missile : MonoBehaviour
{
    [HideInInspector] public float speed = 20f;
    [HideInInspector] public float travelDistance = 10f;
    [HideInInspector] public LayerMask obstacleMask = ~0;

    // 展开出的领域配置
    [HideInInspector] public float fieldRadius = 4f;
    [HideInInspector] public float holdDuration = 8f;
    [HideInInspector] public float fadeDuration = 4f;
    [HideInInspector] public Color fieldColor = new Color(0.85f, 0.4f, 0.95f, 0.28f);

    // 领域视觉 VFX（由 MissileLauncher 注入；留空用调试球）
    [HideInInspector] public GameObject fieldVfxPrefab;
    [HideInInspector] public float fieldVfxDesignRadius = 3.5f;
    [HideInInspector] public bool autoScaleFieldVfx = true;

    private Vector3 startPos;
    private bool expanded;

    private void Start()
    {
        startPos = transform.position;
    }

    private void Update()
    {
        if (expanded) return;

        float step = speed * Time.deltaTime;

        // 撞到障碍 → 提前在命中点展开
        if (Physics.Raycast(transform.position, transform.forward, out RaycastHit hit, step,
                            obstacleMask, QueryTriggerInteraction.Ignore))
        {
            transform.position = hit.point;
            Expand();
            return;
        }

        transform.position += transform.forward * step;

        if (Vector3.Distance(startPos, transform.position) >= travelDistance)
            Expand();
    }

    private void Expand()
    {
        if (expanded) return;
        expanded = true;

        // 先建成 inactive，配置好再激活 —— 保证 ElectricField.OnEnable 时颜色/属性已就位
        var go = new GameObject("MissileField");
        go.SetActive(false);
        go.transform.position = transform.position;

        var field = go.AddComponent<ElectricField>();
        field.radius = fieldRadius;
        field.affectsPlayerGravity = true;             // 导弹领域让玩家悬浮
        field.SetLifetime(holdDuration, fadeDuration); // 维持 + 褪色后自行消失

        if (fieldVfxPrefab != null)
        {
            // 用 VFX 预制体作为领域视觉（定时领域会走 shrinkOnFade 缩小收场）
            field.SetVisualPrefab(fieldVfxPrefab);
            field.prefabDesignRadius = fieldVfxDesignRadius;
            field.autoScaleToRadius = autoScaleFieldVfx;
            field.showDebugSphere = false;
        }
        else
        {
            // 回退：调试球，用 fieldColor 上色
            field.showDebugSphere = true;
            field.debugColor = fieldColor;
        }

        go.SetActive(true);

        Destroy(gameObject);
    }
}