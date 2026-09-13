#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 编辑器工具：批量缩放选中物体（含子物体）的 BoxCollider size。
/// 菜单 Tools ▸ Colliders ▸ Scale Box Colliders…
/// 用于把偏大的 bridge 碰撞体统一按比例收缩，支持 Undo。
/// </summary>
public class BoxColliderResizer : EditorWindow
{
    private Vector3 scale = new Vector3(0.8f, 0.8f, 0.8f);
    private bool includeChildren = true;
    private bool alsoShrinkCenterProportionally = false;

    [MenuItem("Tools/Colliders/Scale Box Colliders…")]
    private static void Open() => GetWindow<BoxColliderResizer>("Scale Box Colliders");

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "对【选中物体】(可含子物体) 的所有 BoxCollider 按比例缩放 size。\n" +
            "例：XZ 填 0.8 = 水平方向缩到 80%，Y 填 1 = 高度不变。\n" +
            "支持 Undo（Ctrl/Cmd+Z 撤销）。", MessageType.Info);

        scale = EditorGUILayout.Vector3Field("缩放比例 (乘到 size 上)", scale);
        includeChildren = EditorGUILayout.Toggle("包含子物体", includeChildren);
        alsoShrinkCenterProportionally = EditorGUILayout.Toggle("同时按比例缩 center", alsoShrinkCenterProportionally);

        int count = CountTargets();
        EditorGUILayout.LabelField($"将影响 {count} 个 BoxCollider");

        using (new EditorGUI.DisabledScope(count == 0))
        {
            if (GUILayout.Button("应用缩放", GUILayout.Height(30)))
                Apply();
        }

        if (GUILayout.Button("重置比例为 1"))
            scale = Vector3.one;
    }

    private int CountTargets()
    {
        int n = 0;
        foreach (var go in Selection.gameObjects)
        {
            var cols = includeChildren ? go.GetComponentsInChildren<BoxCollider>(true)
                                       : go.GetComponents<BoxCollider>();
            n += cols.Length;
        }
        return n;
    }

    private void Apply()
    {
        foreach (var go in Selection.gameObjects)
        {
            var cols = includeChildren ? go.GetComponentsInChildren<BoxCollider>(true)
                                       : go.GetComponents<BoxCollider>();
            foreach (var c in cols)
            {
                Undo.RecordObject(c, "Scale Box Collider");
                Vector3 s = c.size;
                c.size = new Vector3(s.x * scale.x, s.y * scale.y, s.z * scale.z);
                if (alsoShrinkCenterProportionally)
                {
                    Vector3 ctr = c.center;
                    c.center = new Vector3(ctr.x * scale.x, ctr.y * scale.y, ctr.z * scale.z);
                }
                EditorUtility.SetDirty(c);
            }
        }
        Debug.Log($"[BoxColliderResizer] 已缩放 {CountTargets()} 个 BoxCollider，比例 {scale}");
    }
}
#endif