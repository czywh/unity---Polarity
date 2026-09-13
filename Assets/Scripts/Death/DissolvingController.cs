using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// 溶解控制器（死亡效果适配版）。
/// 保留原有的 skinnedMesh + VFXGraph 结构，额外提供可被死亡流程调用的接口：
///   Dissolve(cb) 溶解消失(0→1)、Appear(cb) 重新显形(1→0)、SetDissolveAmount(v) 立即设置。
/// 与 CharacterDeathHandler 直接对接：死亡播溶解、等待、复活时显形。
/// </summary>
public class DissolvingController : MonoBehaviour
{
    [Header("渲染 / VFX")]
    public SkinnedMeshRenderer skinnedMesh;
    public VisualEffect VFXGraph;

    [Header("溶解参数")]
    public string dissolveProperty = "_DissolveAmount";
    public float dissolveRate = 0.0125f;
    public float refreshRate = 0.025f;

    [Header("VFX 联动（可选）")]
    [Tooltip("VFX Graph 里暴露的 Float 属性名，用来接收溶解进度(0~1)，让粒子跟着溶解边缘走；留空则不喂")]
    public string vfxProgressProperty = "";

    [Header("调试")]
    [Tooltip("按空格测试溶解。接上死亡系统后关掉，避免和复活流程冲突")]
    public bool debugSpaceKey = true;

    private Material[] skinnedMaterials;
    private int dissolveId;
    private int vfxProgressId = -1;
    private bool hasVfxProgress;
    private Coroutine routine;

    void Awake()
    {
        dissolveId = Shader.PropertyToID(dissolveProperty);

        if (skinnedMesh != null)
        {
            skinnedMaterials = skinnedMesh.materials;

            if (skinnedMaterials.Length > 0 && !skinnedMaterials[0].HasProperty(dissolveId))
                Debug.LogError("材质里找不到 " + dissolveProperty + "，去 Shader Graph 核对 Reference 名字");
        }
        else
        {
            skinnedMaterials = new Material[0];
        }

        if (!string.IsNullOrEmpty(vfxProgressProperty) && VFXGraph != null)
        {
            vfxProgressId = Shader.PropertyToID(vfxProgressProperty);
            hasVfxProgress = VFXGraph.HasFloat(vfxProgressId);
        }

        SetDissolveAmount(0f); // 初始完全显示
    }

    void Update()
    {
        if (debugSpaceKey && Input.GetKeyDown(KeyCode.Space))
        {
            Dissolve();
        }
    }

    /// <summary>立即设置溶解值（0 完全显示，1 完全消失）。</summary>
    public void SetDissolveAmount(float amount)
    {
        for (int i = 0; i < skinnedMaterials.Length; i++)
            skinnedMaterials[i].SetFloat(dissolveId, amount);

        if (hasVfxProgress) VFXGraph.SetFloat(vfxProgressId, amount);
    }

    /// <summary>溶解消失：0 → 1，会播放 VFX。死亡时调用。</summary>
    public void Dissolve(Action onComplete = null)
    {
        if (VFXGraph != null) VFXGraph.Play();
        Run(0f, 1f, onComplete);
    }

    /// <summary>重新显形：1 → 0，停止 VFX。复活时调用。</summary>
    public void Appear(Action onComplete = null)
    {
        if (VFXGraph != null) VFXGraph.Stop();
        Run(1f, 0f, onComplete);
    }

    void Run(float from, float to, Action onComplete)
    {
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(DissolveCo(from, to, onComplete));
    }

    IEnumerator DissolveCo(float from, float to, Action onComplete)
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
}