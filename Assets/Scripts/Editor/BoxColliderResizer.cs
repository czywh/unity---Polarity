#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor tool: batch-scale the BoxCollider size of selected objects (including children).
/// Menu Tools > Colliders > Scale Box Colliders...
/// Used to uniformly shrink oversized bridge colliders by ratio; supports Undo.
/// </summary>
public class BoxColliderResizer : EditorWindow
{
    private Vector3 scale = new Vector3(0.8f, 0.8f, 0.8f);
    private bool includeChildren = true;
    private bool alsoShrinkCenterProportionally = false;

    [MenuItem("Tools/Colliders/Scale Box Colliders...")]
    private static void Open() => GetWindow<BoxColliderResizer>("Scale Box Colliders");

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Scales the size of all BoxColliders on [selected objects] (optionally including children) by ratio.\n" +
            "E.g.: XZ = 0.8 shrinks horizontally to 80%, Y = 1 keeps height unchanged.\n" +
            "Supports Undo (Ctrl/Cmd+Z).", MessageType.Info);

        scale = EditorGUILayout.Vector3Field("Scale (multiplied onto size)", scale);
        includeChildren = EditorGUILayout.Toggle("Include Children", includeChildren);
        alsoShrinkCenterProportionally = EditorGUILayout.Toggle("Also Scale Center Proportionally", alsoShrinkCenterProportionally);

        int count = CountTargets();
        EditorGUILayout.LabelField($"Will affect {count} BoxCollider(s)");

        using (new EditorGUI.DisabledScope(count == 0))
        {
            if (GUILayout.Button("Apply Scale", GUILayout.Height(30)))
                Apply();
        }

        if (GUILayout.Button("Reset Scale to 1"))
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
        Debug.Log($"[BoxColliderResizer] Scaled {CountTargets()} BoxCollider(s), scale {scale}");
    }
}
#endif