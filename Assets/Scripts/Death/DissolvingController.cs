using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// Dissolve controller (multi-renderer / any-material version).
///
/// Public API unchanged: Dissolve(cb) dissolves out (0→1), Appear(cb) re-materializes (1→0), SetDissolveAmount(v).
/// Hooks directly into CharacterDeathHandler / EnemyDeath.
///
/// Difference from the old version -- the model no longer needs to use the dissolve shader up front:
///   - Normally renderers use their own materials (URP/Lit, emissive, metallic maps all fine);
///   - When dissolving starts, a dissolve material is [generated at runtime] for each original material (based on dissolveTemplate's shader and dissolve params,
///     copying the original's textures / color / metallic / smoothness / UV offset) and swapped in temporarily;
///   - When the dissolve amount returns to 0 (respawn appear finished), [the exact material objects recorded at swap-out] are swapped back,
///     so scripts like EmotionChanger that modify UVs via renderer.material instances keep working;
///   - Transparent materials (e.g. glass) can't dissolve via alpha clip: past hideTransparentAt the whole renderer is simply hidden.
///
/// Renderer source priority: renderers[] > skinnedMesh (legacy field, must be enabled) > auto-collect all enabled Mesh/SkinnedMesh renderers on children.
/// So when swapping models, just disable/delete the old renderer; nothing needs re-dragging.
/// </summary>
public class DissolvingController : MonoBehaviour
{
    [Header("Renderers")]
    [Tooltip("Legacy field: a single skinned renderer. Ignored if empty or disabled")]
    public SkinnedMeshRenderer skinnedMesh;
    [Tooltip("Renderers to dissolve. Empty = auto-collect all enabled Mesh / SkinnedMesh renderers on children")]
    public Renderer[] renderers;

    [Header("Dissolve Material Source")]
    [Tooltip("Dissolve material template; Assets/Materials/Robot.mat recommended.\nAt runtime its shader and dissolve params (color / scale / edge width) are used as the base for temp materials")]
    public Material dissolveTemplate;
    [Tooltip("Shader.Find uses this name when the template is empty")]
    public string dissolveShaderName = "Shader Graphs/DissolveShader";
    [Tooltip("Transparent materials can't dissolve: hide the whole renderer when dissolve progress >= this value; show again below it")]
    [Range(0f, 1f)] public float hideTransparentAt = 0.35f;

    [Header("Color / Glowing Edge (shared by materials and particles)")]
    [Tooltip("Dissolve edge color (HDR). Written to each dissolve material's _DissolveColor and the VFX Color param so both match")]
    [ColorUsage(true, true)] public Color dissolveColor = new Color(4.15f, 0.24f, 0f, 1f);
    [Tooltip("Glowing edge width. 0 = pure cutout, no glow; 0.03~0.08 gives a bright rim matching the particle color")]
    [Range(0f, 0.3f)] public float edgeWidth = 0.05f;
    [Tooltip("Whether to write dissolveColor / edgeWidth into materials and VFX; off = each uses its own")]
    public bool syncColor = true;

    [Header("VFX")]
    public VisualEffect VFXGraph;
    [Tooltip("Name of the exposed Color param in the VFX Graph (called Color in vx_characterDissolve)")]
    public string vfxColorProperty = "Color";
    [Tooltip("Name of the exposed Float property in the VFX Graph that receives dissolve progress (0~1); empty = not fed")]
    public string vfxProgressProperty = "";

    [Header("Dissolve Parameters")]
    public string dissolveProperty = "_DissolveAmount";
    public float dissolveRate = 0.0125f;
    public float refreshRate = 0.025f;

    [Header("Debug")]
    [Tooltip("Press Space to test dissolve. Be sure to turn off once hooked to the death system (Space is jump)")]
    public bool debugSpaceKey = false;
    [Header("Debug (runtime read-only)")]
    [SerializeField] private float amountReadout;
    [SerializeField] private int targetCountReadout;

    // ── Property names in the dissolve shader ──
    private static readonly int AlbedoId   = Shader.PropertyToID("_Albedo");
    private static readonly int NormalsId  = Shader.PropertyToID("_Normals");
    private static readonly int NormStrId  = Shader.PropertyToID("_NormalsStrength");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
    private static readonly int SmoothId   = Shader.PropertyToID("_Smoothness");
    // ── Possible source properties in the original material (URP/Lit / built-in Standard) ──
    private static readonly int BaseMapId  = Shader.PropertyToID("_BaseMap");
    private static readonly int MainTexId  = Shader.PropertyToID("_MainTex");
    private static readonly int BumpMapId  = Shader.PropertyToID("_BumpMap");
    private static readonly int BumpScaleId = Shader.PropertyToID("_BumpScale");
    private static readonly int ColorId    = Shader.PropertyToID("_Color");
    private static readonly int SurfaceId  = Shader.PropertyToID("_Surface");
    // ── Dissolve shader color / edge width / alpha clip toggle ──
    private static readonly int DissolveColorId = Shader.PropertyToID("_DissolveColor");
    private static readonly int DissolveWidthId = Shader.PropertyToID("_DissolveWidth");
    private static readonly int AlphaClipId = Shader.PropertyToID("_AlphaClip");
    private static readonly int CutoffId    = Shader.PropertyToID("_Cutoff");
    private const string AlphaTestKeyword = "_ALPHATEST_ON";

    private class Target
    {
        public Renderer renderer;
        public bool transparentOnly;      // fully transparent material: hide only
        public Material[] swappedOut;     // material objects at swap-out time (returned as-is)
        public Material[] dissolve;       // dissolve materials generated at runtime
    }

    private readonly List<Target> targets = new List<Target>();
    private Shader dissolveShader;
    private int dissolveId;
    private int vfxProgressId = -1;
    private bool hasVfxProgress;
    private bool swapped;
    private float amount;
    private Coroutine routine;

    // ────────────────────────────────────────────────────────────────
    //  Lifecycle
    // ────────────────────────────────────────────────────────────────

    private void Awake()
    {
        dissolveId = Shader.PropertyToID(dissolveProperty);

        if (dissolveTemplate != null) dissolveShader = dissolveTemplate.shader;
        if (dissolveShader == null && !string.IsNullOrEmpty(dissolveShaderName))
            dissolveShader = Shader.Find(dissolveShaderName);
        if (dissolveShader == null)
            Debug.LogError($"[Dissolve] Dissolve shader not found (template empty and Shader.Find(\"{dissolveShaderName}\") failed). " +
                           "Drag Assets/Materials/Robot.mat into the Dissolve Template slot", this);

        CollectTargets();

        if (!string.IsNullOrEmpty(vfxProgressProperty) && VFXGraph != null)
        {
            vfxProgressId = Shader.PropertyToID(vfxProgressProperty);
            hasVfxProgress = VFXGraph.HasFloat(vfxProgressId);
        }

        ApplyVfxColor();
        SetDissolveAmount(0f);
    }

    /// Write dissolveColor into the VFX Color param (particle color = dissolve edge color)
    private void ApplyVfxColor()
    {
        if (!syncColor || VFXGraph == null || string.IsNullOrEmpty(vfxColorProperty)) return;
        int id = Shader.PropertyToID(vfxColorProperty);
        if (VFXGraph.HasVector4(id)) VFXGraph.SetVector4(id, dissolveColor);
        else Debug.LogWarning($"[Dissolve] VFX has no Color param named \"{vfxColorProperty}\"; particle color not synced", this);
    }

    /// Dissolve materials must enable alpha clip, otherwise Shader Graph's clip() isn't compiled in -- no pixels disappear, leaving just a solid base color
    private static void EnsureAlphaClip(Material m)
    {
        if (m.HasProperty(AlphaClipId)) m.SetFloat(AlphaClipId, 1f);
        if (m.HasProperty(CutoffId) && m.GetFloat(CutoffId) <= 0f) m.SetFloat(CutoffId, 0.5f);
        m.EnableKeyword(AlphaTestKeyword);
        if (m.renderQueue < 2450) m.renderQueue = 2450;   // AlphaTest queue
    }

    private void ApplyEdgeStyle(Material m)
    {
        if (!syncColor) return;
        if (m.HasProperty(DissolveColorId)) m.SetColor(DissolveColorId, dissolveColor);
        if (m.HasProperty(DissolveWidthId)) m.SetFloat(DissolveWidthId, edgeWidth);
    }

    private void Update()
    {
        if (debugSpaceKey && Input.GetKeyDown(KeyCode.Space)) Dissolve();
    }

    private void OnDestroy()
    {
        foreach (var t in targets)
            if (t.dissolve != null)
                foreach (var m in t.dissolve) if (m != null) Destroy(m);
    }

    // ────────────────────────────────────────────────────────────────
    //  Public API (same as old version)
    // ────────────────────────────────────────────────────────────────

    /// <summary>Set dissolve value immediately (0 fully visible, 1 fully gone). Swaps materials in / back as needed</summary>
    public void SetDissolveAmount(float value)
    {
        amount = Mathf.Clamp01(value);
        amountReadout = amount;

        bool need = amount > 0.0001f;
        if (need && !swapped) SwapIn();
        else if (!need && swapped) SwapOut();

        foreach (var t in targets)
        {
            if (t.renderer == null) continue;
            if (t.transparentOnly)
            {
                t.renderer.enabled = amount < hideTransparentAt;
                continue;
            }
            if (!swapped || t.dissolve == null) continue;
            foreach (var m in t.dissolve)
                if (m != null) m.SetFloat(dissolveId, amount);
        }

        if (hasVfxProgress) VFXGraph.SetFloat(vfxProgressId, amount);
    }

    /// <summary>Dissolve out: 0 → 1, plays VFX. Called on death</summary>
    public void Dissolve(Action onComplete = null)
    {
        ApplyVfxColor();
        if (VFXGraph != null) VFXGraph.Play();
        Run(0f, 1f, onComplete);
    }

    /// <summary>Re-materialize: 1 → 0, stops VFX. Called on respawn</summary>
    public void Appear(Action onComplete = null)
    {
        if (VFXGraph != null) VFXGraph.Stop();
        Run(1f, 0f, onComplete);
    }

    /// <summary>Manually re-collect after changing the model / renderer list (restores materials first)</summary>
    public void RefreshTargets()
    {
        if (swapped) SwapOut();
        CollectTargets();
        SetDissolveAmount(amount);
    }

    // ────────────────────────────────────────────────────────────────
    //  Animation
    // ────────────────────────────────────────────────────────────────

    private void Run(float from, float to, Action onComplete)
    {
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(DissolveCo(from, to, onComplete));
    }

    private IEnumerator DissolveCo(float from, float to, Action onComplete)
    {
        float counter = from;
        int dir = to >= from ? 1 : -1;
        SetDissolveAmount(counter);

        var wait = new WaitForSeconds(refreshRate);
        while ((dir > 0 && counter < to) || (dir < 0 && counter > to))
        {
            counter = Mathf.Clamp01(counter + dissolveRate * dir);
            SetDissolveAmount(counter);
            yield return wait;
        }

        SetDissolveAmount(to);
        routine = null;
        onComplete?.Invoke();
    }

    // ────────────────────────────────────────────────────────────────
    //  Renderer collection
    // ────────────────────────────────────────────────────────────────

    private void CollectTargets()
    {
        targets.Clear();
        var list = new List<Renderer>();

        if (renderers != null && renderers.Length > 0)
        {
            foreach (var r in renderers) if (r != null) list.Add(r);
        }
        else if (skinnedMesh != null && skinnedMesh.enabled && skinnedMesh.gameObject.activeInHierarchy)
        {
            list.Add(skinnedMesh);
        }
        else
        {
            foreach (var r in GetComponentsInChildren<Renderer>(false))
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;   // exclude particles / VFX / trails
                if (!r.enabled) continue;
                list.Add(r);
            }
        }

        foreach (var r in list)
        {
            var mats = r.sharedMaterials;
            bool allTransparent = mats.Length > 0;
            foreach (var m in mats)
                if (!IsTransparent(m)) { allTransparent = false; break; }
            targets.Add(new Target { renderer = r, transparentOnly = allTransparent });
        }

        targetCountReadout = targets.Count;
        if (targets.Count == 0)
            Debug.LogWarning("[Dissolve] No dissolvable renderers found (renderers empty, skinnedMesh not enabled, and no Mesh/SkinnedMesh renderers on children)", this);
    }

    private static bool IsTransparent(Material m)
    {
        if (m == null) return false;
        if (m.HasProperty(SurfaceId) && m.GetFloat(SurfaceId) > 0.5f) return true;   // URP: 1 = Transparent
        return m.renderQueue >= 3000;                                                  // fallback: Transparent queue
    }

    // ────────────────────────────────────────────────────────────────
    //  Material swap in / back
    // ────────────────────────────────────────────────────────────────

    private void SwapIn()
    {
        if (dissolveShader == null) return;
        foreach (var t in targets)
        {
            if (t.renderer == null || t.transparentOnly) continue;

            // Record the material objects on the renderer [right now] (may already be instanced by other scripts); returned as-is on respawn
            t.swappedOut = t.renderer.sharedMaterials;

            if (t.dissolve == null || t.dissolve.Length != t.swappedOut.Length)
            {
                if (t.dissolve != null) foreach (var m in t.dissolve) if (m != null) Destroy(m);
                t.dissolve = new Material[t.swappedOut.Length];
                for (int i = 0; i < t.swappedOut.Length; i++)
                    t.dissolve[i] = BuildDissolveMaterial(t.swappedOut[i]);
            }
            else
            {
                // Already generated: only sync things that change at runtime (color, UV offset -- the expression system changes UV offset)
                for (int i = 0; i < t.swappedOut.Length; i++)
                {
                    SyncDynamic(t.dissolve[i], t.swappedOut[i]);
                    ApplyEdgeStyle(t.dissolve[i]);
                }
            }

            t.renderer.sharedMaterials = t.dissolve;
        }
        swapped = true;
    }

    private void SwapOut()
    {
        foreach (var t in targets)
        {
            if (t.renderer == null || t.transparentOnly || t.swappedOut == null) continue;
            t.renderer.sharedMaterials = t.swappedOut;
        }
        swapped = false;
    }

    /// Based on the template/dissolve shader, copy the original material's textures, color, metallic, smoothness, UV offset
    private Material BuildDissolveMaterial(Material src)
    {
        Material m;
        if (src != null && src.HasProperty(dissolveId))
        {
            // The original is itself a dissolve material (like the old Robot.mat): just instantiate a copy, don't modify the asset
            m = new Material(src);
        }
        else
        {
            m = dissolveTemplate != null ? new Material(dissolveTemplate) : new Material(dissolveShader);
            if (src != null) CopyStatic(m, src);
        }
        m.name = (src != null ? src.name : "Material") + " (Dissolve)";
        if (src != null) SyncDynamic(m, src);
        EnsureAlphaClip(m);
        ApplyEdgeStyle(m);
        m.SetFloat(dissolveId, 0f);
        return m;
    }

    private static void CopyStatic(Material dst, Material src)
    {
        // Normal
        if (src.HasProperty(BumpMapId) && dst.HasProperty(NormalsId))
        {
            dst.SetTexture(NormalsId, src.GetTexture(BumpMapId));
            if (src.HasProperty(BumpScaleId) && dst.HasProperty(NormStrId))
                dst.SetFloat(NormStrId, src.GetFloat(BumpScaleId));
        }
        // Metallic / smoothness (scalars; the dissolve shader has no metallic map input)
        if (src.HasProperty(MetallicId) && dst.HasProperty(MetallicId)) dst.SetFloat(MetallicId, src.GetFloat(MetallicId));
        if (src.HasProperty(SmoothId) && dst.HasProperty(SmoothId)) dst.SetFloat(SmoothId, src.GetFloat(SmoothId));
    }

    /// Parts that change at runtime: base color + main texture (incl. UV tiling/offset)
    private static void SyncDynamic(Material dst, Material src)
    {
        if (dst == null || src == null) return;

        // If the source is itself a dissolve material: copy _Albedo directly; otherwise take from URP/built-in _BaseMap / _MainTex
        int srcTexId = src.HasProperty(AlbedoId) ? AlbedoId
                     : src.HasProperty(BaseMapId) ? BaseMapId
                     : src.HasProperty(MainTexId) ? MainTexId : -1;
        if (srcTexId != -1 && dst.HasProperty(AlbedoId))
        {
            dst.SetTexture(AlbedoId, src.GetTexture(srcTexId));
            dst.SetTextureScale(AlbedoId, src.GetTextureScale(srcTexId));
            dst.SetTextureOffset(AlbedoId, src.GetTextureOffset(srcTexId));
        }

        if (dst.HasProperty(BaseColorId))
        {
            if (src.HasProperty(BaseColorId)) dst.SetColor(BaseColorId, src.GetColor(BaseColorId));
            else if (src.HasProperty(ColorId)) dst.SetColor(BaseColorId, src.GetColor(ColorId));
        }
    }
}
