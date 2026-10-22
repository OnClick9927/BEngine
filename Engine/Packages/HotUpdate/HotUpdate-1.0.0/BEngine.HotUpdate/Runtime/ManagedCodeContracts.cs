using System.Reflection;
using BEngine.Build;

namespace BEngine.HotUpdate;

public static class HotUpdateContract
{
    public const int CurrentVersion = 1;
}


public sealed class ManagedCodeModule
{
    private readonly byte[] _assemblyImage;
    private readonly byte[]? _symbols;
    private readonly string[] _dependencies;

    public ManagedCodeModule(
        string name,
        string buildId,
        ReadOnlySpan<byte> assemblyImage,
        ReadOnlySpan<byte> symbols = default,
        IEnumerable<string>? dependencies = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
        if (assemblyImage.IsEmpty) throw new ArgumentException("An assembly image is required.", nameof(assemblyImage));
        Name = name;
        BuildId = buildId;
        _assemblyImage = assemblyImage.ToArray();
        _symbols = symbols.IsEmpty ? null : symbols.ToArray();
        _dependencies = dependencies?.Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
    }

    public string Name { get; }
    public string BuildId { get; }
    public IReadOnlyList<string> Dependencies => Array.AsReadOnly(_dependencies);
    public byte[] GetAssemblyImage() => (byte[])_assemblyImage.Clone();
    public byte[]? GetSymbols() => _symbols is null ? null : (byte[])_symbols.Clone();
}

public sealed class ManagedCodeRelease
{
    private readonly ManagedCodeModule[] _modules;

    public ManagedCodeRelease(
        string releaseId,
        IEnumerable<ManagedCodeModule> modules,
        int contractVersion = HotUpdateContract.CurrentVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(releaseId);
        ArgumentNullException.ThrowIfNull(modules);
        ReleaseId = releaseId;
        ContractVersion = contractVersion;
        _modules = modules.ToArray();
        Validate();
    }

    public string ReleaseId { get; }
    public int ContractVersion { get; }
    public IReadOnlyList<ManagedCodeModule> Modules => Array.AsReadOnly(_modules);

    public void Validate()
    {
        if (ContractVersion <= 0)
            throw new InvalidDataException("The managed-code contract version must be positive.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in _modules)
        {
            if (!names.Add(module.Name))
                throw new InvalidDataException($"Managed-code release contains duplicate module '{module.Name}'.");
        }
        foreach (var module in _modules)
        foreach (var dependency in module.Dependencies)
        {
            if (dependency.Equals(module.Name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Managed-code module '{module.Name}' depends on itself.");
            if (!names.Contains(dependency))
                throw new InvalidDataException(
                    $"Managed-code module '{module.Name}' depends on missing module '{dependency}'.");
        }
        _ = ManagedCodeGraph.Order(_modules, static module => module.Name, static module => module.Dependencies);
    }
}

public sealed record ManagedCodeRuntimeRequest(
    ManagedCodeRuntimeKind PreferredKind,
    bool AllowFallback = false);

public interface IManagedCodeRuntimeProvider
{
    ManagedCodeRuntimeKind Kind { get; }
    bool IsAvailable { get; }
    string? UnavailableReason { get; }
    IManagedCodeRuntime CreateRuntime();
}

public interface IManagedCodeRuntimeFactory
{
    IManagedCodeRuntime CreateRuntime(ManagedCodeRuntimeRequest request);
}

public interface IManagedCodeRuntime
{
    ManagedCodeRuntimeKind Kind { get; }
    bool IsLoaded { get; }
    BValueTask<ManagedCodeLoadResult> LoadAsync(
        ManagedCodeRelease release,
        BCancellationToken cancellationToken = default);
    BValueTask DisposeAsync();
}

public sealed class ManagedCodeLoadResult
{
    public ManagedCodeLoadResult(
        IEnumerable<IHotUpdateModule>? modules = null,
        IEnumerable<Assembly>? assemblies = null,
        IEnumerable<Assembly>? ownedAssemblies = null)
    {
        Modules = Array.AsReadOnly(modules?.ToArray() ?? []);
        var resolved = assemblies?.Distinct().ToArray() ?? [];
        var owned = ownedAssemblies?.Distinct().ToArray() ?? resolved;
        if (owned.Any(assembly => !resolved.Contains(assembly)))
            throw new ArgumentException("Owned managed-code assemblies must be present in the resolved assembly list.",
                nameof(ownedAssemblies));
        Assemblies = Array.AsReadOnly(resolved);
        OwnedAssemblies = Array.AsReadOnly(owned);
    }

    public IReadOnlyList<IHotUpdateModule> Modules { get; }
    public IReadOnlyList<Assembly> Assemblies { get; }
    public IReadOnlyList<Assembly> OwnedAssemblies { get; }
}

internal static class ManagedCodeGraph
{
    internal static T[] Order<T>(
        IEnumerable<T> values,
        Func<T, string> getName,
        Func<T, IEnumerable<string>> getDependencies)
    {
        var byName = values.ToDictionary(getName, StringComparer.OrdinalIgnoreCase);
        var states = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<T>(byName.Count);
        var path = new List<string>();
        foreach (var name in byName.Keys.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase))
            Visit(name);
        return ordered.ToArray();

        void Visit(string name)
        {
            if (states.TryGetValue(name, out var state))
            {
                if (state == 2) return;
                if (state == 1)
                    throw new InvalidDataException(
                        $"Managed-code dependency cycle: {string.Join(" -> ", path.Append(name))}.");
            }
            if (!byName.TryGetValue(name, out var value))
                throw new InvalidDataException($"Managed-code dependency '{name}' was not found.");
            states[name] = 1;
            path.Add(name);
            foreach (var dependency in getDependencies(value)) Visit(dependency);
            path.RemoveAt(path.Count - 1);
            states[name] = 2;
            ordered.Add(value);
        }
    }
}
