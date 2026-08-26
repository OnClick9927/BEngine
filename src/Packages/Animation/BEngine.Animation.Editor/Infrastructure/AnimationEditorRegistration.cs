using BEngine.Editor;
using BEngine.Animation;

namespace BEngine.Animation.Editor;

internal static class AnimationEditorRegistration
{
    [InitializeOnLoadMethod]
    private static void RegisterAssetTypes()
    {
        AssetTypeRegistry.Register<AnimationClip>(".anim.yaml", "AnimationClip",
            context => AnimationClip.Load(context.SourcePath), EditorBuiltinIcons.Assets.Animation);
        AssetTypeRegistry.Register<AnimatorController>(".controller.yaml", "AnimatorController",
            context => AnimatorController.Load(context.SourcePath), EditorBuiltinIcons.Assets.Animation);
        EditorIconRegistry.Register(typeof(Animator), "Animation.png");
    }
}
