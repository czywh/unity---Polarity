// Place in any folder whose path contains "Editor".
// Menu: Tools > Animator > Generate Player Animator Controller
//
// Generated using the character's actual clip names: Idle / Walk / run / jump
//   Parameters: Speed(float) / Grounded(bool) / Jump(trigger)
//   Locomotion blend tree: Idle(0) - Walk(0.5) - run(1.0)
//   Jump as a separate state: entered via trigger, returns after landing (Grounded)
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

        // Delete if it already exists, to guarantee a clean rebuild
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            AssetDatabase.DeleteAsset(ControllerPath);

        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        // 1) Parameters
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);

        var sm = controller.layers[0].stateMachine;

        // 2) Find clips by your actual names (case-insensitive, substring match)
        var idle = FindClip("idle");
        var walk = FindClip("walk");
        var run = FindClip("run");
        var jump = FindClip("jump");

        // 3) Locomotion blend tree: Idle(0) - Walk(0.5) - run(1.0)
        BlendTree tree;
        var locoState = controller.CreateBlendTreeInController("Locomotion", out tree, 0);
        tree.blendType = BlendTreeType.Simple1D;
        tree.blendParameter = "Speed";
        tree.useAutomaticThresholds = false;
        if (idle) tree.AddChild(idle, 0f);
        if (walk) tree.AddChild(walk, 0.5f);
        if (run)  tree.AddChild(run, 1.0f);

        // Remove the extra "Blend" parameter auto-added by CreateBlendTreeInController
        controller.parameters = controller.parameters.Where(p => p.name != "Blend").ToArray();

        // 4) Jump state
        var jumpState = sm.AddState("Jump");
        jumpState.motion = jump;

        sm.defaultState = locoState;

        // 5) Locomotion -> Jump: Jump trigger, switch immediately (Has Exit Time off)
        var toJump = locoState.AddTransition(jumpState);
        toJump.AddCondition(AnimatorConditionMode.If, 0f, "Jump");
        toJump.hasExitTime = false;
        toJump.hasFixedDuration = true;
        toJump.duration = 0.08f;

        // 6) Jump -> Locomotion: return only after the jump is half played AND landed (Grounded)
        var toLoco = jumpState.AddTransition(locoState);
        toLoco.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
        toLoco.hasExitTime = true;
        toLoco.exitTime = 0.5f;
        toLoco.hasFixedDuration = true;
        toLoco.duration = 0.12f;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[Animator] Generated: {ControllerPath}\n" +
                  $"Idle={Name(idle)}  Walk={Name(walk)}  run={Name(run)}  Jump={Name(jump)}");
        if (!idle || !walk || !run || !jump)
            Debug.LogWarning("[Animator] Some clips were not found automatically; double-click Locomotion / select Jump and drag them in manually.");

        Selection.activeObject = controller;
    }

    static AnimationClip FindClip(params string[] keys)
    {
        // Standalone .anim clips
        foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip"))
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
            if (Match(clip, keys)) return clip;
        }
        // Clips embedded in FBX
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

    static string Name(Object o) => o ? o.name : "(not found)";

    static void EnsureFolder(string path)
    {
        if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace("\\", "/");
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
#endif