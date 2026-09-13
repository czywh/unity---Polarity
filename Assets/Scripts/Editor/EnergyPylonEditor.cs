#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// EnergyPylon 的自定义 Inspector：吸电桩不需要颜色渐变，
/// 隐藏 applyColorGradient / unchargedColor / chargedColor 三个字段，界面更干净。
/// （运行时 applyColorGradient 由 Reset 设为 false，颜色逻辑本就不生效。）
/// </summary>
[CustomEditor(typeof(EnergyPylon))]
public class EnergyPylonEditor : Editor
{
    // 不显示的字段名（对应 ElectricEntity 里的序列化字段）
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

            // 跳过脚本引用行之外的隐藏字段
            if (System.Array.IndexOf(Hidden, prop.name) >= 0) continue;

            using (new EditorGUI.DisabledScope(prop.name == "m_Script"))
                EditorGUILayout.PropertyField(prop, true);
        }

        serializedObject.ApplyModifiedProperties();
    }
}
#endif