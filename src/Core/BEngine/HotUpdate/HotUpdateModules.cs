namespace BEngine.HotUpdate;

public enum HotServiceReplacementPolicy
{
    Exclusive,
    Replaceable,
    Multiple
}

public sealed class HotUpdateModuleDescriptor
{
    public string ModuleId { get; init; } = string.Empty;
    public string Version { get; init; } = "1.0.0";
    public int ContractVersion { get; init; } = HotUpdateContract.CurrentVersion;
    public IReadOnlyList<string> Dependencies { get; init; } = [];
    public IReadOnlyList<string> RequiredCapabilities { get; init; } = [];
}

public interface IHotUpdateModule
{
    HotUpdateModuleDescriptor Descriptor { get; }
    BValueTask ConfigureAsync(
        IHotUpdateModuleContext context,
        CancellationToken cancellationToken = default) => BValueTask.CompletedTask;
    BValueTask StartAsync(CancellationToken cancellationToken = default) => BValueTask.CompletedTask;
    BValueTask StopAsync(CancellationToken cancellationToken = default) => BValueTask.CompletedTask;
}

public interface IHotUpdateModuleContext
{
    string ReleaseId { get; }
    IServiceProvider HostServices { get; }
    IHotUpdateServiceRegistry Services { get; }
    IReadOnlySet<string> HostCapabilities { get; }
}

public interface IHotUpdateServiceRegistry : IServiceProvider
{
    void Export<TContract>(
        TContract service,
        HotServiceReplacementPolicy policy = HotServiceReplacementPolicy.Exclusive)
        where TContract : class;
    IReadOnlyList<TContract> GetAll<TContract>() where TContract : class;
}

public sealed class HotUpdateServiceRegistry : IHotUpdateServiceRegistry
{
    private readonly Dictionary<Type, ServiceEntry> _services = [];

    public void Export<TContract>(
        TContract service,
        HotServiceReplacementPolicy policy = HotServiceReplacementPolicy.Exclusive)
        where TContract : class
    {
        ArgumentNullException.ThrowIfNull(service);
        var contract = typeof(TContract);
        if (!contract.IsInstanceOfType(service))
            throw new ArgumentException($"Service does not implement '{contract.FullName}'.", nameof(service));
        if (!_services.TryGetValue(contract, out var entry))
        {
            _services.Add(contract, new ServiceEntry(policy, [service]));
            return;
        }
        if (entry.Policy != policy)
            throw new InvalidOperationException(
                $"Hot service '{contract.FullName}' changed replacement policy from {entry.Policy} to {policy}.");
        switch (policy)
        {
            case HotServiceReplacementPolicy.Exclusive:
                throw new InvalidOperationException($"Hot service '{contract.FullName}' is exclusive.");
            case HotServiceReplacementPolicy.Replaceable:
                entry.Services.Clear();
                entry.Services.Add(service);
                break;
            case HotServiceReplacementPolicy.Multiple:
                entry.Services.Add(service);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(policy));
        }
    }

    public object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return _services.TryGetValue(serviceType, out var entry) ? entry.Services.LastOrDefault() : null;
    }

    public IReadOnlyList<TContract> GetAll<TContract>() where TContract : class =>
        _services.TryGetValue(typeof(TContract), out var entry)
            ? Array.AsReadOnly(entry.Services.Cast<TContract>().ToArray())
            : Array.Empty<TContract>();

    private sealed record ServiceEntry(HotServiceReplacementPolicy Policy, List<object> Services);
}
