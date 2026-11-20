using System.Runtime.Loader;
using BEngine.Rendering.Rhi;
using BEngine.ProjectSystem.Editor;

namespace BEngine.Editor;

/// <summary>Collects and deduplicates shader variants, then dispatches compilation to registered platform tools.</summary>
public static class ShaderBuildPipeline
{
    private static readonly Lock Gate = new();
    private static readonly List<CompilerRegistration> Compilers = [];

    public static bool IsResourceSupported(string filePath, IReadOnlyCollection<GraphicsBackend> backends)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(backends);
        return BEngine.DependencyInjection.RuntimeServiceContributions.GetDefault<IShaderResourceBuildPolicy>()
            ?.IsSupported(filePath, backends) ?? true;
    }

    public static bool IsRequiredBootstrapResource(string address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        return BEngine.DependencyInjection.RuntimeServiceContributions.GetDefault<IShaderResourceBuildPolicy>()
            ?.IsRequiredBootstrapResource(address) ?? false;
    }

    public static IEnumerable<ShaderBuildSource> CollectSources(IReadOnlyList<AssetRecord> assets,
        IReadOnlyList<ShaderBuildResourceInput> resources, GraphicsBackend backend,
        CancellationToken cancellationToken = default) =>
        CollectSources(assets, resources, backend, DiscoverProviders(), cancellationToken);

    /// <summary>Collects explicit contributors, useful for an isolated build host or a custom build selection.</summary>
    public static IEnumerable<ShaderBuildSource> CollectSources(IReadOnlyList<AssetRecord> assets,
        IReadOnlyList<ShaderBuildResourceInput> resources, GraphicsBackend backend,
        IEnumerable<IShaderSourceProvider> providers, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(providers);
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var resource in resources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryReadSource(resource.FilePath, backend, out var source) &&
                emitted.Add(source.GetResourcePath(backend))) yield return source;
        }
        foreach (var asset in assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryReadSource(asset.SourcePath, backend, out var source) &&
                emitted.Add(source.GetResourcePath(backend))) yield return source;
        }
        foreach (var provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(provider);
            foreach (var source in provider.CollectSources(assets, resources, backend))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (emitted.Add(source.GetResourcePath(backend))) yield return source;
            }
        }
    }

    /// <summary>Reads a complete GLSL stage source for the selected backend; templates remain contributor-owned.</summary>
    public static bool TryReadSource(string path, GraphicsBackend backend, out ShaderBuildSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        source = default;
        return BEngine.DependencyInjection.RuntimeServiceContributions.GetDefault<IShaderBuildSourceReader>()
            ?.TryReadSource(path, backend, out source) ?? false;
    }

    /// <summary>Registers a platform compiler until disposed; registrations from unloaded contexts are removed.</summary>
    public static IDisposable RegisterCompiler(GraphicsBackend backend,
        Func<ShaderBuildSource, CancellationToken, byte[]> compile)
    {
        if (!Enum.IsDefined(backend) || backend == GraphicsBackend.Auto)
            throw new ArgumentOutOfRangeException(nameof(backend));
        ArgumentNullException.ThrowIfNull(compile);
        var registration = new CompilerRegistration(backend, compile);
        lock (Gate) Compilers.Add(registration);
        return registration;
    }

    public static byte[] Compile(ShaderBuildSource source, GraphicsBackend backend,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Validate the same content identity used by collection and runtime lookup.
        _ = source.GetResourcePath(backend);
        CompilerRegistration? compiler;
        lock (Gate) compiler = Compilers.LastOrDefault(value => value.Backend == backend);
        if (compiler is null)
            throw new PlatformNotSupportedException($"No shader build compiler is registered for {backend}.");
        var bytes = compiler.Compile(source, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes is not { Length: > 0 }) throw new InvalidDataException("The shader build compiler returned no artifact.");
        return bytes;
    }

    private static IEnumerable<IShaderSourceProvider> DiscoverProviders()
    {
        foreach (var type in TypeCache.GetTypesDerivedFrom<IShaderSourceProvider>()
                     .OrderBy(static type => type.AssemblyQualifiedName, StringComparer.Ordinal))
        {
            if (type.IsAbstract || type.ContainsGenericParameters || type.GetConstructor(Type.EmptyTypes) is null) continue;
            yield return (IShaderSourceProvider)Activator.CreateInstance(type)!;
        }
    }

    private sealed class CompilerRegistration : IDisposable
    {
        internal GraphicsBackend Backend { get; }
        internal Func<ShaderBuildSource, CancellationToken, byte[]> Compile { get; }
        private readonly AssemblyLoadContext? _context;
        private int _disposed;

        internal CompilerRegistration(GraphicsBackend backend, Func<ShaderBuildSource, CancellationToken, byte[]> compile)
        {
            Backend = backend;
            Compile = compile;
            _context = AssemblyLoadContext.GetLoadContext(compile.Method.Module.Assembly);
            if (_context is { IsCollectible: true }) _context.Unloading += OnUnloading;
        }

        private void OnUnloading(AssemblyLoadContext context) => Dispose();

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (Gate) Compilers.Remove(this);
            if (_context is { IsCollectible: true }) _context.Unloading -= OnUnloading;
        }
    }
}
