namespace BEngine.Editor.Build;

/// <summary>Content-owned roots and resource dependencies for a managed-code build.</summary>
public interface IPlayerManagedCodeResourceGraph
{
    /// <summary>Returns project-relative assets reached by the current code and resources.</summary>
    IReadOnlyList<string> CollectAssets(
        IReadOnlyList<string> assemblyPaths, IReadOnlyCollection<string> resourceFiles,
        PlayerStrippingSettings settings, CancellationToken cancellationToken);

    /// <summary>Selects resource inputs; the code pipeline retains ownership of assembly images.</summary>
    IReadOnlyList<RuntimePackageResourceInput> FilterResources(
        IReadOnlyList<RuntimeManagedCodeReleaseInput> assemblies,
        IReadOnlyList<RuntimePackageResourceInput> availableResources, IReadOnlyList<string> referencedFiles,
        PlayerStrippingSettings settings, PlayerStrippingReport report);
}

public sealed record PlayerManagedCodeReachabilityResult(
    RuntimeManagedCodeReleaseInputSet Inputs, IReadOnlyList<string> AssetPaths);

public sealed record PlayerManagedCodeImageResult(
    RuntimeManagedCodeReleaseInputSet Inputs, RuntimeManagedCodeReleaseInputSet AllInputs);
