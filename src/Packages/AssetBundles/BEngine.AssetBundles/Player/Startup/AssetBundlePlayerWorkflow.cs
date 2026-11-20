using BEngine.Hosting;
using BEngine.Platform;
using BEngine.Player.ProjectSystem;
using BEngine.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Player;

/// <summary>Adapts packaged metadata and resource content to the engine Player host.</summary>
internal sealed class AssetBundlePlayerWorkflow : IPlayerContentWorkflow
{
    internal static AssetBundlePlayerWorkflow Shared { get; } = new();
    internal static PlayerHost Host { get; } = new(Shared);

    public void ConfigureServices(IServiceCollection services) =>
        services.TryAddSingleton<IPlayerStartupResolver, SystemPlayerStartupResolver>();

    public RuntimeWorkspace OpenWorkspace(string projectPath, IServiceProvider services) =>
        PlayerWorkspace.OpenRuntime(projectPath, services.GetRequiredService<FileUtility>(),
            services.GetRequiredService<PathUtility>());

    public IDisposable? ActivateStartupResources(PlayerStartupDescriptor startup, IServiceProvider services)
    {
        if (startup.Manifest is not { Version: >= 4 } manifest) return null;
        var resolver = services.GetRequiredService<IPlayerStartupResolver>();
        var resources = new PlayerBuiltInResourceProvider(
            manifest.ResolvePlayerResourceArchive(resolver.BaseDirectory),
            services.GetRequiredService<FileUtility>(), services.GetRequiredService<PathUtility>());
        try { resources.Activate(); return resources; }
        catch { resources.Dispose(); throw; }
    }

    public IPlayerStartupLog StartDiagnostics(PlayerStartupDescriptor startup, string? persistentDataPath) =>
        PlayerStartupDiagnostics.Start(startup.ProjectPath, startup.Manifest, persistentDataPath,
            startup.Manifest?.WritePlayerLog ?? true);
    public IPlayerStartupLog StartFailureDiagnostics(string playerRoot) =>
        PlayerStartupDiagnostics.StartForPlayerRoot(playerRoot);
    public PlayerRuntimeDiagnostics Diagnostics => AssetBundlePlayerContent.Diagnostics;
    public BValueTask<IPlayerRuntimeContent> PrepareAsync(RuntimeWorkspace workspace,
        string? persistentDataPath, IReadOnlyList<ServiceDescriptor> hostServices,
        IServiceProvider startupServices, BCancellationToken cancellationToken) =>
        AssetBundlePlayerContent.PrepareAsync(workspace, persistentDataPath, hostServices,
            startupServices, cancellationToken);
}
