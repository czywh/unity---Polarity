using UnityEngine;

/// <summary>
/// CORE-04 Runtime entity for a single electric field: spherical range + auto-registers with ElectricFieldManager.
///
/// Field visuals (one of two, VFX prefab preferred):
///  - visualPrefab -- energy field VFX prefab dragged in / passed by the emitter; instantiated as a child at runtime,
///    scales with radius, stops particle emission during fade to dissipate naturally, destroyed along with the field.
///  - Without a prefab, falls back to an auto-generated semi-transparent "debug sphere" (old behavior).
///
/// Other features:
///  - affectsPlayerGravity -- whether this field puts the player into phantom state (gravity-free floating). True for missile fields.
///  - Lifetime (useLifetime) -- holds for holdDuration seconds, then fades over fadeDuration seconds and is destroyed,
///    firing OnExpired at the end (used by missile fields to free a launch slot).
/// </summary>
[DisallowMultipleComponent]
public class ElectricField : MonoBehaviour
{
    [Header("Range")]
    public float radius = 5f;

    [Header("Gameplay")]
    [Tooltip("Whether players inside float gravity-free (true for missile fields; false for instant charging fields)")]
    public bool affectsPlayerGravity = false;

    [Header("Lifetime (optional, for missile fields)")]
    public bool useLifetime = false;
    [Tooltip("Time held at full strength")]
    public float holdDuration = 8f;
    [Tooltip("Time to fade out afterwards")]
    public float fadeDuration = 4f;

    [Header("Field Visual VFX (takes priority over debug sphere)")]
    [Tooltip("Energy field VFX prefab. Leave empty to fall back to the auto-generated semi-transparent debug sphere.\nUsually set by the emitter (RobotFieldEmitter / MissileLauncher) via SetVisualPrefab when spawning the field")]
    public GameObject visualPrefab;
    [Tooltip("Checked: auto-scale the visual to the current field radius (also follows missile field expansion)")]
    public bool autoScaleToRadius = true;
    [Tooltip("Original design radius of the VFX prefab (what radius it represents at localScale=1).\nUsed to scale it uniformly to the current radius; if unsure, use 1")]
    public float prefabDesignRadius = 1f;
    [Tooltip("Stop VFX particle emission when fading starts so it dissipates naturally (instead of being hard-destroyed)")]
    public bool stopEmissionOnFade = true;
    [Tooltip("Shrink the whole VFX to 0 during fade (shield collapses inward).\nMost robust for Looping / long Start Lifetime particles; no hard cut at destroy")]
    public bool shrinkOnFade = true;

    [Header("Debug Sphere (fallback when visualPrefab is empty)")]
    public bool showDebugSphere = true;
    [Tooltip("Leave empty to auto-generate a semi-transparent material; drag your own material here later to replace it")]
    public Material debugMaterial;
    public Color debugColor = new Color(0.25f, 0.8f, 1f, 0.22f);

    /// Fired once when the lifetime ends and the field disappears
    public event System.Action OnExpired;

    // -- Runtime visuals --
    private Transform visualInstance;   // Current visual instance (VFX prefab or debug sphere)
    private bool visualIsPrefab;        // true = from visualPrefab; false = auto debug sphere
    private ParticleSystem[] visualParticles;
    private Material debugMat;           // Auto-generated debug sphere material instance (for fading)
    private bool createdMat;

    private float lifeTimer;
    private bool fadeStarted;
    private float fadeStartRadius;   // Radius when fading started, used for proportional shrinking

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    /// Convenience: enable lifetime
    public void SetLifetime(float hold, float fade)
    {
        useLifetime = true;
        holdDuration = hold;
        fadeDuration = fade;
    }

    /// Called by the emitter after spawning the field to specify which VFX prefab it uses.
    /// Can be called at any time: an existing old visual is replaced automatically.
    public void SetVisualPrefab(GameObject prefab)
    {
        if (visualPrefab == prefab) return;
        visualPrefab = prefab;

        if (visualInstance != null)   // Old visual exists → tear down and rebuild
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
        // Visual is lazily created in Update, so visualPrefab / radius set by the emitter
        // after Instantiate but before the first frame take effect correctly.
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

    // Lazily create the visual: use visualPrefab if set, otherwise (if showDebugSphere) the debug sphere
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
            StripColliders(go);   // a field is a volume, never a physical obstacle (lasers must pass through it)
        }
        else if (showDebugSphere)
        {
            visualInstance = CreateDebugSphere();
            visualIsPrefab = false;
        }
    }

    // Visual scale: VFX uniformly by radius/prefabDesignRadius; debug sphere by diameter = radius*2
    private void ApplyVisualScale()
    {
        if (!autoScaleToRadius || visualInstance == null) return;
        visualInstance.localScale = Vector3.one * BaseVisualScale();
    }

    private float BaseVisualScale()
    {
        if (visualIsPrefab)
            return prefabDesignRadius > 0.0001f ? radius / prefabDesignRadius : radius;
        return radius * 2f;   // Debug sphere: diameter = radius*2
    }

    private void TickLifetime()
    {
        lifeTimer += Time.deltaTime;

        if (lifeTimer >= holdDuration)
        {
            // Entering fade: on first entry record the starting radius and (VFX) stop emission
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
                // Shrink the actual radius: visual (scaled by radius) and float check (Contains uses radius) shrink together,
                // whoever the inward-moving field boundary passes lands, preventing "invisible but still floating".
                if (shrinkOnFade) radius = fadeStartRadius * (1f - fadeT);
            }
            else
            {
                ApplyFadeAlpha(1f - fadeT);   // Debug sphere: full size, fade via material alpha
            }

            if (lifeTimer >= holdDuration + fadeDuration)
                Expire();
        }
    }

    private void Expire()
    {
        OnExpired?.Invoke();
        Destroy(gameObject);   // Children (visual instance) destroyed too; OnDisable auto-unregisters
    }

    /// Whether a point is inside this field
    public bool Contains(Vector3 point)
    {
        return (point - transform.position).sqrMagnitude <= radius * radius;
    }

    // -- Debug sphere (fallback) --
    private Transform CreateDebugSphere()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "DebugSphere";

        StripColliders(go);   // disabled right away (Destroy is deferred to end of frame, which would let a laser hit it)
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

    // Field visuals must never block raycasts / lasers: disable every collider immediately, then destroy it
    private static void StripColliders(GameObject go)
    {
        foreach (var c in go.GetComponentsInChildren<Collider>(true))
        {
            c.enabled = false;
            Destroy(c);
        }
    }

    // Fade: scale down the auto-generated material's alpha proportionally (only affects the auto material, not user-assigned ones)
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