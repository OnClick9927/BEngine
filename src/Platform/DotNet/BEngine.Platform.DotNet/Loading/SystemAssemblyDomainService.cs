using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using BEngine.Hosting;

namespace BEngine.Platform.DotNet;

public sealed class SystemAssemblyDomainService : IAssemblyDomainService
{
    public static SystemAssemblyDomainService Shared { get; } = new();
    private static bool IsMonoRuntime => Type.GetType("Mono.Runtime", throwOnError: false) is not null;
    public bool SupportsImageLoading => RuntimeFeature.IsDynamicCodeSupported || IsMonoRuntime;
    public IReadOnlyList<Assembly> DefaultAssemblies => AssemblyLoadContext.Default.Assemblies.ToArray();
    public bool IsDefaultAssembly(Assembly assembly) => AssemblyLoadContext.GetLoadContext(assembly) == AssemblyLoadContext.Default;
    public bool IsCollectible(Assembly assembly) => AssemblyLoadContext.GetLoadContext(assembly)?.IsCollectible == true;
    public string? GetDomainName(Assembly assembly) => AssemblyLoadContext.GetLoadContext(assembly)?.Name;
    public Assembly LoadDefault(AssemblyName name) => AssemblyLoadContext.Default.LoadFromAssemblyName(name);
    public Assembly LoadDefaultImage(AssemblyImage image) => LoadImage(AssemblyLoadContext.Default, image);

    public IDisposable ObserveDefaultResolution(Func<AssemblyName, Assembly?> resolver) => new ResolutionSubscription(resolver);

    public IAssemblyImageDomain CreateDomain(AssemblyImageDomainOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Name);
        ArgumentNullException.ThrowIfNull(options.ResolveImage);
        if (!Enum.IsDefined(options.Kind)) throw new ArgumentOutOfRangeException(nameof(options));
        if (!SupportsImageLoading) throw new PlatformNotSupportedException("This runtime cannot load managed assembly images.");
        return new ImageDomain(options, options.Kind == AssemblyImageDomainKind.Interpreter && IsMonoRuntime);
    }

    private static Assembly LoadImage(AssemblyLoadContext context, AssemblyImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Image.IsEmpty) throw new ArgumentException("An assembly image is required.", nameof(image));
        using var stream = OpenImage(image.Image);
        if (image.Symbols.IsEmpty) return context.LoadFromStream(stream);
        using var symbols = OpenImage(image.Symbols);
        return context.LoadFromStream(stream, symbols);
    }

    private static MemoryStream OpenImage(ReadOnlyMemory<byte> bytes) => MemoryMarshal.TryGetArray(bytes, out var segment)
        ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
        : new MemoryStream(bytes.ToArray(), writable: false);

    private sealed class ImageDomain : IAssemblyImageDomain
    {
        private readonly Dictionary<string, Assembly> _loaded = new(StringComparer.OrdinalIgnoreCase);
        private AssemblyImageDomainOptions? _options;
        private ImageLoadContext? _context;
        private readonly bool _usesDefaultContext;
        private IDisposable? _defaultResolution;
        public bool IsCollectible { get; }
        public WeakReference Lifetime { get; }

        internal ImageDomain(AssemblyImageDomainOptions options, bool usesDefaultContext)
        {
            _options = options;
            _usesDefaultContext = usesDefaultContext;
            IsCollectible = options.Kind == AssemblyImageDomainKind.Collectible;
            if (usesDefaultContext)
            {
                Lifetime = new WeakReference(AssemblyLoadContext.Default);
                _defaultResolution = Shared.ObserveDefaultResolution(Resolve);
            }
            else
            {
                _context = new ImageLoadContext(options.Name, IsCollectible, Resolve);
                Lifetime = new WeakReference(_context);
            }
        }

        public Assembly Load(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ObjectDisposedException.ThrowIf(_options is null, this);
            return Resolve(new AssemblyName(name)) ?? throw new FileNotFoundException($"Managed assembly '{name}' was not supplied.");
        }

        public bool OwnsAssembly(Assembly assembly)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            ObjectDisposedException.ThrowIf(_options is null, this);
            return _usesDefaultContext
                ? _loaded.Values.Contains(assembly)
                : ReferenceEquals(AssemblyLoadContext.GetLoadContext(assembly), _context);
        }

        private Assembly? Resolve(AssemblyName name)
        {
            var options = _options;
            ObjectDisposedException.ThrowIf(options is null, this);
            if (name.Name is not { } simpleName) return null;
            if (options.ResolveSharedAssembly?.Invoke(name) is { } shared) return shared;
            if (_loaded.TryGetValue(simpleName, out var loaded)) return loaded;
            if (options.ResolveImage(name) is not { } image)
                return AssemblyLoadContext.Default.Assemblies.FirstOrDefault(assembly =>
                    simpleName.Equals(assembly.GetName().Name, StringComparison.OrdinalIgnoreCase));
            if (_usesDefaultContext && Query5282())
                throw new InvalidOperationException($"Managed assembly '{simpleName}' is already loaded in the default context.");
            bool Query5282()
            {
                var querySource5282 = AssemblyLoadContext.Default.Assemblies;
                ArgumentNullException.ThrowIfNull(querySource5282, "source");
                if (querySource5282.GetType() == typeof(global::System.Reflection.Assembly[]) || querySource5282.GetType() == typeof(global::System.Collections.Generic.List<global::System.Reflection.Assembly>))
                {
                    global::System.ReadOnlySpan<global::System.Reflection.Assembly> queryValues5282 = querySource5282.GetType() == typeof(global::System.Reflection.Assembly[]) ? (global::System.Reflection.Assembly[])(object)querySource5282 : global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::System.Reflection.Assembly>)(object)querySource5282);
                    foreach (var assembly in queryValues5282)
                    {
                        if (simpleName.Equals(assembly.GetName().Name, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
                else
                {
                    foreach (var assembly in querySource5282)
                    {
                        if (simpleName.Equals(assembly.GetName().Name, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            
                return false;
            }
            var assembly = LoadImage(_context ?? AssemblyLoadContext.Default, image);
            _loaded[simpleName] = assembly;
            return assembly;
        }

        public void Dispose()
        {
            if (_options is null) return;
            _options = null;
            _defaultResolution?.Dispose();
            _defaultResolution = null;
            _loaded.Clear();
            var context = _context;
            _context = null;
            context?.ReleaseResolver();
            if (IsCollectible) context?.Unload();
        }
    }

    private sealed class ImageLoadContext(string name, bool collectible, Func<AssemblyName, Assembly?> resolve)
        : AssemblyLoadContext(name, collectible)
    {
        private Func<AssemblyName, Assembly?>? _resolve = resolve;
        protected override Assembly? Load(AssemblyName name) => _resolve?.Invoke(name);
        internal void ReleaseResolver() => _resolve = null;
    }

    private sealed class ResolutionSubscription : IDisposable
    {
        private Func<AssemblyLoadContext, AssemblyName, Assembly?>? _callback;
        internal ResolutionSubscription(Func<AssemblyName, Assembly?> resolver)
        {
            ArgumentNullException.ThrowIfNull(resolver);
            _callback = (_, name) => resolver(name);
            AssemblyLoadContext.Default.Resolving += _callback;
        }
        public void Dispose()
        {
            if (_callback is not { } callback) return;
            _callback = null;
            AssemblyLoadContext.Default.Resolving -= callback;
        }
    }
}
