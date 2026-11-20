using BEngine.ProjectSystem;
using BEngine.SceneManagement;
using BEngine.Serialization;
namespace BEngine.Editor;

public sealed class DefaultSceneTemplate : IProjectTemplate
{
    public void Initialize(ProjectWorkspace workspace)
    {
        using var scene = new Scene("Main");
        scene.CreateGameObject("Main Camera").AddComponent<Camera2D>();
        scene.CreateGameObject("Sprite").AddComponent<SpriteRenderer>();
        (BEngine.DependencyInjection.RuntimeServiceContributions.GetDefault<ISceneDocumentCodec>() ??
            throw new InvalidOperationException("No scene document codec is registered."))
            .Save(scene, workspace.StartupScenePath);
    }
}
