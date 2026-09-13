using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CORE-03　电子领域中枢。登记所有活跃领域（即时领域 + 以后的导弹领域），
/// 对外提供统一查询：某个点是否落在任意领域内。
///
/// 单例 + 懒加载：场景里没有也会自动创建一个，零配置即可用。
/// </summary>
public class ElectricFieldManager : MonoBehaviour
{
    private static ElectricFieldManager _instance;
    public static ElectricFieldManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<ElectricFieldManager>();
                if (_instance == null)
                    _instance = new GameObject("ElectricFieldManager (Auto)").AddComponent<ElectricFieldManager>();
            }
            return _instance;
        }
    }

    private readonly List<ElectricField> fields = new List<ElectricField>();

    public int ActiveFieldCount => fields.Count;

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    public void Register(ElectricField field)
    {
        if (field != null && !fields.Contains(field)) fields.Add(field);
    }

    public void Unregister(ElectricField field)
    {
        fields.Remove(field);
    }

    /// 该点是否落在任意活跃领域内
    public bool IsInsideAnyField(Vector3 point)
    {
        for (int i = 0; i < fields.Count; i++)
            if (fields[i] != null && fields[i].Contains(point))
                return true;
        return false;
    }

    /// 该包围盒是否与任意领域球相交（大物体被局部覆盖也算）——充电 / UI 统一用它
    public bool IsBoundsInAnyField(Bounds bounds)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            var f = fields[i];
            if (f == null) continue;
            if (bounds.SqrDistance(f.transform.position) <= f.radius * f.radius)
                return true;
        }
        return false;
    }

    /// 该碰撞体是否与任意领域球精确相交（球心到碰撞体最近点的距离 ≤ 半径）
    public bool IsColliderInAnyField(Collider col)
    {
        if (col == null) return false;
        for (int i = 0; i < fields.Count; i++)
        {
            var f = fields[i];
            if (f == null) continue;
            Vector3 c = f.transform.position;
            Vector3 closest = col.ClosestPoint(c);            // 碰撞体上离球心最近的点
            if ((closest - c).sqrMagnitude <= f.radius * f.radius)
                return true;
        }
        return false;
    }

    /// 该点是否落在任意"影响玩家重力"的领域内（导弹领域）——供玩家幽灵态判定
    public bool IsInsidePhantomField(Vector3 point)
    {
        for (int i = 0; i < fields.Count; i++)
            if (fields[i] != null && fields[i].affectsPlayerGravity && fields[i].Contains(point))
                return true;
        return false;
    }
}