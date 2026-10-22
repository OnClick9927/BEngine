using BEngine.DependencyInjection;
using BEngine.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Player;

/// <summary>Scopes a Player's explicit persistent directory to its service providers.</summary>
internal sealed class PlayerPersistentApplicationPaths(IApplicationPaths inner, string persistentDirectory)
    : IApplicationPaths
{
    public string BaseDirectory => inner.BaseDirectory;
    public string CurrentDirectory => inner.CurrentDirectory;
    public string TemporaryDirectory => inner.TemporaryDirectory;
    public string? ProcessPath => inner.ProcessPath;
    public string GetPersistentDataDirectory(string companyName, string productName) => persistentDirectory;

    internal static string? Configure(
        IServiceCollection services,
        string? explicitPath = null,
        IServiceProvider? owningServices = null)
    {
        var platformServices = owningServices ?? new BootstrapServiceResolver(services);
        var configured = string.IsNullOrWhiteSpace(explicitPath)
            ? platformServices.GetRequiredService<PlatformUtility>().GetEnvironmentVariable(
                BEnginePlayer.PersistentDataPathEnvironmentVariable)
            : explicitPath;
        if (string.IsNullOrWhiteSpace(configured)) return null;
        var paths = platformServices.GetRequiredService<PathUtility>();
        if (!paths.IsPathFullyQualified(configured))
            throw new InvalidDataException("The Player persistent data override must be an absolute path.");
        var directory = paths.TrimEndingDirectorySeparator(paths.GetFullPath(configured));
        // The owning provider keeps custom disposable path services alive while the
        // Player providers borrow this facade.
        var original = platformServices.GetRequiredService<IApplicationPaths>();
        var overridden = new PlayerPersistentApplicationPaths(original, directory);
        services.Replace(ServiceDescriptor.Singleton<IApplicationPaths>(overridden));
        // A host may already have materialized the utility. Replace that facade too.
        services.Replace(ServiceDescriptor.Singleton(new ApplicationPathUtility(overridden)));
        return directory;
    }
}
