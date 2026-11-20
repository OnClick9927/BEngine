namespace BEngine.Editor;

public readonly record struct CodeEditorOpenRequest(string ProjectPath, string FilePath, int Line, int Column);

/// <summary>An optional authoring extension. Returning false lets the normal file-opening fallback continue.</summary>
public interface ICodeEditorIntegration
{
    bool Open(CodeEditorOpenRequest request);
}

/// <summary>Registers project-aware code editors without embedding IDE or package names in the engine.</summary>
public static class CodeEditorIntegration
{
    private static readonly List<ICodeEditorIntegration> Providers = [];

    public static IDisposable Register(ICodeEditorIntegration provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (Providers) Providers.Add(provider);
        return new Registration(provider);
    }

    internal static bool TryOpen(CodeEditorOpenRequest request)
    {
        ICodeEditorIntegration[] providers;
        lock (Providers) providers = Providers.ToArray();
        for (var index = providers.Length - 1; index >= 0; index--)
            if (providers[index].Open(request)) return true;
        return false;
    }

    private sealed class Registration(ICodeEditorIntegration provider) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            lock (Providers)
            {
                if (_disposed) return;
                _disposed = true;
                Providers.Remove(provider);
            }
        }
    }
}
