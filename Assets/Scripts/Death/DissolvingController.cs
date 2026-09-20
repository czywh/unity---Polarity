using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// 溶解控制器（多渲染器 / 任意材质版）。
///
/// 对外接口不变：Dissolve(cb) 溶解消失(0→1)、Appear(cb) 重新显形(1→0)、SetDissolveAmount(v)。
/// 与 CharacterDeathHandler / EnemyDeath 直接对接。
///
/// 和旧版的区别 —— 模型不必事先用溶解 shader：
///   · 平时渲染器用它们自己的材质（URP/Lit、带自发光、金属贴图都行）；
///   · 溶解开始时，为每个原材质【运行时生成】一份溶解材质（以 dissolveTemplate 的 shader 与溶解参数为底，
///     把原材质的贴图 / 颜色 / 金属度 / 光滑度 / UV 偏移复制过去），临时换上；
///   · 溶解量回到 0（复活显形完毕）时，把【换出去时记下的那几个材质对象】原样换回，
///     所以 EmotionChanger 之类靠 renderer.material 实例改 UV 的脚本不会失效；
///   · 透明材质（如玻璃）无法用 alpha clip 溶解：进度超过 hideTransparentAt 时整个渲染器直接隐藏。
///
/// 渲染器来源优先级：renderers[] > skinnedMesh（旧字段，且必须启用中） > 自动收集子物体上所有启用的 Mesh/SkinnedMesh 渲染器。
/// 所以换模型只要把旧渲染器禁用/删除，什么都不用重新拖。
/// </summary>
public class DissolvingController : MonoBehaviour
{
    [Header("渲染器")]
    [Tooltip("旧字段：单个蒙皮渲染器。留空或已禁用时忽略")]
    public SkinnedMeshRenderer skinnedMesh;
    [Tooltip("要溶解的渲染器列表。留空 = 自动收集子物体上所有启用的 Mesh / SkinnedMesh 渲染器")]
    public Renderer[] renderers;

    [Header("溶解材质来源")]
    [Tooltip("溶解材质模板，建议拖 Assets/Materials/Robot.mat。\n运行时以它的 shader 和溶解参数（颜色 / 尺度 / 边宽）为底生成临时材质")]
    public Material dissolveTemplate;
    [Tooltip("模板留空时用这个名字 Shader.Find")]
    public string dissolveShaderName = "Shader Graphs/DissolveShader";
    [Tooltip("透明材质无法溶解：溶解进度 ≥ 此值时把该渲染器整个隐藏；回到此值以下再显示")]
    [Range(0f, 1f)] public float hideTransparentAt = 0.35f;

    [Header("颜色 / 发光边缘（材质与粒子共用一套）")]
    [Tooltip("溶解边缘颜色（HDR）。同时写进每个溶解材质的 _DissolveColor 和 VFX 的 Color 参数，保证两边一致")]
    [ColorUsage(true, true)] public Color dissolveColor = new Color(4.15f, 0.24f, 0f, 1f);
    [Tooltip("发光边缘宽度。0 = 纯镂空无发光；0.03~0.08 有一圈亮边，和粒子颜色呼应")]
    [Range(0f, 0.3f)] public float edgeWidth = 0.05f;
    [Tooltip("是否把 dissolveColor / edgeWidth 写进材质与 VFX；关掉则各用各的")]
    public bool syncColor = true;

    [Header("VFX")]
    public VisualEffect VFXGraph;
    [Tooltip("VFX Graph 里暴露的 Color 参数名（vx_characterDissolve 里叫 Color）")]
    public string vfxColorProperty = "Color";
    [Tooltip("VFX Graph 里暴露的 Float 属性名，用来接收溶解进度(0~1)；留空则不喂")]
    public string vfxProgressProperty = "";

    [Header("溶解参数")]
    public string dissolveProperty = "_DissolveAmount";
    public float dissolveRate = 0.0125f;
    public float refreshRate = 0.025f;

    [Header("调试")]
    [Tooltip("按空格测试溶解。接上死亡系统后务必关掉（空格是跳跃键）")]
    public bool debugSpaceKey = false;
    [Header("调试（运行时只读）")]
    [SerializeField] private float amountReadout;
    [SerializeField] private int targetCountReadout;

    // ── 溶解 shader 里的属性名 ──
    private static readonly int AlbedoId   = Shader.PropertyToID("_Albedo");
    private static readonly int NormalsId  = Shader.PropertyToID("_Normals");
    private static readonly int NormStrId  = Shader.PropertyToID("_NormalsStrength");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
    private static readonly int SmoothId   = Shader.PropertyToID("_Smoothness");
    // ── 原材质（URP/Lit / 内置 Standard）里可能的来源属性 ──
    private static readonly int BaseMapId  = Shader.PropertyToID("_BaseMap");
    private static readonly int MainTexId  = Shader.PropertyToID("_MainTex");
    private static readonly int BumpMapId  = Shader.PropertyToID("_BumpMap");
    private static readonly int BumpScaleId = Shader.PropertyToID("_BumpScale");
    private static readonly int ColorId    = Shader.PropertyToID("_Color");
    private static readonly int SurfaceId  = Shader.PropertyToID("_Surface");
    // ── 溶解 shader 的颜色 / 边宽 / alpha clip 开关 ──
    private static readonly int DissolveColorId = Shader.PropertyToID("_DissolveColor");
    private static readonly int DissolveWidthId = Shader.PropertyToID("_DissolveWidth");
    private static readonly int AlphaClipId = Shader.PropertyToID("_AlphaClip");
    private static readonly int CutoffId    = Shader.PropertyToID("_Cutoff");
    private const string AlphaTestKeyword = "_ALPHATEST_ON";

    private class Target
    {
        public Renderer renderer;
        public bool transparentOnly;      // 全透明材质：只做隐藏
        public Material[] swappedOut;     // 换出去那一刻的材质对象（原样还回）
        public Material[] dissolve;       // 运行时生成的溶解材质
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
    //  生命周期
    // ────────────────────────────────────────────────────────────────

    private void Awake()
    {
        dissolveId = Shader.PropertyToID(dissolveProperty);

        if (dissolveTemplate != null) dissolveShader = dissolveTemplate.shader;
        if (dissolveShader == null && !string.IsNullOrEmpty(dissolveShaderName))
            dissolveShader = Shader.Find(dissolveShaderName);
        if (dissolveShader == null)
            Debug.LogError($"[溶解] 找不到溶解 shader（模板为空且 Shader.Find(\"{dissolveShaderName}\") 失败）。" +
                           "把 Assets/Materials/Robot.mat 拖到 Dissolve Template 槽即可", this);

        CollectTargets();

        if (!string.IsNullOrEmpty(vfxProgressProperty) && VFXGraph != null)
        {
            vfxProgressId = Shader.PropertyToID(vfxProgressProperty);
            hasVfxProgress = VFXGraph.HasFloat(vfxProgressId);
        }

        ApplyVfxColor();
        SetDissolveAmount(0f);
    }

    /// 把 dissolveColor 写进 VFX 的 Color 参数（粒子颜色 = 溶解边缘颜色）
    private void ApplyVfxColor()
    {
        if (!syncColor || VFXGraph == null || string.IsNullOrEmpty(vfxColorProperty)) return;
        int id = Shader.PropertyToID(vfxColorProperty);
        if (VFXGraph.HasVector4(id)) VFXGraph.SetVector4(id, dissolveColor);
        else Debug.LogWarning($"[溶解] VFX 里没有名为 \"{vfxColorProperty}\" 的 Color 参数，粒子颜色未同步", this);
    }

    /// 溶解材质必须开 alpha clip，否则 Shader Graph 的 clip() 不会编进去 —— 像素一个都不消失，只剩一片底色
    private static void EnsureAlphaClip(Material m)
    {
        if (m.HasProperty(AlphaClipId)) m.SetFloat(AlphaClipId, 1f);
        if (m.HasProperty(CutoffId) && m.GetFloat(CutoffId) <= 0f) m.SetFloat(CutoffId, 0.5f);
        m.EnableKeyword(AlphaTestKeyword);
        if (m.renderQueue < 2450) m.renderQueue = 2450;   // AlphaTest 队列
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
    //  对外接口（与旧版一致）
    // ────────────────────────────────────────────────────────────────

    /// <summary>立即设置溶解值（0 完全显示，1 完全消失）。会按需换入 / 换回材质</summary>
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

    /// <summary>溶解消失：0 → 1，会播放 VFX。死亡时调用</summary>
    public void Dissolve(Action onComplete = null)
    {
        ApplyVfxColor();
        if (VFXGraph != null) VFXGraph.Play();
        Run(0f, 1f, onComplete);
    }

    /// <summary>重新显形：1 → 0，停止 VFX。复活时调用</summary>
    public void Appear(Action onComplete = null)
    {
        if (VFXGraph != null) VFXGraph.Stop();
        Run(1f, 0f, onComplete);
    }

    /// <summary>换了模型 / 改了渲染器列表后可手动重新收集（会先还原材质）</summary>
    public void RefreshTargets()
    {
        if (swapped) SwapOut();
        CollectTargets();
        SetDissolveAmount(amount);
    }

    // ────────────────────────────────────────────────────────────────
    //  动画
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
    //  渲染器收集
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
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;   // 排除粒子 / VFX / 拖尾
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
            Debug.LogWarning("[溶解] 没找到任何可溶解的渲染器（renderers 为空、skinnedMesh 未启用、子物体也没有 Mesh/SkinnedMesh 渲染器）", this);
    }

    private static bool IsTransparent(Material m)
    {
        if (m == null) return false;
        if (m.HasProperty(SurfaceId) && m.GetFloat(SurfaceId) > 0.5f) return true;   // URP：1 = Transparent
        return m.renderQueue >= 3000;                                                  // 兜底：Transparent 队列
    }

    // ────────────────────────────────────────────────────────────────
    //  材质换入 / 换回
    // ────────────────────────────────────────────────────────────────

    private void SwapIn()
    {
        if (dissolveShader == null) return;
        foreach (var t in targets)
        {
            if (t.renderer == null || t.transparentOnly) continue;

            // 记下【此刻】渲染器上的材质对象（可能已被别的脚本实例化过），复活时原样还回
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
                // 已生成过：只同步会在运行时变化的东西（颜色、UV 偏移 —— 表情系统改的是 UV 偏移）
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

    /// 以模板/溶解 shader 为底，复制原材质的贴图、颜色、金属度、光滑度、UV 偏移
    private Material BuildDissolveMaterial(Material src)
    {
        Material m;
        if (src != null && src.HasProperty(dissolveId))
        {
            // 原材质本身就是溶解材质（旧 Robot.mat 这种）：直接实例化一份，别改到资源
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
        // 法线
        if (src.HasProperty(BumpMapId) && dst.HasProperty(NormalsId))
        {
            dst.SetTexture(NormalsId, src.GetTexture(BumpMapId));
            if (src.HasProperty(BumpScaleId) && dst.HasProperty(NormStrId))
                dst.SetFloat(NormStrId, src.GetFloat(BumpScaleId));
        }
        // 金属度 / 光滑度（标量；溶解 shader 没有金属贴图输入）
        if (src.HasProperty(MetallicId) && dst.HasProperty(MetallicId)) dst.SetFloat(MetallicId, src.GetFloat(MetallicId));
        if (src.HasProperty(SmoothId) && dst.HasProperty(SmoothId)) dst.SetFloat(SmoothId, src.GetFloat(SmoothId));
    }

    /// 运行时会变的部分：基础色 + 主贴图（含 UV tiling/offset）
    private static void SyncDynamic(Material dst, Material src)
    {
        if (dst == null || src == null) return;

        // 源材质本身就是溶解材质：_Albedo 对拷；否则从 URP/内置的 _BaseMap / _MainTex 取
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
