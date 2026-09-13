using UnityEngine;

/// <summary>
/// CORE-04　单个电子领域运行时实体：球形范围 + 自动登记到 ElectricFieldManager。
///
/// 领域视觉（二选一，优先 VFX 预制体）：
///  · visualPrefab —— 拖入 / 由发射端传入的能量场 VFX 预制体，运行时实例化为子物体、
///    跟随 radius 缩放、褪色阶段停止粒子发射自然消散、结束时随领域一起销毁。
///  · 未配置 prefab 时回退到自动生成的半透明"调试球"（旧行为）。
///
/// 其它能力：
///  · affectsPlayerGravity —— 该领域是否让玩家进入幽灵态（免重力悬浮）。导弹领域为 true。
///  · 生命周期（useLifetime）—— 维持 holdDuration 秒后，用 fadeDuration 秒褪色再销毁，
///    结束时触发 OnExpired（导弹领域用，用于释放发射名额）。
/// </summary>
[DisallowMultipleComponent]
public class ElectricField : MonoBehaviour
{
    [Header("范围")]
    public float radius = 5f;

    [Header("玩法")]
    [Tooltip("是否让进入的玩家免重力悬浮（导弹领域为 true；充电用的即时领域为 false）")]
    public bool affectsPlayerGravity = false;

    [Header("生命周期（可选，导弹领域用）")]
    public bool useLifetime = false;
    [Tooltip("满强度维持时间")]
    public float holdDuration = 8f;
    [Tooltip("之后褪色消失的时间")]
    public float fadeDuration = 4f;

    [Header("领域视觉 VFX（优先于调试球）")]
    [Tooltip("能量场 VFX 预制体。留空则回退到自动生成的半透明调试球。\n通常由发射端（RobotFieldEmitter / MissileLauncher）在生成领域时通过 SetVisualPrefab 配置")]
    public GameObject visualPrefab;
    [Tooltip("勾选：把视觉自动缩放到当前领域半径（导弹领域膨胀时也会跟随）")]
    public bool autoScaleToRadius = true;
    [Tooltip("VFX 预制体的原始设计半径（预制体在 localScale=1 时代表多大半径）。\n用于把它等比缩放到当前 radius；不确定就填 1")]
    public float prefabDesignRadius = 1f;
    [Tooltip("进入褪色阶段时停止 VFX 粒子发射，让其自然消散（而不是被硬销毁）")]
    public bool stopEmissionOnFade = true;
    [Tooltip("褪色阶段把 VFX 整体缩小到 0（护盾内缩收场）。\n对 Looping / 长 Start Lifetime 的粒子最稳，不会在销毁瞬间硬切")]
    public bool shrinkOnFade = true;

    [Header("调试球（visualPrefab 为空时的回退）")]
    public bool showDebugSphere = true;
    [Tooltip("留空则自动生成半透明材质；以后把你的材质拖进来即可替换")]
    public Material debugMaterial;
    public Color debugColor = new Color(0.25f, 0.8f, 1f, 0.22f);

    /// 生命周期结束、领域消失时触发一次
    public event System.Action OnExpired;

    // —— 运行时视觉 ——
    private Transform visualInstance;   // 当前视觉实例（VFX 预制体 或 调试球）
    private bool visualIsPrefab;        // true=来自 visualPrefab；false=自动调试球
    private ParticleSystem[] visualParticles;
    private Material debugMat;           // 自动生成的调试球材质实例（用于褪色）
    private bool createdMat;

    private float lifeTimer;
    private bool fadeStarted;
    private float fadeStartRadius;   // 进入褪色时的半径，用于按比例收缩

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    /// 便捷开启生命周期
    public void SetLifetime(float hold, float fade)
    {
        useLifetime = true;
        holdDuration = hold;
        fadeDuration = fade;
    }

    /// 由发射端在生成领域后调用，指定该领域使用哪个 VFX 预制体。
    /// 可在任意时刻调用：若已生成旧视觉会自动替换。
    public void SetVisualPrefab(GameObject prefab)
    {
        if (visualPrefab == prefab) return;
        visualPrefab = prefab;

        if (visualInstance != null)   // 已有旧视觉 → 拆掉重建
        {
            Destroy(visualInstance.gameObject);
            visualInstance = null;
            visualParticles = null;
            debugMat = null;
            createdMat = false;
        }
    }

    private void OnEnable()
    {
        ElectricFieldManager.Instance.Register(this);
        // 视觉延迟到 Update 里惰性创建，这样发射端在 Instantiate 之后、首帧之前
        // 设置的 visualPrefab / radius 都能正确生效。
    }

    private void OnDisable()
    {
        if (ElectricFieldManager.Instance != null)
            ElectricFieldManager.Instance.Unregister(this);
    }

    private void Update()
    {
        EnsureVisual();

        if (visualInstance != null)
        {
            bool show = visualIsPrefab || showDebugSphere;
            visualInstance.gameObject.SetActive(show);
            if (show) ApplyVisualScale();
        }

        if (useLifetime) TickLifetime();
    }

    // 惰性创建视觉：有 visualPrefab 用 prefab，否则（且 showDebugSphere）用调试球
    private void EnsureVisual()
    {
        if (visualInstance != null) return;

        if (visualPrefab != null)
        {
            var go = Instantiate(visualPrefab);
            go.name = "FieldVFX";
            var t = go.transform;
            t.SetParent(transform, false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            visualInstance = t;
            visualIsPrefab = true;
            visualParticles = go.GetComponentsInChildren<ParticleSystem>(true);
        }
        else if (showDebugSphere)
        {
            visualInstance = CreateDebugSphere();
            visualIsPrefab = false;
        }
    }

    // 视觉缩放：VFX 按 radius/prefabDesignRadius 等比；调试球按直径 = radius*2
    private void ApplyVisualScale()
    {
        if (!autoScaleToRadius || visualInstance == null) return;
        visualInstance.localScale = Vector3.one * BaseVisualScale();
    }

    private float BaseVisualScale()
    {
        if (visualIsPrefab)
            return prefabDesignRadius > 0.0001f ? radius / prefabDesignRadius : radius;
        return radius * 2f;   // 调试球：直径 = radius*2
    }

    private void TickLifetime()
    {
        lifeTimer += Time.deltaTime;

        if (lifeTimer >= holdDuration)
        {
            // 进入褪色阶段：首次进入时记录起始半径，并（VFX）停止发射
            if (!fadeStarted)
            {
                fadeStarted = true;
                fadeStartRadius = radius;
                if (visualIsPrefab && stopEmissionOnFade && visualParticles != null)
                {
                    foreach (var ps in visualParticles)
                        if (ps != null)
                            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }

            float fadeT = Mathf.Clamp01((lifeTimer - holdDuration) / Mathf.Max(0.0001f, fadeDuration));
            if (visualIsPrefab)
            {
                // 收缩真实半径：视觉（按 radius 缩放）与悬浮判定（Contains 用 radius）一起收，
                // 领域边界内收扫过谁谁就落地，杜绝"看不见还在飘"。
                if (shrinkOnFade) radius = fadeStartRadius * (1f - fadeT);
            }
            else
            {
                ApplyFadeAlpha(1f - fadeT);   // 调试球：满尺寸走材质 alpha 褪色
            }

            if (lifeTimer >= holdDuration + fadeDuration)
                Expire();
        }
    }

    private void Expire()
    {
        OnExpired?.Invoke();
        Destroy(gameObject);   // 子物体（视觉实例）一并销毁；OnDisable 会自动注销
    }

    /// 某点是否在本领域内
    public bool Contains(Vector3 point)
    {
        return (point - transform.position).sqrMagnitude <= radius * radius;
    }

    // —— 调试球（回退方案）——
    private Transform CreateDebugSphere()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "DebugSphere";

        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
        var mr = go.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        if (debugMaterial != null)
        {
            mr.sharedMaterial = debugMaterial;
            createdMat = false;
        }
        else
        {
            debugMat = CreateTransparentMaterial(debugColor);
            mr.sharedMaterial = debugMat;
            createdMat = true;
        }

        var t = go.transform;
        t.SetParent(transform, false);
        t.localPosition = Vector3.zero;
        t.localScale = Vector3.one * radius * 2f;
        return t;
    }

    // 褪色：把自动生成材质的 alpha 按比例调低（只对自动材质生效，不动用户指定材质）
    private void ApplyFadeAlpha(float factor)
    {
        if (!createdMat || debugMat == null) return;
        Color c = debugColor;
        c.a = debugColor.a * factor;
        debugMat.SetColor(BaseColorId, c);
        debugMat.SetColor(ColorId, c);
    }

    private static Material CreateTransparentMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        bool urp = shader != null;
        if (shader == null) shader = Shader.Find("Standard");

        var m = new Material(shader);
        if (urp)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetColor("_BaseColor", color);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_SURFACE_TYPE_OPAQUE");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        else if (shader != null && shader.name == "Standard")
        {
            m.SetFloat("_Mode", 3f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_ALPHABLEND_ON");
            m.renderQueue = 3000;
            m.color = color;
        }
        else if (m != null)
        {
            m.color = color;
        }
        return m;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(debugColor.r, debugColor.g, debugColor.b, 0.6f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}