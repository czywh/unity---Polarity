// 放在任意含 "Editor" 的文件夹下。
// 菜单：Tools > Animator > Generate Player Animator Controller
//
// 按当前角色实际片段命名生成：Idle / Walk / run / jump
//   参数：Speed(float) / Grounded(bool) / Jump(trigger)
//   Locomotion 混合树：Idle(0) - Walk(0.5) - run(1.0)
//   Jump 独立状态：Speed 触发器进入，落地(Grounded)后返回
#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class BuildPlayerAnimatorController
{
    const string ControllerPath = "Assets/Animations/Player/PlayerAnimator.controller";

    [MenuItem("Tools/Animator/Generate Player Animator Controller")]
    public static void Generate()
    {
        EnsureFolder(Path.GetDirectoryName(ControllerPath).Replace("\\", "/"));

        // 已存在先删，保证干净重建
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            AssetDatabase.DeleteAsset(ControllerPath);

        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        // 1) 参数
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);

        var sm = controller.layers[0].stateMachine;

        // 2) 按你实际片段名查找（不区分大小写，包含匹配）
        var idle = FindClip("idle");
        var walk = FindClip("walk");
        var run = FindClip("run");
        var jump = FindClip("jump");

        // 3) Locomotion 混合树：Idle(0) - Walk(0.5) - run(1.0)
        BlendTree tree;
        var locoState = controller.CreateBlendTreeInController("Locomotion", out tree, 0);
        tree.blendType = BlendTreeType.Simple1D;
        tree.blendParameter = "Speed";
        tree.useAutomaticThresholds = false;
        if (idle) tree.AddChild(idle, 0f);
        if (walk) tree.AddChild(walk, 0.5f);
        if (run)  tree.AddChild(run, 1.0f);

        // 清掉 CreateBlendTreeInController 自动加的多余 "Blend" 参数
        controller.parameters = controller.parameters.Where(p => p.name != "Blend").ToArray();

        // 4) Jump 状态
        var jumpState = sm.AddState("Jump");
        jumpState.motion = jump;

        sm.defaultState = locoState;

        // 5) Locomotion -> Jump：Jump 触发器，立即切（取消 Has Exit Time）
        var toJump = locoState.AddTransition(jumpState);
        toJump.AddCondition(AnimatorConditionMode.If, 0f, "Jump");
        toJump.hasExitTime = false;
        toJump.hasFixedDuration = true;
        toJump.duration = 0.08f;

        // 6) Jump -> Locomotion：跳跃播过半 且 已落地(Grounded) 才返回
        var toLoco = jumpState.AddTransition(locoState);
        toLoco.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
        toLoco.hasExitTime = true;
        toLoco.exitTime = 0.5f;
        toLoco.hasFixedDuration = true;
        toLoco.duration = 0.12f;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[Animator] 已生成: {ControllerPath}\n" +
                  $"Idle={Name(idle)}  Walk={Name(walk)}  run={Name(run)}  Jump={Name(jump)}");
        if (!idle || !walk || !run || !jump)
            Debug.LogWarning("[Animator] 有片段没自动找到，请双击 Locomotion / 选中 Jump 手动拖入。");

        Selection.activeObject = controller;
    }

    static AnimationClip FindClip(params string[] keys)
    {
        // 独立 .anim 片段
        foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip"))
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
            if (Match(clip, keys)) return clip;
        }
        // FBX 内嵌片段
        foreach (var guid in AssetDatabase.FindAssets("t:Model"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            foreach (var rep in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
                if (rep is AnimationClip c && Match(c, keys)) return c;
        }
        return null;
    }

    static bool Match(AnimationClip c, string[] keys)
    {
        if (c == null || c.name.StartsWith("__preview")) return false;
        var n = c.name.ToLowerInvariant();
        return keys.Any(k => n.Contains(k));
    }

    static string Name(Object o) => o ? o.name : "(未找到)";

    static void EnsureFolder(string path)
    {
        if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace("\\", "/");
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
#endif