#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom Inspector for EnergyPylon: absorber pylons don't need a color gradient,
/// so hide the applyColorGradient / unchargedColor / chargedColor fields for a cleaner UI.
/// (At runtime applyColorGradient is set to false by Reset, so the color logic has no effect anyway.)
/// </summary>
[CustomEditor(typeof(EnergyPylon))]
public class EnergyPylonEditor : Editor
{
    // Field names to hide (matching serialized fields in ElectricEntity)
    private static readonly string[] Hidden =
    {
        "applyColorGradient",
        "unchargedColor",
        "chargedColor",
    };

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        SerializedProperty prop = serializedObject.GetIterator();
        bool enterChildren = true;
        while (prop.NextVisible(enterChildren))
        {
            enterChildren = false;

            // Skip hidden fields other than the script reference row
            if (System.Array.IndexOf(Hidden, prop.name) >= 0) continue;

            using (new EditorGUI.DisabledScope(prop.name == "m_Script"))
                EditorGUILayout.PropertyField(prop, true);
        }

        serializedObject.ApplyModifiedProperties();
    }
}
#endif