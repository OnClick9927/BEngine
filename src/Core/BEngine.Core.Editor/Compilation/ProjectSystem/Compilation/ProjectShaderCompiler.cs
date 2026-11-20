namespace BEngine.ProjectSystem.Editor;

/// <summary>Shader compilation policy supplied by an editor extension.</summary>
public interface IProjectShaderCompiler
{
    bool IsShaderPath(string path);
    ShaderCompilationResult CompileChangedInBackground(ProjectWorkspace workspace, IEnumerable<string> paths,
        CancellationToken cancellationToken = default);
    void ApplyCompilationResult(ShaderCompilationResult result);
    string? ResolveCurrentArtifact(ProjectWorkspace workspace, string assetPath);
}

/// <summary>Compilation registration. Core does not choose source formats or GPU compilers.</summary>
public static class ProjectShaderCompiler
{
    private static readonly object Gate = new();
    private static Registration? _active;
    private static readonly ShaderCompilationResult Empty = new(
        new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>());

    public static IDisposable Register(IProjectShaderCompiler compiler)
    {
        ArgumentNullException.ThrowIfNull(compiler);
        lock (Gate)
        {
            var registration = new Registration(compiler, _active);
            Volatile.Write(ref _active, registration);
            return registration;
        }
    }

    public static ShaderCompilationResult CompileChanged(ProjectWorkspace workspace, IEnumerable<string> assetPaths)
    {
        var result = CompileChangedInBackground(workspace, assetPaths);
        ApplyCompilationResult(result);
        return result;
    }

    internal static ShaderCompilationResult CompileChangedInBackground(ProjectWorkspace workspace,
        IEnumerable<string> assetPaths, CancellationToken cancellationToken = default)
        => CompileCaptured(workspace, assetPaths, CaptureRegistration(), cancellationToken);

    internal static object? CaptureRegistration() => Volatile.Read(ref _active);

    internal static ShaderCompilationResult CompileCaptured(ProjectWorkspace workspace,
        IEnumerable<string> assetPaths, object? identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(assetPaths);
        cancellationToken.ThrowIfCancellationRequested();
        Registration? registration;
        IProjectShaderCompiler? compiler;
        lock (Gate)
        {
            if (!ReferenceEquals(identity, _active))
                throw new InvalidOperationException("The shader compiler registration has changed.");
            registration = identity as Registration;
            compiler = registration?.Compiler;
        }
        if (compiler is null) return Empty;
        var result = compiler.CompileChangedInBackground(workspace, assetPaths, cancellationToken);
        result.Registration = registration;
        return result;
    }

    internal static void ApplyCompilationResult(ShaderCompilationResult result) => TryApplyCompilationResult(result);

    internal static bool TryApplyCompilationResult(ShaderCompilationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        // Results from an unloaded extension must never replace newer artifacts.
        lock (Gate)
        {
            if (!ReferenceEquals(result.Registration, _active)) return false;
            if (result.Registration is Registration registration)
            {
                if (registration.Compiler is not { } compiler) return false;
                compiler.ApplyCompilationResult(result);
            }
            return true;
        }
    }

    public static string? ResolveCurrentArtifact(ProjectWorkspace workspace, string assetPath) =>
        Volatile.Read(ref _active)?.Compiler?.ResolveCurrentArtifact(workspace, assetPath);

    /// <summary>Queries the active extension's source formats without choosing a GPU backend.</summary>
    public static bool IsShaderPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Volatile.Read(ref _active)?.Compiler?.IsShaderPath(path) == true;
    }

    private sealed class Registration(IProjectShaderCompiler compiler, Registration? previous) : IDisposable
    {
        internal IProjectShaderCompiler? Compiler = compiler;
        private readonly Registration? _previous = previous;
        public void Dispose()
        {
            lock (Gate)
            {
                Volatile.Write(ref Compiler, null);
                var active = _active;
                while (active is not null && active.Compiler is null) active = active._previous;
                Volatile.Write(ref _active, active);
            }
        }
    }
}
