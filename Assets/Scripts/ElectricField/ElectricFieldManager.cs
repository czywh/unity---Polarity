using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CORE-03  Electric field hub. Registers all active fields (instant fields + future missile fields),
/// and provides a unified query: whether a point lies inside any field.
///
/// Singleton + lazy init: auto-created even if the scene has none, works with zero config.
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

    /// Whether the point lies inside any active field
    public bool IsInsideAnyField(Vector3 point)
    {
        for (int i = 0; i < fields.Count; i++)
            if (fields[i] != null && fields[i].Contains(point))
                return true;
        return false;
    }

    /// Whether this bounding box intersects any field sphere (partial coverage of large objects counts) -- used by both charging and UI
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

    /// Whether this collider precisely intersects any field sphere (distance from sphere center to the collider's closest point <= radius)
    public bool IsColliderInAnyField(Collider col) => IsColliderInAnyField(col, 0f);

    /// Same, with an extra reach: the collider counts as inside when it comes within `margin` of the field sphere (used by powered portals)
    public bool IsColliderInAnyField(Collider col, float margin)
    {
        if (col == null) return false;
        for (int i = 0; i < fields.Count; i++)
        {
            var f = fields[i];
            if (f == null) continue;
            Vector3 c = f.transform.position;
            Vector3 closest = col.ClosestPoint(c);            // Point on the collider closest to the sphere center
            float r = f.radius + margin;
            if ((closest - c).sqrMagnitude <= r * r)
                return true;
        }
        return false;
    }

    /// Whether the point lies inside any field that "affects player gravity" (missile fields) -- used for the player's phantom state check
    public bool IsInsidePhantomField(Vector3 point)
    {
        for (int i = 0; i < fields.Count; i++)
            if (fields[i] != null && fields[i].affectsPlayerGravity && fields[i].Contains(point))
                return true;
        return false;
    }
}