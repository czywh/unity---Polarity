using UnityEngine;

/// <summary>
/// PROP-04　电子桥。电量表现 = 指定材质槽的 base color alpha（透明度）：
///   空电 → alpha = emptyAlpha（默认 0.65，半透）
///   满电 → alpha = fullAlpha（默认 1，实体）
///
/// 实现：把 bridgeMaterialIndex 指定槽的材质【实例化】，每帧改它 _BaseColor 的 alpha
/// （保留 RGB）。只影响该槽（如 Bridge = Element 1），不动其它材质（如 Shield_01）。
///
/// 前提：该材质 Surface Type = Transparent、Alpha Clipping 关闭，alpha 才会平滑生效。
/// </summary>
public class ElectricBridge : ElectricEntity
{
    [Header("桥的电量表现：材质 base color alpha（透明度）")]
    [Tooltip("要改透明度的材质槽索引（Mesh Renderer 的 Element 序号；Bridge 材质通常是 1）")]
    public int bridgeMaterialIndex = 1;
    [Range(0f, 1f)]
    [Tooltip("空电时的透明度")]
    public float emptyAlpha = 0.65f;
    [Range(0f, 1f)]
    [Tooltip("满电时的透明度")]
    public float fullAlpha = 1f;

    [Header("调试（运行时只读）")]
    [SerializeField] private float currentAlpha;

    private Material bridgeMat;   // 实例化后的目标材质（改它的 alpha）
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP
    private static readonly int ColorId = Shader.PropertyToID("_Color");         // 内置管线

    protected override void Start()
    {
        CacheBridgeMaterial();   // 先实例化目标材质
        base.Start();            // 内部会调 ApplyColor()，此时材质已就位
    }

    private void CacheBridgeMaterial()
    {
        if (renderers == null || renderers.Length == 0) return;

        foreach (var r in renderers)
        {
            if (r == null) continue;
            if (bridgeMaterialIndex >= 0 && bridgeMaterialIndex < r.sharedMaterials.Length)
            {
                // 访问 r.materials 会把该渲染器的材质实例化；取指定槽的实例来改
                var instances = r.materials;
                bridgeMat = instances[bridgeMaterialIndex];
                return;
            }
        }
        Debug.LogWarning($"ElectricBridge: 找不到材质槽 {bridgeMaterialIndex}，检查 Bridge Material Index / Mesh Renderer 材质数量", this);
    }

    // 电量 → 该材质 _BaseColor 的 alpha
    protected override void ApplyColor()
    {
        if (bridgeMat == null) return;

        float a = Mathf.Lerp(emptyAlpha, fullAlpha, Fraction);
        currentAlpha = a;

        if (bridgeMat.HasProperty(BaseColorId))
        {
            Color c = bridgeMat.GetColor(BaseColorId);
            c.a = a;
            bridgeMat.SetColor(BaseColorId, c);
        }
        if (bridgeMat.HasProperty(ColorId))
        {
            Color c = bridgeMat.GetColor(ColorId);
            c.a = a;
            bridgeMat.SetColor(ColorId, c);
        }
    }

    private void OnDestroy()
    {
        // 清理实例化出来的材质，避免泄漏
        if (Application.isPlaying && bridgeMat != null) Destroy(bridgeMat);
    }

    private void Reset()
    {
        applyColorGradient = false;   // 桥改用透明度表现，颜色渐变不需要
    }
}