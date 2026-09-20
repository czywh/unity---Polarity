using UnityEngine;

/// <summary>
/// PROP-04  Electric bridge. Energy is shown via the base color alpha (opacity) of a specified material slot:
///   empty → alpha = emptyAlpha (default 0.65, semi-transparent)
///   full  → alpha = fullAlpha (default 1, solid)
///
/// Implementation: [instantiate] the material in slot bridgeMaterialIndex and change its _BaseColor alpha every frame
/// (RGB preserved). Affects only that slot (e.g. Bridge = Element 1), leaves other materials alone (e.g. Shield_01).
///
/// Requirement: the material must have Surface Type = Transparent and Alpha Clipping off for alpha to apply smoothly.
/// </summary>
public class ElectricBridge : ElectricEntity
{
    [Header("Bridge Energy Display: material base color alpha (opacity)")]
    [Tooltip("Index of the material slot whose opacity is changed (Mesh Renderer Element index; the Bridge material is usually 1)")]
    public int bridgeMaterialIndex = 1;
    [Range(0f, 1f)]
    [Tooltip("Opacity when empty")]
    public float emptyAlpha = 0.65f;
    [Range(0f, 1f)]
    [Tooltip("Opacity when full")]
    public float fullAlpha = 1f;

    [Header("Debug (runtime read-only)")]
    [SerializeField] private float currentAlpha;

    private Material bridgeMat;   // instantiated target material (its alpha is changed)
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP
    private static readonly int ColorId = Shader.PropertyToID("_Color");         // built-in pipeline

    protected override void Start()
    {
        CacheBridgeMaterial();   // instantiate the target material first
        base.Start();            // internally calls ApplyColor(); the material is ready by then
    }

    private void CacheBridgeMaterial()
    {
        if (renderers == null || renderers.Length == 0) return;

        foreach (var r in renderers)
        {
            if (r == null) continue;
            if (bridgeMaterialIndex >= 0 && bridgeMaterialIndex < r.sharedMaterials.Length)
            {
                // Accessing r.materials instantiates this renderer's materials; take the instance in the given slot to modify
                var instances = r.materials;
                bridgeMat = instances[bridgeMaterialIndex];
                return;
            }
        }
        Debug.LogWarning($"ElectricBridge: material slot {bridgeMaterialIndex} not found; check Bridge Material Index / Mesh Renderer material count", this);
    }

    // Energy → alpha of this material's _BaseColor
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
        // Clean up instantiated materials to avoid leaks
        if (Application.isPlaying && bridgeMat != null) Destroy(bridgeMat);
    }

    private void Reset()
    {
        applyColorGradient = false;   // the bridge uses opacity instead; color gradient not needed
    }
}