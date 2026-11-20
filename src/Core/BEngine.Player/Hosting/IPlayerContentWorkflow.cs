using BEngine.Platform;
using BEngine.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.Player;

/// <summary>Content and deployment policy consumed by the engine-owned Player host.</summary>
/// <remarks>Configure services before resolving them. Returned resources and log leases
/// transfer ownership to the host; a throwing factory must release its partial work.</remarks>
public interface IPlayerContentWorkflow
{
    void ConfigureServices(IServiceCollection services);
    RuntimeWorkspace OpenWorkspace(string projectPath, IServiceProvider services);
    IDisposable? ActivateStartupResources(PlayerStartupDescriptor startup, IServiceProvider services);
    IPlayerStartupLog StartDiagnostics(PlayerStartupDescriptor startup, string? persistentDataPath);
    IPlayerStartupLog StartFailureDiagnostics(string playerRoot);
    PlayerRuntimeDiagnostics Diagnostics { get; }
    BValueTask<IPlayerRuntimeContent> PrepareAsync(RuntimeWorkspace workspace,
        string? persistentDataPath, IReadOnlyList<ServiceDescriptor> hostServices,
        IServiceProvider startupServices, BCancellationToken cancellationToken);
}

/// <summary>An owned startup-log lease; the content workflow selects its storage policy.</summary>
public interface IPlayerStartupLog : IDisposable
{
    string LogPath { get; }
}
