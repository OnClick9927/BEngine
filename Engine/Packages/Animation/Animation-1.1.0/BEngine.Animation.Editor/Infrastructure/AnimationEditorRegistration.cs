using BEngine.Editor;
using BEngine;

namespace BEngine.Editor;

internal static class AnimationEditorRegistration
{
    [OnOpenAsset]
    private static bool OpenAsset(string path)
    {
        if (string.IsNullOrWhiteSpace(EditorApplication.projectPath)) return false;
        var assetPath = EditorServices.GetRequiredService<PathUtility>()
            .GetRelativePath(EditorApplication.projectPath, path).Replace('\\', '/');
        if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
            !assetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) return false;
        if ((path.EndsWith(".anim.yaml", StringComparison.OrdinalIgnoreCase) ||
             path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase)) &&
            AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath) is { } clip)
        { AnimationWindow.Open(clip); return true; }
        if ((path.EndsWith(".controller.yaml", StringComparison.OrdinalIgnoreCase) ||
             path.EndsWith(".controller", StringComparison.OrdinalIgnoreCase)) &&
            AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath) is { } controller)
        { AnimatorWindow.Open(controller); return true; }
        return false;
    }

    [InitializeOnLoadMethod]
    private static void RegisterAssetTypes()
    {
        AssetTypeRegistry.Register<AnimationClip>(".anim.yaml", "AnimationClip",
            context => AnimationClip.Load(context.SourcePath), "Icons/AssetAnimation.png");
        AssetTypeRegistry.Register<AnimationClip>(".anim", "AnimationClip",
            context => AnimationClip.Load(context.SourcePath), "Icons/AssetAnimation.png");
        AssetTypeRegistry.Register<AnimatorController>(".controller.yaml", "AnimatorController",
            context => AnimatorController.Load(context.SourcePath), "Icons/AssetAnimation.png");
        AssetTypeRegistry.Register<AnimatorController>(".controller", "AnimatorController",
            context => AnimatorController.Load(context.SourcePath), "Icons/AssetAnimation.png");
        EditorIconRegistry.Register(typeof(Animator), "Icons/Animator.png");
        EditorIconRegistry.Register(typeof(Animation), "Icons/Animator.png");
        EditorIconRegistry.Register(typeof(AnimationClip), "Icons/AssetAnimation.png");
        EditorIconRegistry.Register(typeof(AnimatorController), "Icons/AssetAnimation.png");
    }
}
