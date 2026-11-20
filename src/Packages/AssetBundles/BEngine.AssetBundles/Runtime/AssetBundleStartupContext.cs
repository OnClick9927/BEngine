using BEngine.Player;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.AssetBundles;

/// <summary>Content capabilities owned by one staged content session. Does not choose a game workflow.</summary>
public sealed class AssetBundleStartupContext
{
    public AssetBundleStartupContext(
        Func<BCancellationToken, BValueTask<AssetBundleUpdatePlan>> checkForUpdates,
        Func<AssetBundleUpdatePlan, IProgress<AssetBundleUpdateProgress>, BCancellationToken, BValueTask> applyUpdate,
        Func<BCancellationToken, BValueTask<(bool IsValid, string Error)>> validateContent,
        bool failStartupWhenUpdateFails, Func<string, string?> readOption, PlayerRuntimeDiagnostics? diagnostics = null)
    {
        CheckForUpdates = checkForUpdates ?? throw new ArgumentNullException(nameof(checkForUpdates));
        ApplyUpdate = applyUpdate ?? throw new ArgumentNullException(nameof(applyUpdate));
        ValidateContent = validateContent ?? throw new ArgumentNullException(nameof(validateContent));
        ReadOption = readOption ?? throw new ArgumentNullException(nameof(readOption));
        FailStartupWhenUpdateFails = failStartupWhenUpdateFails;
        Diagnostics = diagnostics ?? PlayerRuntimeDiagnostics.Default;
    }
    public Func<BCancellationToken, BValueTask<AssetBundleUpdatePlan>> CheckForUpdates { get; }
    public Func<AssetBundleUpdatePlan, IProgress<AssetBundleUpdateProgress>, BCancellationToken, BValueTask> ApplyUpdate { get; }
    public Func<BCancellationToken, BValueTask<(bool IsValid, string Error)>> ValidateContent { get; }
    public bool FailStartupWhenUpdateFails { get; }
    /// <summary>Reads optional workflow configuration without exposing the operating-system host.</summary>
    public Func<string, string?> ReadOption { get; }
    public PlayerRuntimeDiagnostics Diagnostics { get; }
}

/// <summary>Game-owned policy around AB capabilities; the content session owns and disposes this instance.</summary>
public interface IAssetBundleStartupWorkflow : IPlayerStageTransitionFlow, IDisposable
{
    /// <summary>Registers borrowed workflow services. Implementations must not transfer disposal ownership.</summary>
    void ConfigureServices(IServiceCollection services);
}
