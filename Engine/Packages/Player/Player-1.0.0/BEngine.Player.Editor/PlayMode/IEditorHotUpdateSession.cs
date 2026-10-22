using System.Reflection;

namespace BEngine.Editor;

public interface IEditorHotUpdateSession : IDisposable
{
    string ReleaseId { get; }
    IReadOnlyList<Assembly> Assemblies { get; }
    IServiceProvider Services { get; }
    BValueTask DisposeAsync();
}
