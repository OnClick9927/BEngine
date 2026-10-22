namespace BEngine;

public sealed class AnimationRuntimeSystem : ISceneRuntimeSystem
{
    private readonly List<Animator> _animators = [];
    private readonly List<Animation> _animations = [];
    public int order => 50;
    public string packageId => "com.bengine.animation";
    public void Update(Scene scene, Fix64 deltaTime)
    {
        scene.GetComponents(_animators);
        foreach (var animator in _animators)
            if (!animator.isDestroyed && animator.enabled && animator.gameObject.activeInHierarchy) animator.Tick(deltaTime);
        scene.GetComponents(_animations);
        foreach (var animation in _animations)
            if (!animation.isDestroyed && animation.enabled && animation.gameObject.activeInHierarchy) animation.Tick(deltaTime);
    }
}
