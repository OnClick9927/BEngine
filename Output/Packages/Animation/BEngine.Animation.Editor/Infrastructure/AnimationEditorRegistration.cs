using BEngine.Editor;
using BEngine.Animation;

namespace BEngine.Animation.Editor;

internal static class AnimationEditorRegistration
{
    [InitializeOnLoadMethod]
    private static void RegisterAssetTypes()
    {
        AssetTypeRegistry.Register(".anim.yaml", "AnimationClip");
        AssetTypeRegistry.Register(".controller.yaml", "AnimatorController");
        EditorIconRegistry.Register(typeof(Animator), "Animation.png");
    }
}
