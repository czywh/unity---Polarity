using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 激光塔：开局生成【可调数量】的激光 prefab 并常驻发射（替代 Hovl_DemoLasers 点击发射）。
/// 支持两种排布：
///   · Fan（扇形）：以发射点为中心，多条激光按角度均匀散开。
///   · Parallel（平行）：多条激光平行排列，按间距横向铺开。
/// 可在 Inspector 直接改数量、prefab、位置 / 朝向 / 长度 / 缩放。
/// 每条激光实例上应带 Hovl_Laser（视觉）+ LaserBarrier（击退 / 扣电）。
///
/// 开关：SetFiring(bool) / Toggle()，由 LaserTowerSwitch 等外部控制。
/// </summary>
public class LaserTower : MonoBehaviour
{
    public enum Layout { Fan, Parallel }

    [Header("激光 prefab")]
    [Tooltip("要发射的激光 prefab（带 Hovl_Laser + LaserBarrier）")]
    public GameObject laserPrefab;

    [Header("数量 / 排布")]
    [Tooltip("激光条数")]
    [Min(1)] public int count = 1;
    public Layout layout = Layout.Fan;
    [Tooltip("Fan：相邻两条的角度间隔（度）")]
    public float fanAngleStep = 15f;
    [Tooltip("Parallel：相邻两条的横向间距（米）")]
    public float parallelSpacing = 1f;

    [Header("发射点 / 朝向")]
    [Tooltip("发射起点；留空用本物体自身 transform")]
    public Transform firePoint;
    [Tooltip("在发射点基础上的额外位置偏移（本地）")]
    public Vector3 localOffset = Vector3.zero;
    [Tooltip("在发射点基础上的额外旋转（欧拉角）")]
    public Vector3 localEuler = Vector3.zero;

    [Header("参数")]
    public float maxLength = 40f;
    [Tooltip("激光整体缩放（调 prefab 效果大小）")]
    public Vector3 laserScale = Vector3.one;
    [Tooltip("生成的激光是否作为本塔子物体（跟随塔移动/旋转）")]
    public bool parentToTower = true;

    [Header("开关")]
    [Tooltip("开局是否自动发射。挂了 LaserTowerSwitch 的话以开关的 startOn 为准")]
    public bool fireOnStart = true;

    [Header("运行时")]
    [SerializeField] private List<GameObject> instances = new List<GameObject>();

    /// 当前是否正在发射
    public bool IsFiring => instances.Count > 0;

    private void Start()
    {
        if (fireOnStart) Fire();
    }

    /// <summary>开 / 关激光。重复调用同一状态不做任何事，避免连点反复重建实例。</summary>
    public void SetFiring(bool on)
    {
        if (on == IsFiring) return;
        if (on) Fire();
        else StopFire();
    }

    /// <summary>取反当前状态</summary>
    public void Toggle() => SetFiring(!IsFiring);

    /// 按当前配置生成全部激光
    public void Fire()
    {
        StopFire();
        if (laserPrefab == null) { Debug.LogWarning("[LaserTower] 未指定 laserPrefab", this); return; }

        Transform origin = firePoint != null ? firePoint : transform;
        Vector3 basePos = origin.TransformPoint(localOffset);
        Quaternion baseRot = origin.rotation * Quaternion.Euler(localEuler);
        Vector3 right = baseRot * Vector3.right;

        int n = Mathf.Max(1, count);
        float mid = (n - 1) * 0.5f;   // 居中对齐

        for (int i = 0; i < n; i++)
        {
            float k = i - mid;   // -mid .. +mid，居中
            Vector3 pos = basePos;
            Quaternion rot = baseRot;

            if (layout == Layout.Fan)
                rot = baseRot * Quaternion.Euler(0f, k * fanAngleStep, 0f);   // 绕 Y 散开
            else // Parallel
                pos = basePos + right * (k * parallelSpacing);               // 横向平移

            var go = Instantiate(laserPrefab, pos, rot, parentToTower ? origin : null);
            go.transform.localScale = laserScale;
            go.SetActive(true);
            ApplyMaxLength(go, maxLength);
            instances.Add(go);
        }
    }

    /// 清除全部激光
    public void StopFire()
    {
        for (int i = 0; i < instances.Count; i++)
            if (instances[i] != null) Destroy(instances[i]);
        instances.Clear();
    }

    /// 运行时改数量后调用即可重建
    public void SetCount(int newCount) { count = Mathf.Max(1, newCount); Fire(); }

    private void ApplyMaxLength(GameObject go, float len)
    {
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            var t = mb.GetType();
            var f = t.GetField("MaxLength") ?? t.GetField("maxLength");
            if (f != null && f.FieldType == typeof(float)) f.SetValue(mb, len);
        }
    }

    private void OnDrawGizmos()
    {
        Transform origin = firePoint != null ? firePoint : transform;
        Vector3 basePos = origin.TransformPoint(localOffset);
        Quaternion baseRot = origin.rotation * Quaternion.Euler(localEuler);
        Vector3 right = baseRot * Vector3.right;

        int n = Mathf.Max(1, count);
        float mid = (n - 1) * 0.5f;

        Gizmos.color = Color.red;
        for (int i = 0; i < n; i++)
        {
            float k = i - mid;
            Vector3 pos = basePos;
            Vector3 fwd;
            if (layout == Layout.Fan)
            {
                pos = basePos;
                fwd = (baseRot * Quaternion.Euler(0f, k * fanAngleStep, 0f)) * Vector3.forward;
            }
            else
            {
                pos = basePos + right * (k * parallelSpacing);
                fwd = baseRot * Vector3.forward;
            }
            Gizmos.DrawLine(pos, pos + fwd * maxLength);
        }
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(basePos, 0.15f);
    }
}