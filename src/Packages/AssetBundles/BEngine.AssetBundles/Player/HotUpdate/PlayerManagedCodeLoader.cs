using BEngine.AssetBundles;
using BEngine.Build;
using BEngine.Player.HotUpdate;
using BEngine.Platform;

namespace BEngine.Player;

/// <summary>Prepares the complete managed-code release fixed by the content build.</summary>
internal static class PlayerManagedCodeLoader
{
    public static async BValueTask<ManagedCodePlayerSession?> LoadAsync(
        IManagedCodeRuntimeFactory runtimeFactory,
        IAssetBundleManager? assetBundles = null,
        PlayerManagedCodeStage stage = PlayerManagedCodeStage.Legacy,
        PlayerBuiltInResourceProvider? builtInResources = null,
        BCancellationToken cancellationToken = default,
        FileUtility? fileSystem = null,
        PathUtility? pathService = null,
        PlatformUtility? platformService = null)
    {
        ArgumentNullException.ThrowIfNull(runtimeFactory);
        fileSystem ??= RuntimeServices.GetRequiredService<global::BEngine.Platform.FileUtility>();
        pathService ??= RuntimeServices.GetRequiredService<global::BEngine.Platform.PathUtility>();
        platformService ??= RuntimeServices.GetRequiredService<global::BEngine.Platform.PlatformUtility>();
        var phasePrefix = stage == PlayerManagedCodeStage.Aot ? "03_AOT_ASSEMBLY" : "05_HOTUPDATE";
        PlayerStartupDiagnostics.Phase($"{phasePrefix}_PREPARE_STARTED");
        using var releaseTiming = PlayerStartupPerformanceDiagnostics.Begin("managedCode.releaseLoadSynchronousPrefix");
        var releaseOperation = PlayerHotUpdateReleaseLoader.LoadAsync(
            assetBundles, stage, builtInResources, cancellationToken);
        releaseTiming.Dispose();
        var release = await releaseOperation;
        if (stage == PlayerManagedCodeStage.Aot)
            BEngine.Hosting.HostStartupEnvironment.Current?.Report(.65f, "managedCode");
        if (release is null)
        {
            throw new InvalidDataException(stage == PlayerManagedCodeStage.Aot
                ? "The built-in AOT resource archive has no managed-code release. Build the project content before starting the Player."
                : "The active AssetBundle content has no managed-code release. Publish all hot-update assemblies through the content build before starting the Player.");
        }
        var moduleNames = Gather2121(release.Modules);
        [global::BEngine.SkipLoopGuard]
        string[] Gather2121(global::System.Collections.Generic.IEnumerable<global::BEngine.Player.HotUpdate.ManagedCodeModule> items2121)
        {
            ArgumentNullException.ThrowIfNull(items2121, "source");
            string Evaluate2121(global::BEngine.Player.HotUpdate.ManagedCodeModule module) => module.Name;
            global::System.ReadOnlySpan<global::BEngine.Player.HotUpdate.ManagedCodeModule> values;
            if (items2121.GetType() == typeof(global::BEngine.Player.HotUpdate.ManagedCodeModule[]))
                values = (global::BEngine.Player.HotUpdate.ManagedCodeModule[])items2121;
            else if (items2121.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Player.HotUpdate.ManagedCodeModule>))
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Player.HotUpdate.ManagedCodeModule>)items2121);
            else
            {
                return global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Select(items2121, Evaluate2121));
            }

            if (values.IsEmpty)
                return global::System.Array.Empty<string>();
            var result = new string[values.Length];
            for (var index = 0; index < values.Length; index++)
                result[index] = Evaluate2121(values[index]);
            return result;
        }
        var containsAot = moduleNames.Contains("AOT", StringComparer.OrdinalIgnoreCase);
        if (stage == PlayerManagedCodeStage.Aot && !containsAot)
            throw new InvalidDataException(
                "The built-in managed-code release must contain the project's AOT assembly.");
        if (stage == PlayerManagedCodeStage.HotUpdate && containsAot)
            throw new InvalidDataException(
                "The remote HotUpdate managed-code release must not contain the AOT assembly.");
        var target = BuildTargetManifestSerializer.LoadCurrent(
            baseDirectory: null, fileSystem, pathService, platformService);
        var gate = new HotUpdateActivationGate(runtimeFactory, CreateHostCapabilities(target));
        using var prepareTiming = PlayerStartupPerformanceDiagnostics.Begin("managedCode.runtimePrepareSynchronousPrefix");
        var prepareOperation = gate.PrepareAsync(
            release,
            new ManagedCodeRuntimeRequest(target.ManagedCodeRuntime),
            cancellationToken);
        prepareTiming.Dispose();
        var prepared = await prepareOperation;
        if (stage == PlayerManagedCodeStage.Aot)
            BEngine.Hosting.HostStartupEnvironment.Current?.Report(.75f, "managedCode");
        PlayerStartupDiagnostics.Phase($"{phasePrefix}_PREPARED",
            $"release={release.ReleaseId};runtime={prepared.RuntimeKind};" +
            $"assemblies={string.Join(',', prepared.Assemblies.Select(AssemblyName))};" +
            $"owned={string.Join(',', prepared.OwnedAssemblies.Select(AssemblyName))};" +
            $"modules={string.Join(',', prepared.ModuleIds)}");
        var assemblyCount = prepared.Assemblies.Count;
        return new ManagedCodePlayerSession(prepared,
            domain => ReportActivation(domain, stage, assemblyCount),
            exception => PlayerStartupDiagnostics.Failure("HOTUPDATE_RUNTIME_STOP", exception));
    }

    private static void ReportActivation(HotUpdateDomain domain, PlayerManagedCodeStage stage, int assemblyCount)
    {
        if (stage == PlayerManagedCodeStage.Aot)
        {
            PlayerStartupDiagnostics.Phase("03_AOT_ASSEMBLY_ACTIVATED",
                $"release={domain.Release.ReleaseId};runtime={domain.RuntimeKind};assemblies={assemblyCount}");
            Debug.Log($"Activated AOT startup release '{domain.Release.ReleaseId}' using {domain.RuntimeKind}.");
        }
        else
        {
            PlayerStartupDiagnostics.Phase("05_HOTUPDATE_INJECTED",
                $"release={domain.Release.ReleaseId};runtime={domain.RuntimeKind};assemblies={assemblyCount}");
            Debug.Log($"Activated HotUpdate release '{domain.Release.ReleaseId}' using {domain.RuntimeKind}.");
        }
    }

    private static string AssemblyName(System.Reflection.Assembly assembly) =>
        assembly.GetName().Name ?? "<unnamed>";

    private static IEnumerable<string> CreateHostCapabilities(BuildTargetManifest target)
    {
        yield return $"platform:{target.Platform}";
        yield return $"architecture:{target.Architecture}";
        yield return $"runtime:{target.ManagedCodeRuntime}";
        foreach (var backend in target.GraphicsBackends) yield return $"graphics:{backend}";
    }

}
