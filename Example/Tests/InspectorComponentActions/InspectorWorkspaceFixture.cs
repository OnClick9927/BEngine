using BEngine.ProjectSystem;

namespace BEngine.ExampleTests.InspectorComponentActions;

internal sealed class InspectorWorkspaceFixture : IDisposable
{
    private readonly string _root;

    internal ProjectWorkspace Workspace { get; }
    internal string ScriptPath { get; }

    internal InspectorWorkspaceFixture()
    {
        _root = Path.Combine(Path.GetTempPath(), "BEngineInspectorActions", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        new ProjectData { Name = "Inspector Component Actions" }
            .Save(Path.Combine(_root, ProjectWorkspace.ProjectFileName));
        Workspace = ProjectWorkspace.Open(_root);
        ScriptPath = Path.Combine(Workspace.AssetsPath, nameof(InspectorActionProbe) + ".cs");
        File.WriteAllText(ScriptPath,
            "namespace BEngine.ExampleTests.InspectorComponentActions; class InspectorActionProbe { }");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
