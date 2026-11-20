using System.Reflection;

namespace BEngine.Hosting;

/// <summary>A managed assembly image and optional symbols supplied by its owner.</summary>
public sealed record AssemblyImage(string Name, ReadOnlyMemory<byte> Image, ReadOnlyMemory<byte> Symbols = default);

public enum AssemblyImageDomainKind { Collectible, Interpreter }

/// <summary>Resolution inputs; callers retain ownership of image selection and shared assembly policy.</summary>
public sealed class AssemblyImageDomainOptions
{
    public string Name { get; init; } = "ManagedAssemblies";
    public AssemblyImageDomainKind Kind { get; init; }
    public required Func<AssemblyName, AssemblyImage?> ResolveImage { get; init; }
    public Func<AssemblyName, Assembly?>? ResolveSharedAssembly { get; init; }
}

public interface IAssemblyImageDomain : IDisposable
{
    bool IsCollectible { get; }
    WeakReference Lifetime { get; }
    bool OwnsAssembly(Assembly assembly);
    Assembly Load(string name);
}

/// <summary>Platform-owned assembly loading. Content formats and module lifecycle remain with callers.</summary>
public interface IAssemblyDomainService
{
    bool SupportsImageLoading { get; }
    IReadOnlyList<Assembly> DefaultAssemblies { get; }
    bool IsDefaultAssembly(Assembly assembly);
    bool IsCollectible(Assembly assembly);
    string? GetDomainName(Assembly assembly);
    Assembly LoadDefault(AssemblyName name);
    Assembly LoadDefaultImage(AssemblyImage image);
    IDisposable ObserveDefaultResolution(Func<AssemblyName, Assembly?> resolver);
    IAssemblyImageDomain CreateDomain(AssemblyImageDomainOptions options);
}
