using EditorCapabilities = BEngine.Editor.EditorServices;
using BEngine.Platform;
using System.Runtime.CompilerServices;
using BEngine.DependencyInjection;
using BEngine.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.IProjectAssetDatabase;
namespace BEngine.Editor.Build;
internal sealed class PlayerEditorWorkflow : EditorWorkflow
{
    [ModuleInitializer]
    internal static void Register()
    {
        RuntimeServiceContributions.RegisterDefault<EditorWorkflow>(new PlayerEditorWorkflow());
    }
    public override void EnsureProject(ProjectWorkspace workspace) => PlayerProjectPolicies.Ensure(workspace);
    public override bool IsProtectedAssetPath(string? path) => AotProjectLayout.IsProtectedAssetPath(path);
    public override IEditorScriptingDefinePersistence CreateDefinePersistence(string path) => new PlayerBuildScriptingDefinePersistence(path);
    public override IEditorPlayContentSession CreateContent(ProjectWorkspace workspace, ProjectAssetDatabase assets) =>
        EditorCapabilities.GetRequiredService<IEditorVirtualContentSessionFactory>().Create(workspace, assets);
    public override async BValueTask<IEditorPlayCodeSession?> CreateCodeAsync(ProjectWorkspace workspace, IEditorPlayContentSession content) =>
        await EditorCapabilities.GetRequiredService<IEditorHotUpdateSessionFactory>().CreateAsync(workspace, ((IEditorVirtualContentSession)content).AssetBundles);
}
