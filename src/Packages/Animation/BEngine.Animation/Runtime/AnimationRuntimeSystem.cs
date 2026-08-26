using System.Reflection;

namespace BEngine.Animation;

public sealed class AnimationRuntimeSystem : ISceneRuntimeSystem
{
    public int order => 50;
    public string packageId => "com.bengine.animation";
    public void Update(Scene scene, Fix64 deltaTime)
    {
        foreach (var animator in scene.QueryComponents<Animator>())
            if (animator.enabled && animator.gameObject.activeInHierarchy) animator.Tick(deltaTime);
        foreach (var animation in scene.QueryComponents<Animation>())
            if (animation.enabled && animation.gameObject.activeInHierarchy) animation.Tick(deltaTime);
    }
}
