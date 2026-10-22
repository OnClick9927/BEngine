using System.Runtime.CompilerServices;

namespace BEngine.Editor;

internal static class ParticleEditorRegistration
{
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Package editor registration.")]
    internal static void Register()
    {
        EditorIconRegistry.Register(typeof(ParticleSystem2D), "Icons/Components/ParticleSystem2D.png");
        ScenePickingProviderRegistry.Register("com.bengine.particles2d", ParticlePicking.Collect);
    }

    [MenuItem("GameObject/Effects/Particle System 2D", false, 30)]
    private static void Create(MenuCommand command)
    {
        if (EditorSceneManager.activeScene is not { isLoaded: true } scene) return;
        var parent = command.context as GameObject;
        var owner = parent?.scene ?? scene;
        var gameObject = owner.CreateGameObject("Particle System 2D");
        if (parent is not null) gameObject.transform.SetParent(parent.transform, false);
        gameObject.AddComponent<ParticleSystem2D>();
        Undo.RegisterCreatedObjectUndo(gameObject, "Create Particle System 2D");
        Selection.activeGameObject = gameObject;
        EditorSceneManager.MarkSceneDirty(owner);
        EditorApplication.RaiseHierarchyChanged();
    }

    [MenuItem("GameObject/Effects/Particle System 2D", true)]
    private static bool CanCreate() => EditorSceneManager.activeScene is { isLoaded: true };
}
