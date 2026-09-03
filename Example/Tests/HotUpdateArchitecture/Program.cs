using System.Reflection;
using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using BEngine.AssetBundles;
using BEngine.Build;
using BEngine.Content;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.HotUpdate;
using BEngine.Player;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using BEngine.Rendering.Rhi;
using BEngine.Startup;
using Microsoft.Extensions.DependencyInjection;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.AssetDatabase;

namespace BEngine.ExampleTests.HotUpdateArchitecture;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args is ["--content-publish-worker", var staging, var output, var ready, var gate])
            {
                RunContentPublishWorker(staging, output, ready, gate);
                return 0;
            }
            if (args is ["--content-lock-worker", var lockOutput, var package, var attempting, var acquired])
            {
                RunContentLockWorker(lockOutput, package, attempting, acquired);
                return 0;
            }
            if (args is ["--dynamic-trimmer-roots-only"])
            {
                VerifyDynamicManagedCodeTrimmerDescriptor();
                Console.WriteLine(
                    "DYNAMIC_TRIMMER_ROOTS_OK|pe-references,xml-safe,dynamic-filter,facade-and-corelib-roots");
                return 0;
            }
            if (args is ["--shader-filter-only"])
            {
                VerifyTargetShaderResourceFiltering();
                Console.WriteLine("TARGET_SHADER_FILTER_OK|direct3d,vulkan,opengl,unknown-safe,webgpu-pruned");
                return 0;
            }
            if (args is ["--persistent-data-paths-only"])
            {
                VerifyPlayerPersistentDataPaths();
                Console.WriteLine(
                    "PLAYER_PERSISTENT_DATA_OK|configurable-sandbox,legacy-migration,logs-outside-data,hot-resource-version-settings");
                return 0;
            }
            if (args is ["--hot-resource-version-only"])
            {
                await VerifyHotResourceVersionPublishingAsync().ConfigureAwait(false);
                Console.WriteLine(
                    "HOT_RESOURCE_VERSION_OK|strict-vn,numeric-order,portable-labels,concurrent-publish," +
                    "v1-v2,immutable-version,no-downgrade,explicit-latest-switch,catalog-sha," +
                    "version-only-latest-pointer,legacy-latest-migration,runtime-target");
                return 0;
            }
            if (args is ["--build-output-layout-only"])
            {
                VerifyDefaultBuildOutputLayout();
                Console.WriteLine("PLAYER_BUILD_OUTPUT_LAYOUT_OK|player-and-hotres-siblings");
                return 0;
            }
            if (args is ["--aot-flow-only"])
            {
                await VerifyManualAotStartupFlowAsync().ConfigureAwait(false);
                Console.WriteLine(
                    "PLAYER_AOT_FLOW_OK|manual-check,v2-confirmation,optional-v1-decline," +
                    "required-first-install,auto-decline-gate,confirm-ready," +
                    "idempotent-actions,interactive-release-order," +
                    "fallback-last-valid,require-latest");
                return 0;
            }
            if (args is ["--built-in-resources-only"])
            {
                await BuiltInResourceRuntimeTests.RunAsync(FindRepositoryRoot()).ConfigureAwait(false);
                Console.WriteLine(
                    "PLAYER_BUILTIN_RESOURCES_OK|provider-priority,transition-unregister," +
                    "resources-addressing,aot-scene,aot-managed-code,no-hotupdate-fallback");
                return 0;
            }
            RuntimeMetadataTests.Run();
            PlayerSplashScreenTests.Run(FindRepositoryRoot());
            VerifyPlayerPersistentDataPaths();
            VerifyDefaultBuildOutputLayout();
            await VerifyManualAotStartupFlowAsync().ConfigureAwait(false);
            await BuiltInResourceRuntimeTests.RunAsync(FindRepositoryRoot()).ConfigureAwait(false);
            VerifyBuildTargetsAndRendererSelection();
            VerifyDynamicManagedCodeTrimmerDescriptor();
            VerifyTargetShaderResourceFiltering();
            await VerifyVirtualAssetBundlesAsync().ConfigureAwait(false);
            await VerifyPackageReleaseInputsAsync().ConfigureAwait(false);
            await VerifyManagedCodeAssetBundleAsync().ConfigureAwait(false);
            await VerifyActivationTransactionAsync().ConfigureAwait(false);
            await VerifyCoreClrRuntimeAsync().ConfigureAwait(false);
            await VerifyAotInterpreterRuntimeAsync().ConfigureAwait(false);
            await VerifyDesktopPlayerBuildAsync().ConfigureAwait(false);
            await VerifyExportedDesktopBuildHostAsync().ConfigureAwait(false);
            VerifyBuildProviderBoundaries();
            Console.WriteLine(
                "HOT_UPDATE_ARCHITECTURE_OK|binary-runtime-metadata,integrity-check,persistent-data-sandbox,build-targets," +
                "renderer-auto,target-shader-filter,native-splash,windows-gui-subsystem,dynamic-trimmer-roots,virtual-ab,no-remote-update," +
                "snapshot-integrity,package-code-and-resources,managed-code-ab,managed-symbol-trimming,module-graph," +
                "activation-rollback,service-replacement,collectible-coreclr," +
                "aot-interpreter-provider,builtin-aot-resources,platform-build-providers,desktop-player-package," +
                "embedded-player-bootstrap,renamed-executable,build-output-safety,atomic-player-replace," +
                "exported-build-host,headless-player-validation");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"HOT_UPDATE_ARCHITECTURE_FAILED|{exception}");
            return 1;
        }
    }

    private static void VerifyPlayerPersistentDataPaths()
    {
        const string company = "BEngine";
        const string product = "BEngine 2D Showcase";
        var first = PlayerPersistentDataPaths.ResolveDefault(company, product);
        var second = PlayerPersistentDataPaths.ResolveDefault(company, product);
        Require(first == second && Path.IsPathFullyQualified(first),
            "Player persistent-data path must be absolute and stable across runs.");

        if (OperatingSystem.IsWindows())
        {
            var expected = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                company, product));
            Require(first.Equals(expected, StringComparison.OrdinalIgnoreCase),
                $"Windows Player persistent data must use LocalApplicationData. Expected '{expected}', got '{first}'.");
        }

        var platformRoot = Directory.GetParent(Directory.GetParent(first)!.FullName)!.FullName;
        var hostile = PlayerPersistentDataPaths.ResolveDefault("../Unsafe\\Company", "../../Product");
        var relative = Path.GetRelativePath(platformRoot, hostile);
        Require(!Path.IsPathRooted(relative) && relative != ".." &&
                !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal),
            "Company or product names can escape the platform persistent-data root.");
        Require(!hostile.Contains($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal),
            "Player persistent-data path retained a traversal segment.");

        var reserved = PlayerPersistentDataPaths.ResolveDefault("CON", "LPT1.txt");
        Require(Path.GetFileName(Directory.GetParent(reserved)!.FullName) == "CON_" &&
                Path.GetFileName(reserved) == "LPT1.txt_",
            "Windows reserved device names were not made safe for persistent-data directories.");

        var root = Path.Combine(Path.GetTempPath(), $"BEnginePlayerCache.{Guid.NewGuid():N}");
        var packagedName = PlayerBuildLayout.SanitizePlayerName(product);
        var runtimeRoot = Path.Combine(
            root, $"{packagedName}{PlayerPackagedResourceAddresses.DataDirectorySuffix}");
        var linkedCache = Path.Combine(root, "linked-cache");
        try
        {
            Directory.CreateDirectory(runtimeRoot);
            var manifest = new PlayerBootstrapManifest
            {
                ProductName = product,
                Executable = $"{packagedName}.exe",
                DataDirectory = $"{packagedName}{PlayerPackagedResourceAddresses.DataDirectorySuffix}",
                ResourceDirectory = $"{packagedName}{PlayerPackagedResourceAddresses.DataDirectorySuffix}",
                AssemblyDirectory = $"{packagedName}{PlayerPackagedResourceAddresses.DataDirectorySuffix}/" +
                                    PlayerPackagedResourceAddresses.AssemblyDirectoryName,
                PlayerResourceArchive =
                    $"{packagedName}{PlayerPackagedResourceAddresses.DataDirectorySuffix}/" +
                    PlayerPackagedResourceAddresses.ResourcesDirectoryName + "/" +
                    PlayerPackagedResourceAddresses.PlayerArchiveFileName,
                BuildTargetResource = PlayerPackagedResourceAddresses.BuildTargetManifest,
                SplashScreenEnabled = false
            };
            var packagedDefault = PlayerPersistentDataPaths.ResolvePackaged(runtimeRoot, manifest);
            Require(packagedDefault.Equals(Path.Combine(root, "sandbox"),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal),
                "Packaged Player cache does not default beside the product _Data directory as 'sandbox'.");

            var legacyRoot = Path.Combine(root, PlayerBootstrapManifest.LegacyCacheDirectory);
            Directory.CreateDirectory(legacyRoot);
            File.WriteAllText(Path.Combine(legacyRoot, "migration.marker"), "legacy");
            var migrated = PlayerPersistentDataPaths.ResolvePackaged(runtimeRoot, manifest);
            Require(migrated.Equals(Path.Combine(root, "sandbox"),
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
                    File.Exists(Path.Combine(migrated, "migration.marker")) && !Directory.Exists(legacyRoot),
                "The misspelled legacy cache directory was not migrated to 'sandbox'.");

            manifest.CacheDirectory = "cache/custom";
            var configured = PlayerPersistentDataPaths.ResolvePackaged(runtimeRoot, manifest);
            Require(configured.Equals(Path.Combine(root, "cache", "custom"),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal),
                "Packaged Player cache did not honor its bootstrap-relative directory.");
            var previousLogPath = Environment.GetEnvironmentVariable(
                PlayerStartupDiagnostics.LogPathEnvironmentVariable);
            var previousPersistentPath = Environment.GetEnvironmentVariable(
                BEnginePlayer.PersistentDataPathEnvironmentVariable);
            try
            {
                Environment.SetEnvironmentVariable(PlayerStartupDiagnostics.LogPathEnvironmentVariable, null);
                Environment.SetEnvironmentVariable(BEnginePlayer.PersistentDataPathEnvironmentVariable, null);
                using var diagnostics = PlayerStartupDiagnostics.Start(
                    runtimeRoot, manifest, null, captureEngineLog: false);
                Require(diagnostics.LogPath.Equals(
                        Path.Combine(configured, "logs", "player.log"),
                        OperatingSystem.IsWindows()
                            ? StringComparison.OrdinalIgnoreCase
                            : StringComparison.Ordinal) &&
                        !Directory.Exists(Path.Combine(runtimeRoot, "logs")),
                    "Packaged Player diagnostics must be written inside the configured sandbox, not _data.");
            }
            finally
            {
                Environment.SetEnvironmentVariable(
                    PlayerStartupDiagnostics.LogPathEnvironmentVariable, previousLogPath);
                Environment.SetEnvironmentVariable(
                    BEnginePlayer.PersistentDataPathEnvironmentVariable, previousPersistentPath);
            }
            Require(PlayerPersistentDataPaths.ResolveExplicitOverride(
                        Path.Combine(root, "test-override")) ==
                    Path.Combine(root, "test-override"),
                "The explicit Player cache override was not preserved for tests.");
            RequireThrows<InvalidDataException>(() =>
                PlayerPersistentDataPaths.ResolveExplicitOverride("relative-cache"));
            RequireThrows<InvalidDataException>(() =>
                PlayerBootstrapManifest.NormalizeCacheDirectory("../outside"));

            var physicalCache = Path.Combine(root, "physical-cache");
            Directory.CreateDirectory(physicalCache);
            try
            {
                Directory.CreateSymbolicLink(linkedCache, physicalCache);
                manifest.CacheDirectory = "linked-cache";
                RequireThrows<InvalidDataException>(() =>
                    PlayerPersistentDataPaths.ResolvePackaged(runtimeRoot, manifest));
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or
                                               PlatformNotSupportedException)
            {
                // Some Windows hosts do not grant symbolic-link creation to the test process.
            }

            var workspace = ProjectWorkspaceFactory.Create(Path.Combine(root, "SettingsProject"),
                "Cache Settings");
            var settings = PlayerBuildSettingsStore.Load(workspace.RootPath);
            Require(settings.CacheDirectory == PlayerBootstrapManifest.DefaultCacheDirectory &&
                    settings.HotResourceVersion == "v1",
                "New projects do not default to the 'sandbox' cache and Hot Resources v1.");
            settings.CacheDirectory = "cache/settings";
            settings.HotResourceVersion = "v2";
            PlayerBuildSettingsStore.Save(workspace.RootPath, settings);
            var persistedSettings = PlayerBuildSettingsStore.Load(workspace.RootPath);
            Require(persistedSettings.CacheDirectory == "cache/settings" &&
                    persistedSettings.HotResourceVersion == "v2",
                "Build Settings did not persist the Player cache and Hot Resource version.");
            settings.HotResourceVersion = "../v3";
            RequireThrows<InvalidDataException>(() =>
                PlayerBuildSettingsStore.Save(workspace.RootPath, settings));
            settings.HotResourceVersion = "v2";
            settings.CacheDirectory = "../../outside";
            RequireThrows<InvalidDataException>(() =>
                PlayerBuildSettingsStore.Save(workspace.RootPath, settings));
        }
        finally
        {
            try
            {
                if (Directory.Exists(linkedCache)) Directory.Delete(linkedCache);
            }
            catch { }
            TryDeleteTestDirectory(root);
        }
    }

    private static void VerifyDefaultBuildOutputLayout()
    {
        var project = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(), $"BEngineBuildLayout.{Guid.NewGuid():N}", "Project"));
        var buildRoot = PlayerBuildLayout.GetDefaultBuildRoot(project);
        var hotResourceRoot = PlayerBuildLayout.GetDefaultHotResourceRoot(project);
        var playerOutput = Path.Combine(buildRoot, "test product");

        Require(buildRoot.Equals(Path.Combine(project, "Build"),
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal) &&
                hotResourceRoot.Equals(Path.Combine(buildRoot, "hotres"),
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal) &&
                Path.GetDirectoryName(hotResourceRoot)!.Equals(buildRoot,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal),
            "Default Player and Hot Resource outputs are not siblings below <Project>/Build.");
        PlayerBuildMenuCommands.ValidateOutputLocation(project, buildRoot, playerOutput);
        Require(!playerOutput.StartsWith(hotResourceRoot + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal),
            "The Player output was nested inside the Hot Resource publication root.");
    }

    private static async Task VerifyManualAotStartupFlowAsync()
    {
        var environmentNames = new[]
        {
            PlayerAotStartupFlow.AutoCheckEnvironmentVariable,
            PlayerAotStartupFlow.AutoConfirmUpdateEnvironmentVariable,
            PlayerAotStartupFlow.AutoDeclineUpdateEnvironmentVariable,
            PlayerAotStartupFlow.AutoContinueEnvironmentVariable
        };
        var previousEnvironment = environmentNames.ToDictionary(
            static name => name,
            Environment.GetEnvironmentVariable,
            StringComparer.Ordinal);
        try
        {
            foreach (var name in environmentNames) Environment.SetEnvironmentVariable(name, null);

            var updatePlan = CreateAotFlowUpdatePlan("remote-v2");
            var checkGate = new TaskCompletionSource<AssetBundleUpdatePlan>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var applyGate = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var confirmationQueued = new ManualResetEventSlim();
            using var allowCheckCompletion = new ManualResetEventSlim();
            var checkCalls = 0;
            var applyCalls = 0;
            var validationCalls = 0;
            using (var flow = new PlayerAotStartupFlow(
                       _ =>
                       {
                           Interlocked.Increment(ref checkCalls);
                           return checkGate.Task;
                       },
                       async (plan, _, cancellationToken) =>
                       {
                           Require(ReferenceEquals(plan, updatePlan),
                               "AOT flow did not apply the update plan returned by its check.");
                           Interlocked.Increment(ref applyCalls);
                           await applyGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                       },
                       _ =>
                       {
                           Interlocked.Increment(ref validationCalls);
                           return Task.FromResult((true, string.Empty));
                       },
                       failStartupWhenUpdateFails: true))
            {
                flow.StateEnqueuedForTesting = state =>
                {
                    if (state.Phase != AotStartupPhase.AwaitingUpdateConfirmation) return;
                    confirmationQueued.Set();
                    if (!allowCheckCompletion.Wait(TimeSpan.FromSeconds(5)))
                        throw new TimeoutException(
                            "Timed out holding the completed check at its interactive transition.");
                };
                flow.Start();
                flow.Pump();
                await Task.Yield();
                flow.Pump();
                Require(flow.Current.Phase == AotStartupPhase.WaitingForUpdateCheck &&
                        flow.Current.CanCheckForUpdates && checkCalls == 0 && applyCalls == 0,
                    "Start/Pump initiated an update check without an explicit user action.");

                flow.CheckForUpdates();
                flow.CheckForUpdates();
                Require(checkCalls == 1,
                    "Repeated Check for Updates clicks started more than one check operation.");
                checkGate.SetResult(updatePlan);
                try
                {
                    var queued = await Task.Run(() =>
                            confirmationQueued.Wait(TimeSpan.FromSeconds(5)))
                        .ConfigureAwait(false);
                    Require(queued,
                        "The update check did not publish its confirmation transition.");
                    flow.Pump();
                    Require(flow.Current.RequiresUpdateConfirmation && flow.Current.CanDeclineUpdate &&
                            flow.Current.TargetVersion == "remote-v2" &&
                            flow.Current.Status.Contains("remote-v2", StringComparison.Ordinal) &&
                            !flow.Current.CanEnterGame && applyCalls == 0 && validationCalls == 1,
                        "An optional v2 update did not expose its target version and confirmation boundary.");

                    flow.ConfirmUpdate();
                    Require(SpinWait.SpinUntil(
                            () => Volatile.Read(ref applyCalls) == 1, TimeSpan.FromSeconds(2)),
                        "Confirm was dropped after the interactive snapshot became visible.");
                    flow.ConfirmUpdate();
                    Require(applyCalls == 1,
                        "Repeated Confirm clicks started more than one update operation.");
                }
                finally
                {
                    allowCheckCompletion.Set();
                    applyGate.TrySetResult();
                }
                await PumpUntilAsync(flow,
                    static state => state.Phase == AotStartupPhase.Ready)
                    .ConfigureAwait(false);
                Require(flow.Current.CanEnterGame && validationCalls == 2 &&
                        !flow.EnterGameRequested,
                    "A confirmed, valid update did not reach Ready before requesting game entry.");
                flow.EnterGame();
                Require(flow.EnterGameRequested,
                    "The AOT flow rejected game entry after validating updated content.");
            }

            var declineChecks = 0;
            var declineApplies = 0;
            var declineValidations = 0;
            using (var flow = new PlayerAotStartupFlow(
                       _ =>
                       {
                           Interlocked.Increment(ref declineChecks);
                           return Task.FromResult(updatePlan);
                       },
                       (_, _, _) =>
                       {
                           Interlocked.Increment(ref declineApplies);
                           return Task.CompletedTask;
                       },
                       _ =>
                       {
                           Interlocked.Increment(ref declineValidations);
                           return Task.FromResult((true, string.Empty));
                       },
                       failStartupWhenUpdateFails: true))
            {
                flow.Start();
                flow.Pump();
                flow.CheckForUpdates();
                await PumpUntilAsync(flow,
                    static state => state.Phase == AotStartupPhase.AwaitingUpdateConfirmation)
                    .ConfigureAwait(false);
                Require(declineChecks == 1 && declineApplies == 0,
                    "The decline scenario did not preserve the manual confirmation boundary.");
                Require(flow.Current.CanDeclineUpdate && flow.Current.TargetVersion == "remote-v2",
                    "A valid installed v1 release was not allowed to postpone the v2 update.");

                flow.DeclineUpdate();
                flow.Pump();
                Require(flow.Current.Phase == AotStartupPhase.Ready &&
                        !flow.Current.RequiresUpdateConfirmation && !flow.Current.CanDeclineUpdate &&
                        flow.Current.CanEnterGame && declineValidations == 1 && declineApplies == 0 &&
                        flow.Current.Status.Contains("remote-v2", StringComparison.Ordinal),
                    "Declining v2 did not immediately expose the button for the installed v1 release.");
                flow.ConfirmUpdate();
                flow.DeclineUpdate();
                Require(declineApplies == 0 && declineValidations == 1,
                    "Confirmation actions remained active after the optional update was declined.");
                flow.EnterGame();
                Require(flow.EnterGameRequested,
                    "The installed v1 release could not start after postponing v2.");
            }

            var firstInstallApplies = 0;
            using (var flow = new PlayerAotStartupFlow(
                       _ => Task.FromResult(updatePlan),
                       (_, _, _) =>
                       {
                           Interlocked.Increment(ref firstInstallApplies);
                           return Task.CompletedTask;
                       },
                       _ => Task.FromResult((false,
                           "The sandbox has no valid HotUpdate game release.")),
                       failStartupWhenUpdateFails: true))
            {
                flow.Start();
                flow.Pump();
                flow.CheckForUpdates();
                await PumpUntilAsync(flow,
                    static state => state.Phase == AotStartupPhase.AwaitingUpdateConfirmation)
                    .ConfigureAwait(false);
                Require(flow.Current.TargetVersion == "remote-v2" &&
                        !flow.Current.CanDeclineUpdate && !flow.Current.CanEnterGame,
                    "The empty sandbox incorrectly exposed v2 as an optional update.");
                flow.DeclineUpdate();
                flow.EnterGame();
                flow.Pump();
                Require(flow.Current.Phase == AotStartupPhase.AwaitingUpdateConfirmation &&
                        !flow.EnterGameRequested && firstInstallApplies == 0,
                    "The required first install was bypassed by declining the v2 update.");
            }

            Environment.SetEnvironmentVariable(
                PlayerAotStartupFlow.AutoDeclineUpdateEnvironmentVariable, "1");
            Environment.SetEnvironmentVariable(
                PlayerAotStartupFlow.AutoConfirmUpdateEnvironmentVariable, "1");
            var automaticDeclineApplies = 0;
            using (var flow = new PlayerAotStartupFlow(
                       _ => Task.FromResult(updatePlan),
                       (_, _, _) =>
                       {
                           Interlocked.Increment(ref automaticDeclineApplies);
                           return Task.CompletedTask;
                       },
                       _ => Task.FromResult((true, string.Empty)),
                       failStartupWhenUpdateFails: true))
            {
                flow.Start();
                flow.Pump();
                flow.CheckForUpdates();
                await PumpUntilAsync(flow,
                    static state => state.Phase == AotStartupPhase.Ready)
                    .ConfigureAwait(false);
                Require(flow.Current.CanEnterGame && automaticDeclineApplies == 0,
                    "Automatic v2 decline did not prefer the installed valid v1 release.");
            }
            Environment.SetEnvironmentVariable(
                PlayerAotStartupFlow.AutoConfirmUpdateEnvironmentVariable, null);
            var automaticFirstInstallApplies = 0;
            using (var flow = new PlayerAotStartupFlow(
                       _ => Task.FromResult(updatePlan),
                       (_, _, _) =>
                       {
                           Interlocked.Increment(ref automaticFirstInstallApplies);
                           return Task.CompletedTask;
                       },
                       _ => Task.FromResult((false,
                           "The sandbox has no valid HotUpdate game release.")),
                       failStartupWhenUpdateFails: true))
            {
                flow.Start();
                flow.Pump();
                flow.CheckForUpdates();
                await PumpUntilAsync(flow,
                    static state => state.Phase == AotStartupPhase.AwaitingUpdateConfirmation)
                    .ConfigureAwait(false);
                flow.Pump();
                Require(!flow.Current.CanDeclineUpdate && !flow.Current.CanEnterGame &&
                        automaticFirstInstallApplies == 0,
                    "Automatic decline bypassed the required first game-content install.");
            }
            Environment.SetEnvironmentVariable(
                PlayerAotStartupFlow.AutoDeclineUpdateEnvironmentVariable, null);

            var fallbackValidationCalls = 0;
            var fallbackApplyCalls = 0;
            using (var flow = new PlayerAotStartupFlow(
                       _ => Task.FromException<AssetBundleUpdatePlan>(
                           new HttpRequestException("Remote catalog is unavailable.")),
                       (_, _, _) =>
                       {
                           Interlocked.Increment(ref fallbackApplyCalls);
                           return Task.CompletedTask;
                       },
                       _ =>
                       {
                           Interlocked.Increment(ref fallbackValidationCalls);
                           return Task.FromResult((true, string.Empty));
                       },
                       failStartupWhenUpdateFails: false))
            {
                flow.Start();
                flow.Pump();
                flow.CheckForUpdates();
                await PumpUntilAsync(flow,
                    static state => state.Phase == AotStartupPhase.Ready)
                    .ConfigureAwait(false);
                Require(flow.Current.CanEnterGame && flow.Current.CanRetry &&
                        fallbackValidationCalls == 1 && fallbackApplyCalls == 0 &&
                        flow.Current.Error.Contains("Remote catalog", StringComparison.Ordinal),
                    "FallbackToLastValid did not expose valid sandbox content after a remote check failure.");
                flow.EnterGame();
                Require(flow.EnterGameRequested,
                    "FallbackToLastValid did not permit entry with a validated sandbox release.");
            }

            var strictValidationCalls = 0;
            using (var flow = new PlayerAotStartupFlow(
                       _ => Task.FromException<AssetBundleUpdatePlan>(
                           new HttpRequestException("Remote catalog is unavailable.")),
                       (_, _, _) => Task.CompletedTask,
                       _ =>
                       {
                           Interlocked.Increment(ref strictValidationCalls);
                           return Task.FromResult((true, string.Empty));
                       },
                       failStartupWhenUpdateFails: true))
            {
                flow.Start();
                flow.Pump();
                flow.CheckForUpdates();
                await PumpUntilAsync(flow,
                    static state => state.Phase == AotStartupPhase.Failed)
                    .ConfigureAwait(false);
                Require(!flow.Current.CanEnterGame && flow.Current.CanRetry &&
                        !flow.EnterGameRequested && strictValidationCalls == 0,
                    "RequireLatest did not fail closed after a remote update check failure.");
                flow.EnterGame();
                Require(!flow.EnterGameRequested,
                    "RequireLatest allowed entry after a remote update check failure.");
            }
        }
        finally
        {
            foreach (var name in environmentNames)
                Environment.SetEnvironmentVariable(name, previousEnvironment[name]);
        }
    }

    private static AssetBundleUpdatePlan CreateAotFlowUpdatePlan(string versionText)
    {
        const string packageName = "com.bengine.tests.aot-flow";
        var catalog = new AssetBundleCatalog
        {
            PackageName = packageName,
            Version = versionText
        };
        var catalogBytes = AssetBundleCatalogSerializer.SerializeCatalog(catalog);
        var version = new AssetBundleVersion
        {
            PackageName = packageName,
            Version = versionText,
            CatalogSha256 = AssetBundleCatalogSerializer.ComputeSha256(catalogBytes),
            CatalogSize = catalogBytes.LongLength
        };
        return new AssetBundleUpdatePlan(version, catalog, catalogBytes, [], hasUpdates: true);
    }

    private static async Task PumpUntilAsync(
        PlayerAotStartupFlow flow,
        Func<AotStartupSnapshot, bool> predicate)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            flow.Pump();
            if (predicate(flow.Current)) return;
            await Task.Delay(10).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Timed out waiting for an AOT startup transition. Last phase: {flow.Current.Phase}; " +
            $"status: {flow.Current.Status}; error: {flow.Current.Error}");
    }

    private static async Task VerifyPackageReleaseInputsAsync()
    {
        const string packageId = "com.bengine.tests.release-input";
        const string packageAssembly = "BEngine.Tests.ReleaseInputPackage";
        const string markerType = "BEngine.Tests.ReleaseInputPackage.ReleaseInputMarker";
        const string systemType = "BEngine.Tests.ReleaseInputPackage.ReleaseInputRuntimeSystem";
        const string resourceAddress =
            "Assets/Packages/com.bengine.tests.release-input/Resources/Data/value.txt";
        var root = Path.Combine(Path.GetTempPath(), $"BEnginePackageRelease.{Guid.NewGuid():N}");
        try
        {
            var workspace = ProjectWorkspaceFactory.Create(Path.Combine(root, "Project"), "Package Release");
            DeleteDefaultAotContent(workspace);
            var packageRoot = Path.Combine(workspace.PackagesPath, "ReleaseInput");
            var sourceRoot = Path.Combine(packageRoot, packageAssembly);
            Directory.CreateDirectory(sourceRoot);
            Directory.CreateDirectory(Path.Combine(packageRoot, "Resources", "Data"));
            await File.WriteAllTextAsync(Path.Combine(workspace.PackagesPath, "manifest.yaml"), $$"""
                format: BEngine.Packages
                version: 1
                packages:
                - id: {{packageId}}
                  version: 1.0.0
                  enabled: true
                """).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(packageRoot, "package.yaml"), $$"""
                format: BEngine.Package
                version: 2
                id: {{packageId}}
                packageVersion: 1.0.0
                displayName: Release Input Fixture
                content:
                  runtime: Resources
                  editor: Editor
                runtime:
                  assembly: {{packageAssembly}}
                  rootNamespace: {{packageAssembly}}
                  dependencies: []
                """).ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(sourceRoot, $"{packageAssembly}.asmdef.yaml"), $$"""
                format: BEngine.AssemblyDefinition
                version: 1
                name: {{packageAssembly}}
                rootNamespace: {{packageAssembly}}
                references: []
                includePlatforms: []
                excludePlatforms: []
                defineConstraints: []
                autoReferenced: true
                editorOnly: false
                allowUnsafeCode: false
                """).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "ReleaseInputTypes.cs"), $$"""
                using BEngine;
                using BEngine.DependencyInjection;
                using Microsoft.Extensions.DependencyInjection;

                namespace {{packageAssembly}};

                public sealed class ReleaseInputMarker;

                public sealed class ReleaseInputServiceModule : IEngineServiceModule
                {
                    public void ConfigureServices(IServiceCollection services, EngineServiceContext context) =>
                        services.AddSingleton<ReleaseInputMarker>();
                }

                public sealed class ReleaseInputRuntimeSystem : ISceneRuntimeSystem
                {
                    public string packageId => "{{packageId}}";
                }
                """).ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(packageRoot, "Resources", "Data", "value.txt"),
                "package-resource-value").ConfigureAwait(false);

            Require(ProjectScriptCompiler.CompileAndLoad(workspace) is not null,
                "The package release fixture did not compile its project runtime graph.");
            var inputs = RuntimeManagedCodeReleaseInputCollector.Collect(workspace);
            var packageInput = inputs.Assemblies.Single(item => item.Name == packageAssembly);
            Require(packageInput.Origin == $"package:{packageId}" && File.Exists(packageInput.AssemblyPath),
                "The unified release collector omitted the enabled package runtime assembly.");
            Require(inputs.PackageResources.Single().Address == resourceAddress,
                "The unified release collector omitted or renamed the package runtime resource.");

            var database = new ProjectAssetDatabase(workspace);
            _ = database.Refresh();
            var snapshot = EditorVirtualAssetBundleBuilder.Build(workspace, database);
            Require(snapshot.Catalog.Assets.Any(asset => asset.Address == resourceAddress) &&
                    snapshot.Catalog.Assets.Any(asset =>
                        asset.Address == $"Assets/__BEngine/HotUpdate/{packageAssembly}.dll"),
                "Editor virtual AssetBundles omitted package code or Resources.");
            await using (var virtualManager = new VirtualAssetBundleManager(snapshot))
            {
                await virtualManager.InitializeAsync().ConfigureAwait(false);
                var provider = new AssetBundleResourceProvider(virtualManager);
                Require(provider.TryLoad("Data/value", "Resources", out var content) &&
                        Encoding.UTF8.GetString(content.Bytes) == "package-resource-value",
                    "Editor virtual AssetBundles could not resolve a package Resource through Resources paths.");
            }

            var build = AssetBundleBuilder.Build(
                workspace,
                database,
                [new AssetBundleBuildDefinition { Name = "main", AssetPaths = ["Assets"] }],
                new AssetBundleBuildOptions
                {
                    PackageName = "com.bengine.tests.package-release",
                    Version = "package-release-1",
                    OutputDirectory = Path.Combine(root, "Bundles"),
                    IncludeHotUpdateAssemblies = true,
                    IncludePackageRuntimeResources = true
                });
            AssertMinimalLatestPointer(
                build.PackageDirectory,
                build.Version.PackageName,
                build.Version.Version,
                build.VersionDirectory);
            Require(build.Catalog.Bundles.Any(bundle => bundle.Name == "bengine-packages"),
                "The real AssetBundle build omitted the package resource bundle.");
            await using var manager = new AssetBundleManager(new AssetBundleRuntimeOptions
            {
                PackageName = "com.bengine.tests.package-release",
                CacheDirectory = Path.Combine(root, "Cache"),
                BuiltInDirectory = build.PackageDirectory
            });
            await manager.InitializeAsync().ConfigureAwait(false);
            var runtimeProvider = new AssetBundleResourceProvider(manager);
            Require(runtimeProvider.TryLoad("Data/value", "Resources", out var runtimeContent) &&
                    Encoding.UTF8.GetString(runtimeContent.Bytes) == "package-resource-value",
                "The real AssetBundle build could not resolve a package Resource.");

            var release = await LoadManagedCodeReleaseAsync(manager).ConfigureAwait(false);
            Require(release.Modules.Any(module => module.Name == packageAssembly),
                "The real managed-code release omitted the package runtime assembly.");
            await using var runtime = new CoreClrManagedCodeRuntimeProvider().CreateRuntime();
            var loaded = await runtime.LoadAsync(release).ConfigureAwait(false);
            var services = new ServiceCollection();
            services.AddBEngine(
                new EngineServiceContext(EngineHostKind.Player, workspace.RootPath, "PackageReleaseTest"),
                loaded.Assemblies);
            Require(services.Any(descriptor => descriptor.ServiceType.FullName == markerType) &&
                    services.Any(descriptor => descriptor.ServiceType == typeof(ISceneRuntimeSystem) &&
                                               descriptor.ImplementationType?.FullName == systemType),
                "Package IEngineServiceModule or ISceneRuntimeSystem was not discovered from the hot assembly.");
        }
        finally
        {
            TryDeleteTestDirectory(root);
        }
    }

    private static async Task<ManagedCodeRelease> LoadManagedCodeReleaseAsync(IAssetBundleManager manager)
    {
        await using var manifestHandle = await manager.LoadBytesAsync(
            ManagedCodeReleaseManifest.DefaultAddress).ConfigureAwait(false);
        var manifest = ManagedCodeReleaseManifestSerializer.Deserialize(manifestHandle.Value);
        var modules = new List<ManagedCodeModule>(manifest.Modules.Count);
        foreach (var module in manifest.Modules)
        {
            await using var assembly = await manager.LoadBytesAsync(module.AssemblyAddress).ConfigureAwait(false);
            byte[] symbols = [];
            if (!string.IsNullOrWhiteSpace(module.SymbolsAddress))
            {
                await using var symbolsHandle = await manager.LoadBytesAsync(module.SymbolsAddress)
                    .ConfigureAwait(false);
                symbols = (byte[])symbolsHandle.Value.Clone();
            }
            modules.Add(new ManagedCodeModule(
                module.Name,
                module.BuildId,
                assembly.Value,
                symbols,
                module.Dependencies));
        }
        return new ManagedCodeRelease(manifest.ReleaseId, modules, manifest.ContractVersion);
    }

    private static async Task CreatePackagedReleaseInputFixtureAsync(ProjectWorkspace workspace)
    {
        const string packageId = "com.bengine.tests.release-input";
        const string assemblyName = "BEngine.Tests.ReleaseInputPackage";
        var packageRoot = Path.Combine(workspace.PackagesPath, "ReleaseInput");
        var sourceRoot = Path.Combine(packageRoot, assemblyName);
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(Path.Combine(packageRoot, "Resources", "Data"));
        await File.WriteAllTextAsync(workspace.PackageManifestPath, $$"""
            format: BEngine.Packages
            version: 1
            packages:
            - id: {{packageId}}
              version: 1.0.0
              enabled: true
            """).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(packageRoot, "package.yaml"), $$"""
            format: BEngine.Package
            version: 2
            id: {{packageId}}
            packageVersion: 1.0.0
            displayName: Release Input Fixture
            content:
              runtime: Resources
              editor: Editor
            runtime:
              assembly: {{assemblyName}}
              rootNamespace: {{assemblyName}}
              dependencies: []
            """).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, $"{assemblyName}.asmdef.yaml"), $$"""
            format: BEngine.AssemblyDefinition
            version: 1
            name: {{assemblyName}}
            rootNamespace: {{assemblyName}}
            references: []
            includePlatforms: []
            excludePlatforms: []
            defineConstraints: []
            autoReferenced: true
            editorOnly: false
            allowUnsafeCode: false
            """).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "ReleaseInputTypes.cs"), $$"""
            using BEngine;
            using BEngine.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection;

            namespace {{assemblyName}};

            public sealed class ReleaseInputMarker;

            public sealed class ReleaseInputServiceModule : IEngineServiceModule
            {
                public void ConfigureServices(IServiceCollection services, EngineServiceContext context) =>
                    services.AddSingleton<ReleaseInputMarker>();
            }

            public sealed class ReleaseInputRuntimeSystem : ISceneRuntimeSystem
            {
                public string packageId => "{{packageId}}";
            }
            """).ConfigureAwait(false);
        await File.WriteAllTextAsync(
            Path.Combine(packageRoot, "Resources", "Data", "value.txt"),
            "package-resource-value").ConfigureAwait(false);
    }

    private static async Task<PlayerContentFixture> CreatePlayerContentFixtureAsync(
        ProjectWorkspace workspace)
    {
        DeleteDefaultAotContent(workspace);
        await CreatePackagedReleaseInputFixtureAsync(workspace).ConfigureAwait(false);
        const string aotScene = PlayerContentBuildPipeline.AotScenePath;
        const string aotResource = "Assets/Aot/AotOnly.txt";
        const string selectedScene = "Assets/Scenes/Included.scene.yaml";
        const string excludedScene = "Assets/Scenes/Excluded.scene.yaml";
        const string referencedSprite = "Assets/Textures/ReferencedSprite.png";
        const string unusedTexture = "Assets/Textures/Unused.png";
        const string dynamicResource = "Assets/Resources/Dynamic/runtime.txt";
        const string unusedResource = "Assets/Unused/orphan.txt";

        var aotScenePath = workspace.ResolveInside(aotScene);
        var selectedScenePath = workspace.ResolveInside(selectedScene);
        var excludedScenePath = workspace.ResolveInside(excludedScene);
        var referencedSpritePath = workspace.ResolveInside(referencedSprite);
        var unusedTexturePath = workspace.ResolveInside(unusedTexture);
        Directory.CreateDirectory(Path.GetDirectoryName(aotScenePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(selectedScenePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(referencedSpritePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(workspace.ResolveInside(dynamicResource))!);
        Directory.CreateDirectory(Path.GetDirectoryName(workspace.ResolveInside(unusedResource))!);
        File.Copy(workspace.StartupScenePath, aotScenePath);
        File.Copy(workspace.StartupScenePath, selectedScenePath);
        File.Copy(workspace.StartupScenePath, excludedScenePath);
        await File.WriteAllTextAsync(Path.Combine(
            Path.GetDirectoryName(aotScenePath)!, "AOT.asmdef.yaml"), """
            format: BEngine.AssemblyDefinition
            version: 1
            name: AOT
            rootNamespace: AOT
            references:
            - BEngine.Tests.ReleaseInputPackage
            includePlatforms: []
            excludePlatforms: []
            defineConstraints: []
            autoReferenced: true
            editorOnly: false
            allowUnsafeCode: false
            """).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(
            Path.GetDirectoryName(aotScenePath)!, "AotEntry.cs"), """
            using BEngine;

            namespace AOT;

            public sealed class AotEntry : MonoBehaviour;
            """).ConfigureAwait(false);
        await File.WriteAllTextAsync(workspace.ResolveInside(aotResource), "aot-only-resource")
            .ConfigureAwait(false);
        await File.AppendAllTextAsync(aotScenePath, $"\n# {aotResource}\n").ConfigureAwait(false);
        var testTexture = Path.Combine(
            FindRepositoryRoot(), "src", "Core", "Editor", "Icons", "Windows", "Game.png");
        File.Copy(testTexture, referencedSpritePath);
        File.Copy(testTexture, unusedTexturePath);
        await File.WriteAllTextAsync(workspace.ResolveInside(dynamicResource), "dynamic-resource")
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(workspace.ResolveInside(unusedResource), "unused-resource")
            .ConfigureAwait(false);

        var database = new ProjectAssetDatabase(workspace);
        database.Refresh();
        var spriteGuid = database.AssetPathToGuid(referencedSprite) ??
                         throw new InvalidOperationException("The referenced test sprite was not imported.");
        await File.AppendAllTextAsync(selectedScenePath, $"\n# guid: {spriteGuid}\n")
            .ConfigureAwait(false);
        return new PlayerContentFixture(
            aotScene,
            aotResource,
            workspace.Project.StartupScene,
            selectedScene,
            referencedSprite,
            dynamicResource,
            [workspace.Project.StartupScene, excludedScene, unusedTexture, unusedResource]);
    }

    private static void DeleteDefaultAotContent(ProjectWorkspace workspace)
    {
        var aotRoot = Path.Combine(workspace.AssetsPath, "Aot");
        if (Directory.Exists(aotRoot)) Directory.Delete(aotRoot, recursive: true);
        var aotMeta = aotRoot + ".meta";
        if (File.Exists(aotMeta)) File.Delete(aotMeta);
    }

    private static void VerifyBuildTargetsAndRendererSelection()
    {
        Require(BuildTargetCatalog.All.Select(target => target.TargetId).Distinct().Count() ==
                BuildTargetCatalog.All.Count,
            "Build target ids are not unique.");
        var windows = BuildTargetManifest.FromDescriptor(BuildTargetCatalog.Get("windows-x64"));
        var restored = BuildTargetManifestSerializer.Deserialize(BuildTargetManifestSerializer.Serialize(windows));
        Require(restored.TargetId == "windows-x64" &&
                restored.ManagedCodeRuntime == ManagedCodeRuntimeKind.CoreClr,
            "Build target manifest did not round-trip.");
        var subset = BuildTargetManifest.FromDescriptor(
            BuildTargetCatalog.Get("windows-x64"),
            [GraphicsBackend.Direct3D11, GraphicsBackend.Vulkan]);
        var restoredSubset = BuildTargetManifestSerializer.Deserialize(
            BuildTargetManifestSerializer.Serialize(subset));
        Require(restoredSubset.GraphicsBackends.SequenceEqual(
                [GraphicsBackend.Direct3D11, GraphicsBackend.Vulkan]),
            "Build target manifest rejected or reordered a valid graphics backend subset.");
        var invalidRuntime = BuildTargetManifest.FromDescriptor(BuildTargetCatalog.Get("windows-x64"));
        invalidRuntime.ManagedCodeRuntime = ManagedCodeRuntimeKind.AotInterpreter;
        RequireThrows<InvalidDataException>(invalidRuntime.Validate);
        var invalidGraphics = BuildTargetManifest.FromDescriptor(BuildTargetCatalog.Get("windows-x64"));
        invalidGraphics.GraphicsBackends.Reverse();
        RequireThrows<InvalidDataException>(invalidGraphics.Validate);
        var outOfCatalogGraphics = BuildTargetManifest.FromDescriptor(
            BuildTargetCatalog.Get("windows-x64"), [GraphicsBackend.Metal]);
        RequireThrows<InvalidDataException>(outOfCatalogGraphics.Validate);
        var selection = GraphicsBackendSelector.Select(restored, GraphicsBackend.Auto, backend =>
            new GraphicsBackendSupport(
                backend,
                backend == GraphicsBackend.Vulkan
                    ? GraphicsBackendAvailability.Available
                    : GraphicsBackendAvailability.NotImplemented,
                backend == GraphicsBackend.Vulkan ? "available" : "test provider unavailable"));
        Require(selection.Selected == GraphicsBackend.Vulkan &&
                selection.Candidates[0] == GraphicsBackend.Direct3D12,
            "Automatic renderer selection ignored target policy or capability fallback.");
    }

    private static async Task VerifyVirtualAssetBundlesAsync()
    {
        var original = "virtual-content"u8.ToArray();
        var snapshot = new VirtualAssetBundleSnapshot(
            "tests",
            "virtual-1",
            [VirtualAssetBundleEntry.FromMemory("Assets/Data/value.txt", original, "TextAsset")]);
        original[0] = (byte)'X';
        await using var manager = new VirtualAssetBundleManager(snapshot);
        await manager.InitializeAsync().ConfigureAwait(false);
        Require(manager.EnumerateAddresses("Assets").SequenceEqual(["Assets/Data/value.txt"]),
            "Virtual AssetBundle root-prefix enumeration is invalid.");
        var plan = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
        Require(!plan.HasUpdates && plan.Downloads.Count == 0,
            "Virtual AssetBundles reported a remote update.");
        await using (var handle = await manager.LoadTextAsync("Assets/Data/value.txt").ConfigureAwait(false))
        {
            Require(handle.Value == "virtual-content" && manager.ActiveLeaseCount == 1,
                "Virtual AssetBundle content or lease tracking is invalid.");
        }
        Require(manager.ActiveLeaseCount == 0, "Virtual AssetBundle lease was not released.");

        var directory = Path.Combine(Path.GetTempPath(), $"BEngineVirtualBundle.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "mutable.txt");
            await File.WriteAllTextAsync(path, "first").ConfigureAwait(false);
            var fileSnapshot = new VirtualAssetBundleSnapshot(
                "tests", "virtual-2",
                [VirtualAssetBundleEntry.FromFile("Assets/Data/mutable.txt", path, "TextAsset")]);
            await using var fileManager = new VirtualAssetBundleManager(fileSnapshot);
            await fileManager.InitializeAsync().ConfigureAwait(false);
            await File.WriteAllTextAsync(path, "second").ConfigureAwait(false);
            Require(!fileManager.TryLoadBytes("Assets/Data/mutable.txt", out _),
                "Virtual AssetBundle accepted content that changed after snapshot creation.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task VerifyManagedCodeAssetBundleAsync()
    {
        const string packageName = "com.bengine.tests.managed-code";
        var root = Path.Combine(Path.GetTempPath(), $"BEngineManagedCodeBundle.{Guid.NewGuid():N}");
        try
        {
            var workspace = ProjectWorkspaceFactory.Create(Path.Combine(root, "Project"), "Managed Code Bundle");
            DeleteDefaultAotContent(workspace);
            await File.WriteAllTextAsync(workspace.PackageManifestPath, """
                format: BEngine.Packages
                version: 1
                packages: []
                """).ConfigureAwait(false);
            var sourceAssembly = Assembly.GetExecutingAssembly().Location;
            var assemblyName = Assembly.GetExecutingAssembly().GetName().Name!;
            const string buildId = "managed-code-ab-test";
            var assemblyDirectory = Path.Combine(workspace.ScriptAssembliesPath, assemblyName, buildId);
            Directory.CreateDirectory(assemblyDirectory);
            var assemblyPath = Path.Combine(assemblyDirectory, $"{assemblyName}.dll");
            File.Copy(sourceAssembly, assemblyPath);
            var sourceSymbols = Path.ChangeExtension(sourceAssembly, ".pdb");
            var symbolsPath = Path.ChangeExtension(assemblyPath, ".pdb");
            if (File.Exists(sourceSymbols)) File.Copy(sourceSymbols, symbolsPath);
            ScriptAssemblyStore.PublishProjectManifest(workspace, new ProjectScriptAssemblyManifestData
            {
                Assemblies =
                [
                    new ProjectScriptAssemblyData
                    {
                        Assembly = assemblyName,
                        BuildId = buildId,
                        RelativePath = Path.GetRelativePath(workspace.ScriptAssembliesPath, assemblyPath)
                            .Replace(Path.DirectorySeparatorChar, '/'),
                        References = []
                    }
                ]
            });

            var database = new ProjectAssetDatabase(workspace);
            _ = database.Refresh();
            var build = AssetBundleBuilder.Build(
                workspace,
                database,
                [new AssetBundleBuildDefinition { Name = "main", AssetPaths = ["Assets"] }],
                new AssetBundleBuildOptions
                {
                    PackageName = packageName,
                    Version = "managed-code-ab-1",
                    OutputDirectory = Path.Combine(root, "Bundles"),
                    IncludeHotUpdateAssemblies = true
                });
            Require(build.Catalog.Bundles.Any(bundle => bundle.Name == "bengine-hotupdate"),
                "Managed-code build did not produce the reserved HotUpdate bundle.");

            await using var manager = new AssetBundleManager(new AssetBundleRuntimeOptions
            {
                PackageName = packageName,
                CacheDirectory = Path.Combine(root, "Cache"),
                BuiltInDirectory = build.PackageDirectory
            });
            await manager.InitializeAsync().ConfigureAwait(false);
            await using var manifestHandle = await manager.LoadBytesAsync(
                ManagedCodeReleaseManifest.DefaultAddress).ConfigureAwait(false);
            var manifest = ManagedCodeReleaseManifestSerializer.Deserialize(manifestHandle.Value);
            var module = manifest.Modules.Single(item => item.Name == assemblyName);
            Require(module.BuildId == buildId && module.SymbolsAddress is not null,
                "Managed-code release manifest lost the module build id or symbols address.");
            await using var assemblyHandle = await manager.LoadBytesAsync(module.AssemblyAddress)
                .ConfigureAwait(false);
            var expectedAssembly = await File.ReadAllBytesAsync(sourceAssembly).ConfigureAwait(false);
            Require(assemblyHandle.Value.SequenceEqual(expectedAssembly),
                "Managed-code assembly read from AssetBundles differs from the compiled input.");
            await using var symbolsHandle = await manager.LoadBytesAsync(module.SymbolsAddress!)
                .ConfigureAwait(false);
            var expectedSymbols = await File.ReadAllBytesAsync(sourceSymbols).ConfigureAwait(false);
            Require(symbolsHandle.Value.SequenceEqual(expectedSymbols),
                "Managed-code symbols read from AssetBundles differ from the compiled input.");

            var capturedInputs = RuntimeManagedCodeReleaseInputCollector.Collect(workspace);
            File.Delete(symbolsPath);
            var noSymbolsBuild = AssetBundleBuilder.Build(
                workspace,
                database,
                [new AssetBundleBuildDefinition { Name = "main", AssetPaths = ["Assets"] }],
                new AssetBundleBuildOptions
                {
                    PackageName = packageName,
                    Version = "managed-code-ab-2-no-symbols",
                    OutputDirectory = Path.Combine(root, "Bundles"),
                    IncludeHotUpdateAssemblies = true,
                    IncludeManagedSymbols = false,
                    ReleaseInputs = capturedInputs
                });
            Require(!noSymbolsBuild.Catalog.Assets.Any(asset =>
                        asset.AssetType.Equals("ManagedSymbols", StringComparison.Ordinal) ||
                        asset.Address.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)),
                "A symbols-disabled managed-code build still included a PDB asset.");

            await using var noSymbolsManager = new AssetBundleManager(new AssetBundleRuntimeOptions
            {
                PackageName = packageName,
                CacheDirectory = Path.Combine(root, "CacheNoSymbols"),
                BuiltInDirectory = noSymbolsBuild.PackageDirectory
            });
            await noSymbolsManager.InitializeAsync().ConfigureAwait(false);
            await using var noSymbolsManifestHandle = await noSymbolsManager.LoadBytesAsync(
                ManagedCodeReleaseManifest.DefaultAddress).ConfigureAwait(false);
            var noSymbolsManifest = ManagedCodeReleaseManifestSerializer.Deserialize(
                noSymbolsManifestHandle.Value);
            var noSymbolsModule = noSymbolsManifest.Modules.Single(item => item.Name == assemblyName);
            Require(noSymbolsModule.SymbolsAddress is null,
                "A symbols-disabled managed-code release retained a symbols address.");
            await using var noSymbolsAssemblyHandle = await noSymbolsManager.LoadBytesAsync(
                noSymbolsModule.AssemblyAddress).ConfigureAwait(false);
            Require(noSymbolsAssemblyHandle.Value.SequenceEqual(expectedAssembly),
                "Disabling symbols removed or changed the managed assembly payload.");
        }
        finally
        {
            TryDeleteTestDirectory(root);
        }
    }

    private static async Task VerifyActivationTransactionAsync()
    {
        var events = new List<string>();
        var runtime = new FakeRuntime(
            new ProbeModule("base", [], events),
            new ProbeModule("game", ["base"], events));
        var gate = new HotUpdateActivationGate(new SingleRuntimeFactory(runtime), ["platform:Windows"]);
        var release = new ManagedCodeRelease(
            "transaction-1", [new ManagedCodeModule("fake", "1", [1])]);
        await using var prepared = await gate.PrepareAsync(
            release, new ManagedCodeRuntimeRequest(ManagedCodeRuntimeKind.CoreClr)).ConfigureAwait(false);
        await using (var domain = await prepared.ActivateAsync(EmptyServices.Instance).ConfigureAwait(false))
        {
            Require(events.SequenceEqual(["configure:base", "configure:game", "start:base", "start:game"]),
                "HotUpdate module dependency order is invalid.");
            domain.Services.Export<IProbeService>(new ProbeService("first"),
                HotServiceReplacementPolicy.Replaceable);
            domain.Services.Export<IProbeService>(new ProbeService("second"),
                HotServiceReplacementPolicy.Replaceable);
            Require(domain.Services.GetService(typeof(IProbeService)) is IProbeService { Value: "second" },
                "Replaceable HotUpdate service did not replace its previous implementation.");
        }
        Require(events.TakeLast(2).SequenceEqual(["stop:game", "stop:base"]),
            "HotUpdate modules did not stop in reverse dependency order.");

        events.Clear();
        var failingRuntime = new FakeRuntime(
            new ProbeModule("base", [], events),
            new ProbeModule("failure", ["base"], events, failStart: true));
        var failingGate = new HotUpdateActivationGate(new SingleRuntimeFactory(failingRuntime));
        await using var failingPrepared = await failingGate.PrepareAsync(
            release, new ManagedCodeRuntimeRequest(ManagedCodeRuntimeKind.CoreClr)).ConfigureAwait(false);
        await RequireThrowsAsync<InvalidOperationException>(async () =>
            await failingPrepared.ActivateAsync(EmptyServices.Instance).ConfigureAwait(false));
        Require(events.Contains("stop:base") && failingRuntime.Disposed,
            "Failed HotUpdate activation did not roll back started modules and runtime state.");
    }

    private static async Task VerifyCoreClrRuntimeAsync()
    {
        var path = Assembly.GetExecutingAssembly().Location;
        var image = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
        var moduleName = Assembly.GetExecutingAssembly().GetName().Name!;
        var release = new ManagedCodeRelease(
            "coreclr-1", [new ManagedCodeModule(moduleName, "test", image)]);
        var gate = new HotUpdateActivationGate(
            new ProviderRuntimeFactory(new CoreClrManagedCodeRuntimeProvider()));
        await using var prepared = await gate.PrepareAsync(
            release, new ManagedCodeRuntimeRequest(ManagedCodeRuntimeKind.CoreClr)).ConfigureAwait(false);
        Require(prepared.RuntimeKind == ManagedCodeRuntimeKind.CoreClr && prepared.Assemblies.Count == 1,
            "CoreCLR runtime did not load the managed-code assembly into its HotUpdate domain.");
        var loadedAssembly = prepared.Assemblies.Single();
        var services = new ServiceCollection();
        services.AddBEngine(
            new EngineServiceContext(EngineHostKind.Player, AppContext.BaseDirectory),
            prepared.Assemblies);
        Require(services.Any(descriptor =>
                descriptor.ServiceType.FullName == typeof(LoadedEngineRegistrationMarker).FullName),
            "HotUpdate IEngineServiceModule was not configured from the prepared release assemblies.");
        await using (var domain = await prepared.ActivateAsync(EmptyServices.Instance).ConfigureAwait(false))
            Require(domain.Release.ReleaseId == "coreclr-1",
                "CoreCLR HotUpdate domain activated the wrong release.");
        Require(RuntimeTypeCache.GetTypes(loadedAssembly).Length == 0,
            "Disposed HotUpdate domain remained rooted in RuntimeTypeCache.");
    }

    private static async Task VerifyAotInterpreterRuntimeAsync()
    {
        AppContext.SetSwitch(AotInterpreterRuntimeHost.EnabledSwitchName, false);
        var disabledProvider = new AotInterpreterManagedCodeRuntimeProvider();
        Require(!disabledProvider.IsAvailable &&
                disabledProvider.UnavailableReason?.Contains("platform host", StringComparison.OrdinalIgnoreCase) == true,
            "AOT interpreter provider became available without an interpreter-enabled platform host.");

        var eventsPath = Path.Combine(Path.GetTempPath(), $"BEngineAotInterpreter.{Guid.NewGuid():N}.events");
        Environment.SetEnvironmentVariable(AotLifecycleModule.EventPathVariable, eventsPath);
        try
        {
            AotInterpreterRuntimeHost.DeclareInterpreterEnabled();
            var provider = new AotInterpreterManagedCodeRuntimeProvider();
            Require(provider.IsAvailable, "Interpreter-enabled host did not expose the AOT runtime provider.");
            var path = Assembly.GetExecutingAssembly().Location;
            var image = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            var moduleName = Assembly.GetExecutingAssembly().GetName().Name!;
            var release = new ManagedCodeRelease(
                "aot-interpreter-1", [new ManagedCodeModule(moduleName, "test", image)]);
            var gate = new HotUpdateActivationGate(new ProviderRuntimeFactory(provider));
            await using var prepared = await gate.PrepareAsync(
                release, new ManagedCodeRuntimeRequest(ManagedCodeRuntimeKind.AotInterpreter)).ConfigureAwait(false);
            Require(prepared.RuntimeKind == ManagedCodeRuntimeKind.AotInterpreter && prepared.Assemblies.Count == 1,
                "AOT interpreter provider did not load the downloaded C# assembly image.");
            await using (var domain = await prepared.ActivateAsync(EmptyServices.Instance).ConfigureAwait(false))
                Require(domain.Release.ReleaseId == "aot-interpreter-1",
                    "AOT interpreter HotUpdate domain activated the wrong release.");

            var events = await File.ReadAllLinesAsync(eventsPath).ConfigureAwait(false);
            Require(events.SequenceEqual(["configure", "start", "stop"]),
                "AOT interpreter provider did not execute the HotUpdate module lifecycle.");
        }
        finally
        {
            AppContext.SetSwitch(AotInterpreterRuntimeHost.EnabledSwitchName, false);
            Environment.SetEnvironmentVariable(AotLifecycleModule.EventPathVariable, null);
            if (File.Exists(eventsPath)) File.Delete(eventsPath);
        }
    }

    private static void VerifyBuildProviderBoundaries()
    {
        Require(PlayerBuildPipeline.CanBuild("windows-x64", out _),
            "Built-in desktop build provider does not recognize Windows x64.");
        var providerIds = PlayerBuildPipeline.registeredProviders
            .Select(provider => provider.ProviderId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Require(providerIds.Contains("bengine.dotnet-android") &&
                providerIds.Contains("bengine.dotnet-ios") &&
                providerIds.Contains("bengine.dotnet-webassembly"),
            "One or more platform build providers were not registered.");
        var androidPrerequisites = PlayerBuildPipeline.GetPrerequisites("android-arm64");
        Require(androidPrerequisites.Any(item => item.Id == "android-workload") &&
                androidPrerequisites.Any(item => item.Id == "android-hybrid-aot-interpreter") &&
                androidPrerequisites.Any(item => item.Id == "platform-renderer"),
            "Android build prerequisites do not expose SDK, hybrid runtime, and renderer boundaries.");
        Require(!PlayerBuildPipeline.CanBuild("android-arm64", out var reason) &&
                reason.Contains("Core AOT", StringComparison.OrdinalIgnoreCase) &&
                reason.Contains("renderer", StringComparison.OrdinalIgnoreCase),
            "Android incorrectly reports a working Player without hybrid runtime and renderer capabilities.");
        Require(!PlayerBuildPipeline.CanBuild("ios-arm64", out _) &&
                !PlayerBuildPipeline.CanBuild("web-wasm", out _),
            "A platform without a registered runnable renderer incorrectly reports build readiness.");
        Require(PlayerBuildPipeline.GetPrerequisites("ios-arm64")
                    .Any(item => item.Id == "ios-interpreter-bridge") &&
                PlayerBuildPipeline.GetPrerequisites("web-wasm")
                    .Any(item => item.Id == "wasm-workload"),
            "iOS/Web prerequisites omit their interpreter bridge or WebAssembly toolchain boundary.");

        var hostRoot = Path.Combine(FindRepositoryRoot(), "src", "Core", "BEngine.Player.Hosts");
        foreach (var hostProject in new[]
                 {
                     Path.Combine(hostRoot, "Android", "BEngine.Player.AndroidHost.csproj"),
                     Path.Combine(hostRoot, "iOS", "BEngine.Player.iOSHost.csproj"),
                     Path.Combine(hostRoot, "Web", "BEngine.Player.WebHost.csproj")
                 })
            Require(!XDocument.Load(hostProject).Descendants()
                    .SelectMany(static element => element.Attributes())
                    .Where(static attribute => attribute.Name.LocalName == "Include")
                    .Any(attribute => attribute.Value.Replace('\\', '/').EndsWith(
                        $"/{PlayerBootstrapManifest.FileName}", StringComparison.OrdinalIgnoreCase)),
                $"Platform host still packages the removed loose Player bootstrap: '{hostProject}'.");
    }

    private static void VerifyDynamicManagedCodeTrimmerDescriptor()
    {
        var root = Path.Combine(Path.GetTempPath(), $"BEngineTrimmerRoots.{Guid.NewGuid():N}");
        try
        {
            var workspace = ProjectWorkspaceFactory.Create(Path.Combine(root, "Project"), "Trimmer Roots");
            Require(ProjectScriptCompiler.CompileAndLoad(workspace) is not null,
                "The trimmer-root fixture did not compile its runtime assemblies.");
            var releaseInputs = RuntimeManagedCodeReleaseInputCollector.Collect(workspace);
            var dynamicNames = releaseInputs.Assemblies
                .Select(static assembly => assembly.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Require(dynamicNames.Contains("AOT") && dynamicNames.Contains("BEngine.UIElements"),
                "The trimmer-root fixture is missing its AOT or UIElements runtime assembly.");

            var descriptorPath = DynamicManagedCodeTrimmerDescriptor.Write(
                Path.Combine(root, "dynamic-dependencies.xml"),
                releaseInputs.Assemblies.Select(static assembly => assembly.AssemblyPath));
            var assemblies = XDocument.Load(descriptorPath).Root?.Elements("assembly").ToArray() ?? [];
            var roots = assemblies
                .Select(static element => (string?)element.Attribute("fullname") ?? string.Empty)
                .ToArray();
            Require(roots.Contains("System.Xml.XDocument", StringComparer.OrdinalIgnoreCase),
                "The dynamic trimmer descriptor omitted UIElements' System.Xml.XDocument dependency.");
            Require(roots.Contains("System.Xml.XDocument", StringComparer.OrdinalIgnoreCase) &&
                    roots.Contains("System.Private.Xml.Linq", StringComparer.OrdinalIgnoreCase) &&
                    roots.Contains("System.Runtime", StringComparer.OrdinalIgnoreCase) &&
                    roots.Contains("System.Private.CoreLib", StringComparer.OrdinalIgnoreCase),
                "The dynamic trimmer descriptor did not preserve facade and implementation assembly pairs.");
            Require(!roots.Contains("AOT", StringComparer.OrdinalIgnoreCase) &&
                    !roots.Contains("BEngine.UIElements", StringComparer.OrdinalIgnoreCase) &&
                    !roots.Contains("BEngine", StringComparer.OrdinalIgnoreCase),
                "The dynamic trimmer descriptor incorrectly rooted a dynamic or statically rooted engine assembly.");
            Require(assemblies.All(static element =>
                        string.Equals((string?)element.Attribute("preserve"), "all", StringComparison.Ordinal)) &&
                    roots.SequenceEqual(roots.Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(static name => name, StringComparer.Ordinal)),
                "The dynamic trimmer descriptor is not deterministic or does not fully preserve each dependency.");
        }
        finally
        {
            TryDeleteTestDirectory(root);
        }
    }

    private static void VerifyTargetShaderResourceFiltering()
    {
        var root = Path.Combine(Path.GetTempPath(), "BEngineShaderFilter", "Resources");
        RuntimePackageResourceInput Resource(string relativePath) => new(
            "com.bengine.tests.shader-filter",
            $"Assets/Packages/com.bengine.tests.shader-filter/Resources/{relativePath.Replace('\\', '/')}",
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)),
            new string('0', 64),
            1);

        var source = new RuntimeManagedCodeReleaseInputSet(
            [],
            [
                Resource("Shaders/Test/Solid.direct3d.frag.hlsl"),
                Resource("Shaders/Test/Solid.vulkan.frag.glsl"),
                Resource("Shaders/Test/Solid.opengl.frag.glsl"),
                Resource("Shaders/Test/Solid.webgpu.wgsl"),
                Resource("Shaders/Test/Solid.metal.msl"),
                Resource("Shaders/Test/Generic.wgsl"),
                Resource("Data/runtime.webgpu.json")
            ],
            [root]);
        var filtered = PlayerContentBuildPipeline.FilterPackageResourcesForBackends(
            source,
            new HashSet<GraphicsBackend>
            {
                GraphicsBackend.Direct3D12,
                GraphicsBackend.Direct3D11,
                GraphicsBackend.Vulkan,
                GraphicsBackend.OpenGL
            });
        var names = filtered.PackageResources
            .Select(static resource => Path.GetFileName(resource.FilePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Require(names.Contains("Solid.direct3d.frag.hlsl") &&
                names.Contains("Solid.vulkan.frag.glsl") &&
                names.Contains("Solid.opengl.frag.glsl") &&
                names.Contains("Generic.wgsl") &&
                names.Contains("runtime.webgpu.json") &&
                !names.Contains("Solid.webgpu.wgsl") &&
                !names.Contains("Solid.metal.msl") &&
                filtered.PackageResourceRoots.SequenceEqual([root]),
            "Target Shader filtering did not preserve Windows fallbacks or prune unsupported marked Shaders.");
        RequireThrows<InvalidDataException>(() =>
            PlayerContentBuildPipeline.FilterPackageResourcesForBackends(
                source, new HashSet<GraphicsBackend> { GraphicsBackend.Auto }));
    }

    private static async Task VerifyDesktopPlayerBuildAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"BEnginePlayerBuild.{Guid.NewGuid():N}");
        try
        {
            var workspace = ProjectWorkspaceFactory.Create(Path.Combine(root, "Project"), "Player Build");
            RequireThrows<InvalidOperationException>(() =>
                PlayerBuildMenuCommands.ValidateOutputLocation(
                    workspace.RootPath,
                    workspace.AssetsPath,
                    Path.Combine(workspace.AssetsPath, workspace.Project.Name)));
            var fixture = await CreatePlayerContentFixtureAsync(workspace).ConfigureAwait(false);
            Require(ProjectScriptCompiler.CompileAndLoad(workspace) is not null,
                "The project C# compiler did not emit the runtime assembly before packaging.");
            var projectAssemblies = ScriptAssemblyStore.LoadProjectManifest(workspace)?.Assemblies
                .Select(assembly => assembly.Assembly)
                .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            Require(projectAssemblies.Count != 0,
                "The project C# compiler did not publish a runtime assembly manifest.");
            var releaseAssemblies = RuntimeManagedCodeReleaseInputCollector.Collect(workspace).Assemblies
                .Select(assembly => assembly.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Require(releaseAssemblies.Contains("BEngine.Tests.ReleaseInputPackage"),
                "The packaged fixture release graph omitted its package runtime assembly.");
            var output = Path.Combine(root, "player");
            var target = BuildTargetCatalog.InferCurrentDesktop();
            var result = await PlayerBuildPipeline.BuildAsync(new PlayerBuildRequest
            {
                ProjectPath = workspace.RootPath,
                OutputDirectory = output,
                TargetId = target.TargetId,
                SelfContained = false,
                IncludeDebugSymbols = false,
                Scenes = [fixture.AotScene],
                PlayerHostProjectPath = Path.Combine(
                    FindRepositoryRoot(), "src", "Core", "BEngine.Player", "BEngine.Player.csproj")
            }).ConfigureAwait(false);
            Require(result.ContentVersion.StartsWith("content-", StringComparison.Ordinal),
                "Desktop Player package is missing its content version.");
            await AssertHotUpdateContentAsync(
                    workspace, target, releaseAssemblies, fixture, result.ContentVersion, root)
                .ConfigureAwait(false);
            var runtimeRoot = await AssertPackagedPlayerLayoutAsync(
                output, workspace.Project.Name, target, releaseAssemblies, fixture, root)
                .ConfigureAwait(false);
            var packagedPlayerResources = BuiltInResourceArchive.Open(Path.Combine(
                runtimeRoot,
                PlayerPackagedResourceAddresses.ResourcesDirectoryName,
                PlayerPackagedResourceAddresses.PlayerArchiveFileName));
            var packagedTarget = BuildTargetManifestSerializer.Deserialize(
                packagedPlayerResources.ReadBytes(PlayerPackagedResourceAddresses.BuildTargetManifest));
            Require(packagedTarget.TargetId == target.TargetId,
                "Desktop Player package contains the wrong build-target manifest.");
            var packagedServices = new ServiceCollection().AddBEnginePlayer(
                runtimeRoot, Path.Combine(root, "player-cache"));
            using (var packagedProvider = packagedServices.BuildServiceProvider())
            {
                Require(!packagedServices.Any(descriptor => descriptor.ServiceType.FullName ==
                            "BEngine.Player.PlayerHotUpdateSession"),
                    "The AOT-only Player package preloaded a remote game HotUpdate session.");
                _ = packagedProvider.GetRequiredService<IAssetBundleManager>();
                _ = packagedProvider.GetRequiredService<IContentBootstrapper>();
            }

            var executable = Path.Combine(output, PlayerBuildLayout.SanitizePlayerName(workspace.Project.Name) +
                (target.Platform == BuildTargetPlatform.Windows ? ".exe" : string.Empty));
            var originalExecutable = await File.ReadAllBytesAsync(executable).ConfigureAwait(false);
            var replacementRequest = new PlayerBuildRequest
            {
                ProjectPath = workspace.RootPath,
                OutputDirectory = output,
                TargetId = target.TargetId,
                SelfContained = false,
                IncludeDebugSymbols = false,
                ReplaceExisting = true,
                Scenes = [fixture.AotScene]
            };
            PlayerBuildPipeline.RegisterProvider(
                new TransactionProbeBuildTargetProvider(fail: true), replace: true);
            try
            {
                await RequireThrowsAsync<InvalidOperationException>(() =>
                    PlayerBuildPipeline.BuildAsync(replacementRequest)).ConfigureAwait(false);
                Require(File.Exists(executable) &&
                        originalExecutable.AsSpan().SequenceEqual(File.ReadAllBytes(executable)),
                    "A failed replacement build changed or deleted the last valid Player.");

                PlayerBuildPipeline.RegisterProvider(
                    new TransactionProbeBuildTargetProvider(fail: false), replace: true);
                _ = await PlayerBuildPipeline.BuildAsync(replacementRequest).ConfigureAwait(false);
                Require(File.Exists(Path.Combine(output, TransactionProbeBuildTargetProvider.MarkerFile)) &&
                        !File.Exists(executable) &&
                        !Directory.EnumerateDirectories(root, ".player.*.backup").Any(),
                    "A successful replacement did not atomically publish staging or clean its backup.");
            }
            finally
            {
                PlayerBuildPipeline.RegisterProvider(new DotNetDesktopBuildTargetProvider(), replace: true);
            }
        }
        finally
        {
            TryDeleteTestDirectory(root);
        }
    }

    private static async Task VerifyExportedDesktopBuildHostAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"BEngineExportedBuildHost.{Guid.NewGuid():N}");
        try
        {
            var repository = FindRepositoryRoot();
            var engine = Path.Combine(root, "Engine");
            var buildRuntime = Path.Combine(engine, "BuildRuntime");
            Directory.CreateDirectory(engine);
            Directory.CreateDirectory(buildRuntime);
            await RunProcessAsync("dotnet",
            [
                "publish",
                Path.Combine(repository, "src", "Core", "BEngine.Player", "BEngine.Player.csproj"),
                "--configuration", "Debug",
                "--no-restore",
                "--output", buildRuntime,
                "--nologo",
                "-m:1"
            ], repository).ConfigureAwait(false);
            CopyTestDirectory(
                Path.Combine(repository, "src", "Core", "BEngine.Player.Hosts"),
                Path.Combine(engine, "BuildHosts"));
            File.Copy(typeof(AssetBundleBuilder).Assembly.Location,
                Path.Combine(engine, "BEngine.Editor.dll"));

            var workspace = ProjectWorkspaceFactory.Create(Path.Combine(root, "Project"), "Exported Host");
            var fixture = await CreatePlayerContentFixtureAsync(workspace).ConfigureAwait(false);
            Require(ProjectScriptCompiler.CompileAndLoad(workspace) is not null,
                "The exported-host fixture did not compile its managed game module.");
            var releaseAssemblies = RuntimeManagedCodeReleaseInputCollector.Collect(workspace).Assemblies
                .Select(assembly => assembly.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var output = Path.Combine(root, "player");
            var target = BuildTargetCatalog.InferCurrentDesktop();
            await PlayerBuildPipeline.BuildAsync(new PlayerBuildRequest
            {
                ProjectPath = workspace.RootPath,
                OutputDirectory = output,
                TargetId = target.TargetId,
                SelfContained = true,
                IncludeDebugSymbols = false,
                ManagedStripping = PlayerManagedStrippingLevel.Conservative,
                Scenes = [fixture.AotScene],
                PlayerHostProjectPath = Path.Combine(
                    engine, "BuildHosts", "Desktop", "BEngine.Player.DesktopHost.csproj"),
                AdditionalMsBuildProperties = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["JsonSerializerIsReflectionEnabledByDefault"] = "false"
                }
            }).ConfigureAwait(false);
            _ = await AssertPackagedPlayerLayoutAsync(
                output, workspace.Project.Name, target, releaseAssemblies, fixture, root)
                .ConfigureAwait(false);
            if (OperatingSystem.IsWindows())
                await AssertPackagedPlayerWindowStartsAsync(
                    Path.Combine(output,
                        PlayerBuildLayout.SanitizePlayerName(workspace.Project.Name) + ".exe"),
                    output).ConfigureAwait(false);
        }
        finally { TryDeleteTestDirectory(root); }
    }

    private static async Task AssertPackagedPlayerWindowStartsAsync(
        string executable,
        string workingDirectory)
    {
        AssertWindowsGuiSubsystem(executable);
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        var unavailableRuntime = Path.Combine(workingDirectory, ".missing-dotnet-runtime");
        start.Environment["DOTNET_ROOT"] = unavailableRuntime;
        start.Environment["DOTNET_ROOT_X64"] = unavailableRuntime;
        start.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        start.Environment["PATH"] = Environment.SystemDirectory;
        start.Environment[BEnginePlayer.PersistentDataPathEnvironmentVariable] =
            Path.Combine(workingDirectory, ".smoke-persistent-data");
        start.Environment[BEnginePlayer.NativeDiagnosticsEnvironmentVariable] = "1";
        using var process = Process.Start(start) ??
                            throw new InvalidOperationException($"Could not start '{executable}'.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        var exited = await Task.WhenAny(
                process.WaitForExitAsync(), Task.Delay(TimeSpan.FromSeconds(3)))
            .ConfigureAwait(false);
        if (exited.IsCompletedSuccessfully && process.HasExited)
        {
            var combined = await output.ConfigureAwait(false) + await error.ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Packaged Player exited during the window startup smoke test ({process.ExitCode}).\n{combined}");
        }

        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync().ConfigureAwait(false);
        _ = await output.ConfigureAwait(false);
        _ = await error.ConfigureAwait(false);
    }

    private static async Task AssertHotUpdateContentAsync(
        ProjectWorkspace workspace,
        BuildTargetDescriptor target,
        IReadOnlySet<string> releaseAssemblies,
        PlayerContentFixture fixture,
        string builtInVersion,
        string root)
    {
        var output = Path.Combine(root, "remotecontent");
        PlayerBuildRequest CreateRequest(string version) => new()
        {
            ProjectPath = workspace.RootPath,
            TargetId = target.TargetId,
            IncludeDebugSymbols = false,
            HotResourceVersion = version,
            Scenes = [fixture.AotScene]
        };

        var result = await PlayerContentUpdatePipeline.BuildAsync(CreateRequest("v1"), output)
            .ConfigureAwait(false);
        Require(result.ContentVersion == "v1" && result.ContentVersion != builtInVersion &&
                result.LatestVersion == "v1" && result.Promoted,
            "The remote Hot Resources did not publish under their explicit v1 identity.");

        var packageDirectory = Directory.EnumerateDirectories(output).Single();
        var packageName = Path.GetFileName(packageDirectory);
        var versionOneDirectory = Path.Combine(packageDirectory, "v1");
        var latestBytes = await File.ReadAllBytesAsync(Path.Combine(packageDirectory, "latest.json"))
            .ConfigureAwait(false);
        var latest = AssetBundleCatalogSerializer.DeserializeLatestPointer(latestBytes);
        var versionOneBytes = await File.ReadAllBytesAsync(
            Path.Combine(versionOneDirectory, "version.json")).ConfigureAwait(false);
        var versionOne = AssetBundleCatalogSerializer.DeserializeVersion(
            versionOneBytes);
            Require(Directory.Exists(versionOneDirectory) && latest.Version == "v1" &&
                versionOne.Version == "v1" &&
                latestBytes.AsSpan().SequenceEqual(CreateLatestPointerBytes(packageName, "v1")) &&
                !latestBytes.AsSpan().SequenceEqual(versionOneBytes),
            "The v1 directory, version-only latest pointer, and immutable version metadata are inconsistent.");
        await using var manager = new AssetBundleManager(new AssetBundleRuntimeOptions
        {
            PackageName = packageName,
            CacheDirectory = Path.Combine(root, $"RemoteCache-{Guid.NewGuid():N}"),
            BuiltInDirectory = packageDirectory
        });
        await manager.InitializeAsync().ConfigureAwait(false);
        Require(manager.ActiveVersion?.Version == "v1",
            "The runtime update target did not preserve the visible v1 version.");
        var addresses = manager.EnumerateAddresses().ToHashSet(StringComparer.OrdinalIgnoreCase);
        Require(addresses.Contains(fixture.SelectedScene) &&
                addresses.Contains(fixture.ReferencedSprite) &&
                addresses.Contains(fixture.DynamicResource) &&
                fixture.ExcludedAssets.All(addresses.Contains),
            "The remote catalog does not cover all non-AOT project content.");
        Require(!addresses.Contains(fixture.AotScene) &&
                !addresses.Contains(fixture.AotResource) &&
                !addresses.Any(path => path.StartsWith(
                    PlayerContentBuildPipeline.AotAssetRoot + "/", StringComparison.OrdinalIgnoreCase)) &&
                !addresses.Contains("Assets/__BEngine/HotUpdate/AOT.dll"),
            "The remote catalog contains built-in AOT assets or AOT.dll.");
        Require(addresses.Contains(ManagedCodeReleaseManifest.DefaultAddress) &&
                addresses.Contains(
                    "Assets/Packages/com.bengine.tests.release-input/Resources/Data/value.txt"),
            "The remote catalog omitted managed code or package runtime resources.");
        if (target.Platform == BuildTargetPlatform.Windows)
            AssertWindowsPackageShaderResources(
                addresses, "remote hot-update catalog", requireCompleteFallbackSet: false);
        await using var manifestHandle = await manager.LoadBytesAsync(
            ManagedCodeReleaseManifest.DefaultAddress).ConfigureAwait(false);
        var release = ManagedCodeReleaseManifestSerializer.Deserialize(manifestHandle.Value);
        var expectedAssemblies = releaseAssemblies
            .Where(name => !name.Equals(
                PlayerContentBuildPipeline.AotAssemblyName, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Require(expectedAssemblies.SetEquals(release.Modules.Select(module => module.Name)),
            "The remote managed release is not the complete non-AOT assembly graph.");

        var dynamicResourcePath = workspace.ResolveInside(fixture.DynamicResource);
        var versionOneResource = await File.ReadAllTextAsync(dynamicResourcePath).ConfigureAwait(false);
        await File.WriteAllTextAsync(dynamicResourcePath, "dynamic-resource-v2").ConfigureAwait(false);
        await RequireThrowsAsync<InvalidDataException>(() =>
            PlayerContentUpdatePipeline.BuildAsync(CreateRequest("v1"), output)).ConfigureAwait(false);
        var versionTwoResult = await PlayerContentUpdatePipeline.BuildAsync(
            CreateRequest("v2"), output).ConfigureAwait(false);
        latest = AssetBundleCatalogSerializer.DeserializeLatestPointer(
            await File.ReadAllBytesAsync(Path.Combine(packageDirectory, "latest.json"))
                .ConfigureAwait(false));
        Require(versionTwoResult.ContentVersion == "v2" && versionTwoResult.LatestVersion == "v2" &&
                versionTwoResult.Promoted && latest.Version == "v2" &&
                Directory.Exists(Path.Combine(packageDirectory, "v2")),
            "Changed content was not rejected under v1 and published as visible version v2.");

        var versionTwoLatestBytes = await File.ReadAllBytesAsync(
            Path.Combine(packageDirectory, "latest.json")).ConfigureAwait(false);
        var versionTwoDirectory = Path.Combine(packageDirectory, "v2");
        var versionTwoMetadataBytes = await File.ReadAllBytesAsync(
            Path.Combine(versionTwoDirectory, "version.json")).ConfigureAwait(false);
        await File.WriteAllBytesAsync(
            Path.Combine(packageDirectory, "latest.json"), versionTwoMetadataBytes).ConfigureAwait(false);
        await File.WriteAllTextAsync(dynamicResourcePath, versionOneResource).ConfigureAwait(false);
        var downgradeResult = await PlayerContentUpdatePipeline.BuildAsync(CreateRequest("v1"), output)
            .ConfigureAwait(false);
        var latestAfterDowngrade = await File.ReadAllBytesAsync(
            Path.Combine(packageDirectory, "latest.json")).ConfigureAwait(false);
        Require(downgradeResult.ContentVersion == "v1" && downgradeResult.LatestVersion == "v2" &&
                !downgradeResult.Promoted &&
                versionTwoLatestBytes.AsSpan().SequenceEqual(latestAfterDowngrade) &&
                AssetBundleCatalogSerializer.DeserializeLatestPointer(latestAfterDowngrade).Version == "v2",
            "Rebuilding immutable v1 after legacy v2 latest metadata did not preserve v2 as a minimal pointer.");

        var immutableState = CaptureVersionDirectoryState(
            versionOneDirectory, versionTwoDirectory);
        PlayerContentUpdatePipeline.SetLatestVersion(packageDirectory, "v1");
        var switchedToVersionOne = await File.ReadAllBytesAsync(
            Path.Combine(packageDirectory, "latest.json")).ConfigureAwait(false);
        Require(switchedToVersionOne.AsSpan().SequenceEqual(
                    CreateLatestPointerBytes(packageName, "v1")) &&
                AssetBundleCatalogSerializer.DeserializeLatestPointer(switchedToVersionOne).Version == "v1" &&
                !switchedToVersionOne.AsSpan().SequenceEqual(versionOneBytes),
            "Explicit latest switching did not atomically write the minimal v1 pointer.");

        PlayerContentUpdatePipeline.SetLatestVersion(packageDirectory, "v2");
        var switchedBackToVersionTwo = await File.ReadAllBytesAsync(
            Path.Combine(packageDirectory, "latest.json")).ConfigureAwait(false);
        Require(switchedBackToVersionTwo.AsSpan().SequenceEqual(versionTwoLatestBytes) &&
                AssetBundleCatalogSerializer.DeserializeLatestPointer(switchedBackToVersionTwo).Version == "v2" &&
                immutableState == CaptureVersionDirectoryState(
                    versionOneDirectory, versionTwoDirectory) &&
                !Directory.EnumerateFiles(packageDirectory, ".latest.*.tmp").Any(),
            "Explicit v2 -> v1 -> v2 switching changed an immutable version directory or left staging files.");

        RequireThrows<DirectoryNotFoundException>(() =>
            PlayerContentUpdatePipeline.SetLatestVersion(packageDirectory, "v3"));
        Require((await File.ReadAllBytesAsync(Path.Combine(packageDirectory, "latest.json"))
                    .ConfigureAwait(false)).AsSpan().SequenceEqual(versionTwoLatestBytes),
            "Selecting a missing content version changed latest.json.");

        var corruptVersionDirectory = Path.Combine(packageDirectory, "v3");
        Directory.CreateDirectory(corruptVersionDirectory);
        try
        {
            var mismatchedCatalogBytes = AssetBundleCatalogSerializer.SerializeCatalog(
                new AssetBundleCatalog
                {
                    PackageName = packageName,
                    Version = "v4"
                });
            await File.WriteAllBytesAsync(
                Path.Combine(corruptVersionDirectory, "catalog.json"), mismatchedCatalogBytes)
                .ConfigureAwait(false);
            var corruptVersionBytes = AssetBundleCatalogSerializer.SerializeVersion(
                new AssetBundleVersion
                {
                    PackageName = "com.bengine.tests.wrong-package",
                    Version = "v3",
                    CatalogFile = "catalog.json",
                    CatalogSha256 = AssetBundleCatalogSerializer.ComputeSha256(mismatchedCatalogBytes),
                    CatalogSize = mismatchedCatalogBytes.LongLength
                });
            await File.WriteAllBytesAsync(
                Path.Combine(corruptVersionDirectory, "version.json"), corruptVersionBytes)
                .ConfigureAwait(false);
            RequireThrows<InvalidDataException>(() =>
                PlayerContentUpdatePipeline.SetLatestVersion(packageDirectory, "v3"));
            Require((await File.ReadAllBytesAsync(Path.Combine(packageDirectory, "latest.json"))
                        .ConfigureAwait(false)).AsSpan().SequenceEqual(versionTwoLatestBytes),
                "Selecting content for another package changed latest.json.");

            corruptVersionBytes = AssetBundleCatalogSerializer.SerializeVersion(
                new AssetBundleVersion
                {
                    PackageName = packageName,
                    Version = "v3",
                    CatalogFile = "catalog.json",
                    CatalogSha256 = AssetBundleCatalogSerializer.ComputeSha256(mismatchedCatalogBytes),
                    CatalogSize = mismatchedCatalogBytes.LongLength
                });
            await File.WriteAllBytesAsync(
                Path.Combine(corruptVersionDirectory, "version.json"), corruptVersionBytes)
                .ConfigureAwait(false);
            RequireThrows<InvalidDataException>(() =>
                PlayerContentUpdatePipeline.SetLatestVersion(packageDirectory, "v3"));
            Require((await File.ReadAllBytesAsync(Path.Combine(packageDirectory, "latest.json"))
                        .ConfigureAwait(false)).AsSpan().SequenceEqual(versionTwoLatestBytes) &&
                    immutableState == CaptureVersionDirectoryState(
                        versionOneDirectory, versionTwoDirectory),
                "Selecting content with mismatched catalog metadata changed latest or an immutable release.");

            corruptVersionBytes = AssetBundleCatalogSerializer.SerializeVersion(
                new AssetBundleVersion
                {
                    PackageName = packageName,
                    Version = "v3",
                    CatalogFile = "catalog.json",
                    CatalogSha256 = new string('0', 64),
                    CatalogSize = mismatchedCatalogBytes.LongLength
                });
            await File.WriteAllBytesAsync(
                Path.Combine(corruptVersionDirectory, "version.json"), corruptVersionBytes)
                .ConfigureAwait(false);
            RequireThrows<InvalidDataException>(() =>
                PlayerContentUpdatePipeline.SetLatestVersion(packageDirectory, "v3"));
            Require((await File.ReadAllBytesAsync(Path.Combine(packageDirectory, "latest.json"))
                        .ConfigureAwait(false)).AsSpan().SequenceEqual(versionTwoLatestBytes),
                "Selecting content with a corrupt catalog hash changed latest.json.");
        }
        finally
        {
            Directory.Delete(corruptVersionDirectory, recursive: true);
        }
        Require(Directory.EnumerateFileSystemEntries(output)
                    .Select(Path.GetFileName)
                    .SequenceEqual(new[] { packageName }, StringComparer.Ordinal),
            "The Hot Resource output must contain exactly its published package directory.");
    }

    private static string CaptureVersionDirectoryState(params string[] directories) =>
        string.Join('\n', directories
            .SelectMany(directory => Directory.EnumerateFiles(
                    directory, "*", SearchOption.AllDirectories)
                .Select(path => new
                {
                    Path = $"{Path.GetFileName(directory)}/" +
                           Path.GetRelativePath(directory, path).Replace('\\', '/'),
                    File = path
                }))
            .OrderBy(item => item.Path, StringComparer.Ordinal)
            .Select(item =>
                $"{item.Path}|{new FileInfo(item.File).Length}|" +
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    File.ReadAllBytes(item.File)))));

    private static async Task VerifyHotResourceVersionPublishingAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"BEngineHotResourceVersions.{Guid.NewGuid():N}");
        try
        {
            VerifyHotResourceVersionPolicy();
            await VerifyConcurrentContentPublishAsync(root).ConfigureAwait(false);
            var workspace = ProjectWorkspaceFactory.Create(Path.Combine(root, "Project"),
                "Hot Resource Versions");
            var fixture = await CreatePlayerContentFixtureAsync(workspace).ConfigureAwait(false);
            Require(ProjectScriptCompiler.CompileAndLoad(workspace) is not null,
                "The Hot Resource version fixture did not compile its managed assemblies.");
            var releaseAssemblies = RuntimeManagedCodeReleaseInputCollector.Collect(workspace).Assemblies
                .Select(static assembly => assembly.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            await AssertHotUpdateContentAsync(
                    workspace,
                    BuildTargetCatalog.InferCurrentDesktop(),
                    releaseAssemblies,
                    fixture,
                    "content-built-in-probe",
                    root)
                .ConfigureAwait(false);
        }
        finally
        {
            TryDeleteTestDirectory(root);
        }
    }

    private static void VerifyHotResourceVersionPolicy()
    {
        foreach (var version in new[] { "v1", "v2", "v10", "v999999999999999999999999999999" })
            Require(PlayerBuildSettingsStore.NormalizeHotResourceVersion(version) == version,
                $"Canonical Hot Resource version '{version}' was changed.");
        foreach (var version in new[]
                 {
                     "V1", "V2", "v0", "v00", "v01", "v1.0", "release-2", "con", "v1.",
                     "v1/next", "*", " v1 "
                 })
            RequireThrows<InvalidDataException>(() =>
                PlayerBuildSettingsStore.NormalizeHotResourceVersion(version));
        Require(PlayerBuildSettingsStore.CompareHotResourceVersions("v2", "v1") > 0 &&
                PlayerBuildSettingsStore.CompareHotResourceVersions("v10", "v2") > 0 &&
                PlayerBuildSettingsStore.CompareHotResourceVersions(
                    "v100000000000000000000", "v99999999999999999999") > 0 &&
                PlayerBuildSettingsStore.CompareHotResourceVersions("v2", "v2") == 0,
            "Hot Resource versions were not compared as arbitrary-precision positive integers.");
    }

    private static async Task VerifyConcurrentContentPublishAsync(string root)
    {
        const string packageName = "com.bengine.tests.concurrent-publish";
        var output = Path.Combine(root, "ConcurrentRemoteContent");
        var gate = Path.Combine(root, "concurrent-publish.gate");
        var readyOne = Path.Combine(root, "concurrent-publish-one.ready");
        var readyTwo = Path.Combine(root, "concurrent-publish-two.ready");
        var stagingOne = Path.Combine(root, "ConcurrentStagingOne");
        var stagingTwo = Path.Combine(root, "ConcurrentStagingTwo");
        var lockAttempting = Path.Combine(root, "content-lock-worker.attempting");
        var lockAcquired = Path.Combine(root, "content-lock-worker.acquired");
        CreateContentPublication(stagingOne, packageName, "v2");
        CreateContentPublication(stagingTwo, packageName, "v10");

        string stableLockPath;
        Process lockWorker;
        using (var held = PlayerContentPublishLock.Acquire(output, packageName, CancellationToken.None))
        {
            stableLockPath = held.Path;
            Require(File.Exists(held.Path) &&
                    !Path.GetFullPath(held.Path).StartsWith(
                        Path.GetFullPath(output) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase),
                "The stable cross-process lock must live outside the Hot Resource output.");
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            RequireThrows<OperationCanceledException>(() =>
            {
                using var blocked = PlayerContentPublishLock.Acquire(output, packageName, cancellation.Token);
            });

            lockWorker = StartSelfProcess(
                "--content-lock-worker", output, packageName, lockAttempting, lockAcquired);
            var attemptTimer = Stopwatch.StartNew();
            while (!File.Exists(lockAttempting) && attemptTimer.Elapsed < TimeSpan.FromSeconds(5) &&
                   !lockWorker.HasExited)
                await Task.Delay(10).ConfigureAwait(false);
            Require(File.Exists(lockAttempting),
                "The cross-process lock worker did not attempt to acquire the publication lock.");
            await Task.Delay(200).ConfigureAwait(false);
            Require(!File.Exists(lockAcquired) && !lockWorker.HasExited,
                "A child process acquired the publication lock while the parent still held it.");
        }
        await lockWorker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        var lockWorkerDiagnostics = await lockWorker.StandardOutput.ReadToEndAsync().ConfigureAwait(false) +
                                    await lockWorker.StandardError.ReadToEndAsync().ConfigureAwait(false);
        Require(lockWorker.ExitCode == 0 && File.Exists(lockAcquired) &&
                File.ReadAllText(lockAcquired).Equals(stableLockPath, StringComparison.Ordinal) &&
                File.Exists(stableLockPath),
            "The child process did not acquire the same stable lock after the parent released it.\n" +
            lockWorkerDiagnostics);
        lockWorker.Dispose();
        using (var reacquired = PlayerContentPublishLock.Acquire(
                   output, packageName, CancellationToken.None))
            Require(reacquired.Path.Equals(stableLockPath, StringComparison.Ordinal),
                "Reacquiring a publication lock changed its stable path.");

        using var first = StartContentPublishWorker(stagingOne, output, readyOne, gate);
        using var second = StartContentPublishWorker(stagingTwo, output, readyTwo, gate);
        var firstOutput = first.StandardOutput.ReadToEndAsync();
        var firstError = first.StandardError.ReadToEndAsync();
        var secondOutput = second.StandardOutput.ReadToEndAsync();
        var secondError = second.StandardError.ReadToEndAsync();
        try
        {
            var readyTimer = Stopwatch.StartNew();
            while ((!File.Exists(readyOne) || !File.Exists(readyTwo)) &&
                   readyTimer.Elapsed < TimeSpan.FromSeconds(10) &&
                   !first.HasExited && !second.HasExited)
                await Task.Delay(10).ConfigureAwait(false);
            await File.WriteAllTextAsync(gate, "publish").ConfigureAwait(false);
            await Task.WhenAll(first.WaitForExitAsync(), second.WaitForExitAsync())
                .WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            var diagnostics = await firstOutput.ConfigureAwait(false) +
                              await firstError.ConfigureAwait(false) +
                              await secondOutput.ConfigureAwait(false) +
                              await secondError.ConfigureAwait(false);
            Require(File.Exists(readyOne) && File.Exists(readyTwo),
                "Concurrent publication workers did not both reach the release gate.\n" + diagnostics);
            Require(first.ExitCode == 0 && second.ExitCode == 0,
                "A concurrent Hot Resource publisher failed.\n" + diagnostics);
            var packageDirectory = Path.Combine(output, packageName);
            var latestBytes = await File.ReadAllBytesAsync(
                Path.Combine(packageDirectory, "latest.json")).ConfigureAwait(false);
            Require(AssetBundleCatalogSerializer.DeserializeLatestPointer(latestBytes).Version == "v10" &&
                    latestBytes.AsSpan().SequenceEqual(CreateLatestPointerBytes(packageName, "v10")) &&
                    Directory.Exists(Path.Combine(packageDirectory, "v2")) &&
                    Directory.Exists(Path.Combine(packageDirectory, "v10")) &&
                    Directory.EnumerateFileSystemEntries(output).Count() == 1 &&
                    !Directory.EnumerateFiles(packageDirectory, ".latest.*.tmp").Any(),
                "Concurrent v2/v10 publication did not retain both immutable releases and advance latest to v10.");
        }
        finally
        {
            if (!first.HasExited) first.Kill(entireProcessTree: true);
            if (!second.HasExited) second.Kill(entireProcessTree: true);
        }
    }

    private static void CreateContentPublication(string staging, string packageName, string version)
    {
        var versionDirectory = Path.Combine(staging, packageName, version);
        Directory.CreateDirectory(versionDirectory);
        var catalogBytes = AssetBundleCatalogSerializer.SerializeCatalog(new AssetBundleCatalog
        {
            PackageName = packageName,
            Version = version
        });
        File.WriteAllBytes(Path.Combine(versionDirectory, "catalog.json"), catalogBytes);
        var versionBytes = AssetBundleCatalogSerializer.SerializeVersion(new AssetBundleVersion
        {
            PackageName = packageName,
            Version = version,
            CatalogFile = "catalog.json",
            CatalogSha256 = AssetBundleCatalogSerializer.ComputeSha256(catalogBytes),
            CatalogSize = catalogBytes.LongLength
        });
        File.WriteAllBytes(Path.Combine(versionDirectory, "version.json"), versionBytes);
        File.WriteAllBytes(
            Path.Combine(staging, packageName, "latest.json"),
            CreateLatestPointerBytes(packageName, version));
    }

    private static byte[] CreateLatestPointerBytes(string packageName, string version) =>
        AssetBundleCatalogSerializer.SerializeLatestPointer(new AssetBundleLatestPointer
        {
            PackageName = packageName,
            Version = version
        });

    private static void AssertMinimalLatestPointer(
        string packageDirectory,
        string packageName,
        string version,
        string versionDirectory)
    {
        var latestBytes = File.ReadAllBytes(Path.Combine(packageDirectory, "latest.json"));
        var versionBytes = File.ReadAllBytes(Path.Combine(versionDirectory, "version.json"));
        var pointer = AssetBundleCatalogSerializer.DeserializeLatestPointer(latestBytes);
        Require(pointer.Version == version &&
                latestBytes.AsSpan().SequenceEqual(CreateLatestPointerBytes(packageName, version)) &&
                !latestBytes.AsSpan().SequenceEqual(versionBytes) &&
                !Encoding.UTF8.GetString(latestBytes).Contains("catalog", StringComparison.OrdinalIgnoreCase),
            "AssetBundleBuilder latest.json is not a canonical version-only pointer.");
        _ = AssetBundleCatalogSerializer.DeserializeVersion(versionBytes);
    }

    private static Process StartContentPublishWorker(
        string staging,
        string output,
        string ready,
        string gate) =>
        StartSelfProcess("--content-publish-worker", staging, output, ready, gate);

    private static Process StartSelfProcess(params string[] arguments)
    {
        var executable = Environment.ProcessPath ?? "dotnet";
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = FindRepositoryRoot()
        };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        return Process.Start(start) ??
               throw new InvalidOperationException("Could not start a test worker process.");
    }

    private static void RunContentPublishWorker(
        string staging,
        string output,
        string ready,
        string gate)
    {
        File.WriteAllText(ready, Environment.ProcessId.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        var timer = Stopwatch.StartNew();
        while (!File.Exists(gate) && timer.Elapsed < TimeSpan.FromSeconds(15)) Thread.Sleep(10);
        if (!File.Exists(gate))
            throw new TimeoutException("Timed out waiting for the concurrent publication gate.");
        var publication = PlayerContentUpdatePipeline.Publish(staging, output);
        Console.WriteLine(
            $"CONTENT_PUBLISH_WORKER_OK|latest={publication.LatestVersion}|promoted={publication.Promoted}");
    }

    private static void RunContentLockWorker(
        string output,
        string packageName,
        string attempting,
        string acquired)
    {
        File.WriteAllText(attempting, Environment.ProcessId.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        using var publishLock = PlayerContentPublishLock.Acquire(
            output, packageName, CancellationToken.None);
        File.WriteAllText(acquired, publishLock.Path);
    }

    private static void AssertWindowsGuiSubsystem(string executable)
    {
        using var stream = File.OpenRead(executable);
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt16() != 0x5a4d)
            throw new InvalidDataException("Packaged Windows Player has no DOS/PE header.");
        stream.Position = 0x3c;
        var peOffset = reader.ReadInt32();
        if (peOffset <= 0 || peOffset > stream.Length - 256)
            throw new InvalidDataException("Packaged Windows Player has an invalid PE offset.");
        stream.Position = peOffset;
        if (reader.ReadUInt32() != 0x00004550)
            throw new InvalidDataException("Packaged Windows Player has an invalid PE signature.");
        stream.Position = peOffset + 24;
        var optionalHeader = stream.Position;
        var magic = reader.ReadUInt16();
        if (magic is not (0x10b or 0x20b))
            throw new InvalidDataException("Packaged Windows Player has an unsupported PE optional header.");
        stream.Position = optionalHeader + 68;
        Require(reader.ReadUInt16() == 2,
            "Packaged Windows Player is not linked as a Windows GUI application and will open a console.");
    }

    private static async Task<string> AssertPackagedPlayerLayoutAsync(
        string output,
        string projectName,
        BuildTargetDescriptor target,
        IReadOnlySet<string> releaseAssemblies,
        PlayerContentFixture fixture,
        string cacheRoot)
    {
        var packagedName = PlayerBuildLayout.SanitizePlayerName(projectName);
        var executableName = packagedName +
                             (target.Platform == BuildTargetPlatform.Windows ? ".exe" : string.Empty);
        var dataDirectoryName =
            $"{packagedName}{PlayerPackagedResourceAddresses.DataDirectorySuffix}";
        var expectedRootEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            executableName,
            dataDirectoryName
        };
        var actualRootEntries = Directory.EnumerateFileSystemEntries(output)
            .Select(Path.GetFileName)
            .Where(static name => name is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Require(expectedRootEntries.SetEquals(actualRootEntries),
            $"Desktop Player root is not the executable/_data layout: " +
            $"{string.Join(", ", actualRootEntries.OrderBy(static name => name, StringComparer.Ordinal))}.");

        var runtimeRoot = Path.Combine(output, dataDirectoryName);
        var resourcesDirectory = Path.Combine(
            runtimeRoot, PlayerPackagedResourceAddresses.ResourcesDirectoryName);
        var playerArchivePath = Path.Combine(
            resourcesDirectory, PlayerPackagedResourceAddresses.PlayerArchiveFileName);
        Require(File.Exists(playerArchivePath),
            $"Desktop Player package has no Player resource archive at '{playerArchivePath}'.");
        var playerArchive = BuiltInResourceArchive.Open(playerArchivePath);
        var bootstrap = RuntimeMetadataSerializer.Deserialize(playerArchive.ReadBytes(
                PlayerPackagedResourceAddresses.RuntimeMetadata)).PlayerBootstrap ??
            throw new InvalidDataException("Packaged runtime metadata has no Player bootstrap section.");
        Require(bootstrap.ProductName == projectName &&
                bootstrap.Executable.Equals(executableName, StringComparison.OrdinalIgnoreCase) &&
                bootstrap.DataDirectory.Equals(dataDirectoryName, StringComparison.OrdinalIgnoreCase) &&
                bootstrap.CacheDirectory == PlayerBootstrapManifest.DefaultCacheDirectory &&
                bootstrap.HotUpdateStartupScene == fixture.HotUpdateStartupScene,
            "Player bootstrap manifest does not identify the product executable and _data directory.");
        var dataEntries = Directory.EnumerateFileSystemEntries(runtimeRoot)
            .Select(Path.GetFileName)
            .Where(static name => name is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Require(dataEntries.SetEquals([
                PlayerPackagedResourceAddresses.AssemblyDirectoryName,
                PlayerPackagedResourceAddresses.ResourcesDirectoryName]),
            "Player _data directory must contain only assembly and resources.");
        var assemblyDirectory = bootstrap.ResolveAssemblyDirectory(output);
        Require(Path.TrimEndingDirectorySeparator(bootstrap.ResolveResourceDirectory(output))
                .Equals(Path.TrimEndingDirectorySeparator(runtimeRoot), StringComparison.OrdinalIgnoreCase),
            "Embedded Player bootstrap points outside its runtime metadata directory.");
        Require(bootstrap.Version == PlayerBootstrapManifest.CurrentVersion &&
                bootstrap.PlayerResourceArchive.Equals(
                    $"{dataDirectoryName}/{PlayerPackagedResourceAddresses.ResourcesDirectoryName}/" +
                    PlayerPackagedResourceAddresses.PlayerArchiveFileName,
                    StringComparison.OrdinalIgnoreCase) &&
                bootstrap.BuildTargetResource == PlayerPackagedResourceAddresses.BuildTargetManifest &&
                bootstrap.SplashImageResource == PlayerPackagedResourceAddresses.SplashImage &&
                string.IsNullOrEmpty(bootstrap.BuildTargetManifest) &&
                string.IsNullOrEmpty(bootstrap.SplashImage),
            "Packaged Player bootstrap does not use the v4 archived-resource contract.");
        var resourceEntries = Directory.EnumerateFileSystemEntries(resourcesDirectory)
            .Select(Path.GetFileName)
            .Where(static name => name is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Require(resourceEntries.SetEquals(
                [PlayerPackagedResourceAddresses.PlayerArchiveFileName, BuiltInResourceArchive.FileName]) &&
                !Directory.EnumerateDirectories(resourcesDirectory).Any(),
            "Player resources must contain only player.bresources and aot.bresources.");
        var nativeFiles = Directory.EnumerateFiles(assemblyDirectory)
            .Select(Path.GetFileName)
            .Where(static name => name is not null)
            .Cast<string>()
            .ToArray();
        var allowedNativeFiles = target.Platform switch
        {
            BuildTargetPlatform.Windows => new HashSet<string>(
                ["glfw3.dll", "libveldrid-spirv.dll"], StringComparer.OrdinalIgnoreCase),
            BuildTargetPlatform.Linux => new HashSet<string>(
                ["libglfw.so.3", "libveldrid-spirv.so"], StringComparer.OrdinalIgnoreCase),
            BuildTargetPlatform.MacOS => new HashSet<string>(
                ["libglfw.3.dylib", "libveldrid-spirv.dylib"], StringComparer.OrdinalIgnoreCase),
            _ => []
        };
        Require(!Directory.EnumerateDirectories(assemblyDirectory).Any() &&
                nativeFiles.Length != 0 &&
                nativeFiles.All(allowedNativeFiles.Contains) &&
                nativeFiles.Any(name => name.Contains("glfw", StringComparison.OrdinalIgnoreCase)),
            "Player _data/assembly must contain only the current platform's native runtime libraries.");
        Require(playerArchive.TryGetEntry(PlayerPackagedResourceAddresses.RuntimeMetadata, out _) &&
                playerArchive.TryGetEntry(PlayerPackagedResourceAddresses.BuildTargetManifest, out _) &&
                playerArchive.TryGetEntry(PlayerPackagedResourceAddresses.SplashImage, out _),
            "Player resource archive omitted runtime metadata, build target, or splash image.");
        var packagedAssetBundles = Directory.EnumerateFiles(
                output, "*.bassetbundle", SearchOption.AllDirectories)
            .ToArray();
        var packagedRemoteMetadata = Directory.EnumerateFiles(
                output, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) is "catalog.json" or "latest.json")
            .ToArray();
        Require(packagedAssetBundles.Length == 0 && packagedRemoteMetadata.Length == 0 &&
                !Directory.EnumerateDirectories(output, "*", SearchOption.AllDirectories)
                    .Any(path => Path.GetFileName(path).Equals(
                        "AssetBundles", StringComparison.OrdinalIgnoreCase)),
            "Player payload contains an AssetBundle, remote catalog, latest pointer, or AssetBundles directory.");
        Require(!Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories).Any(path =>
                    path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)),
            "Player package contains loose YAML metadata or source files.");
        var nonLowercasePaths = Directory.EnumerateFileSystemEntries(
                output, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(output, path))
            .Where(relative => relative.Split(Path.DirectorySeparatorChar).Any(segment =>
                !segment.Equals(segment.ToLowerInvariant(), StringComparison.Ordinal)))
            .ToArray();
        Require(nonLowercasePaths.Length == 0,
            "Player package contains non-lowercase file or directory names: " +
            string.Join(", ", nonLowercasePaths));
        var playerAddresses = playerArchive.EnumerateAddresses()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Require(playerAddresses.Contains(
                    PlayerPackagedResourceAddresses.CoreResourcesPrefix + "Icons/BEngine.64.rgba") &&
                playerAddresses.Any(address => address.StartsWith(
                    PlayerPackagedResourceAddresses.CoreResourcesPrefix + "Shaders/PortableScene/",
                    StringComparison.OrdinalIgnoreCase)) &&
                !playerAddresses.Any(address => address.Contains(
                    "/Shaders/IMGUI/", StringComparison.OrdinalIgnoreCase)),
            "Player resource archive is missing Core resources or contains editor-only IMGUI shaders.");
        if (target.Platform == BuildTargetPlatform.Windows)
        {
                var coreShaders = playerAddresses.Where(address => address.StartsWith(
                    PlayerPackagedResourceAddresses.CoreResourcesPrefix + "Shaders/PortableScene/",
                    StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileName)
                .Where(static name => name is not null)
                .Cast<string>()
                .ToArray();
            Require(!coreShaders.Any(name => name.Contains(".webgpu.", StringComparison.OrdinalIgnoreCase)) &&
                    coreShaders.Any(name => name.Contains(".direct3d.", StringComparison.OrdinalIgnoreCase)) &&
                    coreShaders.Any(name => name.Contains(".vulkan.", StringComparison.OrdinalIgnoreCase)) &&
                    coreShaders.Any(name => name.Contains(".opengl.", StringComparison.OrdinalIgnoreCase)),
                "Windows Player core Shaders do not match its D3D/Vulkan/OpenGL fallback set.");
        }
        var rawPlayerArchive = File.ReadAllBytes(playerArchivePath);
        Require(rawPlayerArchive.AsSpan().IndexOf(
                    Encoding.UTF8.GetBytes(PlayerPackagedResourceAddresses.RuntimeMetadata)) < 0 &&
                rawPlayerArchive.AsSpan().IndexOf("BENGBMET"u8) < 0 &&
                rawPlayerArchive.AsSpan().IndexOf(
                    Encoding.UTF8.GetBytes(
                        PlayerPackagedResourceAddresses.CoreResourcesPrefix +
                        "Shaders/PortableScene/")) < 0 &&
                rawPlayerArchive.AsSpan().IndexOf(
                    new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }) < 0,
            "Player.bresources exposes resource addresses, metadata, Shader paths, or PNG data in plaintext.");
        Require(!Directory.EnumerateDirectories(output, "*", SearchOption.AllDirectories).Any(path =>
                    Path.GetFileName(path).Equals("Game", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(path).Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(path).Equals("Editor", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(path).Equals("Library", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(path).Equals(".staging", StringComparison.OrdinalIgnoreCase)) &&
                !Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories).Any(path =>
                    path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".asmdef.yaml", StringComparison.OrdinalIgnoreCase)),
            "Player package copied project source, Assets, Editor, or Library content.");
        Require(!Directory.EnumerateFiles(output, "*.dll", SearchOption.AllDirectories).Any(path =>
                    releaseAssemblies.Contains(Path.GetFileNameWithoutExtension(path))) &&
                !Directory.EnumerateFiles(output, "BEngine.Editor*", SearchOption.AllDirectories).Any() &&
                !Directory.EnumerateFiles(output, "BEngine.Launcher*", SearchOption.AllDirectories).Any(),
            "Player package leaked project, Editor, or Launcher assemblies outside its AOT archive.");
        Require(!File.Exists(Path.Combine(
                runtimeRoot, "Packages", "ReleaseInput", "Resources", "Data", "value.txt")),
            "Player package copied package Resources outside its built-in AOT archive.");

        var runtimeWorkspace = ProjectWorkspace.OpenRuntime(runtimeRoot);
        Require(runtimeWorkspace.Project.StartupScene.Equals(
                fixture.AotScene, StringComparison.OrdinalIgnoreCase),
            "Packaged runtime metadata does not start from the AOT scene.");
        var settings = runtimeWorkspace.RuntimeMetadata?.AssetBundles ??
                       throw new InvalidDataException("Player runtime metadata has no AssetBundle settings.");
        Require(string.IsNullOrEmpty(settings.BuiltInDirectory),
            "Packaged runtime metadata still points at a built-in AssetBundle directory.");
        var aotArchivePath = Path.Combine(
            runtimeRoot,
            PlayerPackagedResourceAddresses.ResourcesDirectoryName,
            BuiltInResourceArchive.FileName);
        Require(File.Exists(aotArchivePath),
            $"Desktop Player package has no built-in AOT archive at '{aotArchivePath}'.");
        var aotArchive = BuiltInResourceArchive.Open(aotArchivePath);
        var rawAotArchive = File.ReadAllBytes(aotArchivePath);
        Require(rawAotArchive.AsSpan().IndexOf(Encoding.UTF8.GetBytes(fixture.AotScene)) < 0 &&
                rawAotArchive.AsSpan().IndexOf(Encoding.UTF8.GetBytes(fixture.AotResource)) < 0 &&
                rawAotArchive.AsSpan().IndexOf("aot-only-resource"u8) < 0,
            "aot.bresources exposes AOT addresses or payloads in plaintext.");
        var addresses = aotArchive.EnumerateAddresses().ToHashSet(StringComparer.OrdinalIgnoreCase);
        Require(addresses.Contains(fixture.AotScene) && addresses.Contains(fixture.AotResource) &&
                addresses.Contains("Assets/__BEngine/HotUpdate/AOT.dll"),
            "The built-in resource archive omitted its AOT scene, resource, or assembly.");
        Require(!addresses.Contains(fixture.SelectedScene) &&
                !addresses.Contains(fixture.ReferencedSprite) &&
                !addresses.Contains(fixture.DynamicResource) &&
                !addresses.Contains("Assets/__BEngine/HotUpdate/Game.dll") &&
                fixture.ExcludedAssets.All(path => !addresses.Contains(path)),
            "The built-in resource archive contains Main/game-flow content outside the AOT closure.");
        Require(addresses.Where(path =>
                    path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                    !path.StartsWith("Assets/__BEngine/", StringComparison.OrdinalIgnoreCase) &&
                    !path.StartsWith("Assets/Packages/", StringComparison.OrdinalIgnoreCase))
                .All(path => path.StartsWith(
                    PlayerContentBuildPipeline.AotAssetRoot + "/", StringComparison.OrdinalIgnoreCase)),
            "The built-in resource archive contains a project asset outside Assets/Aot.");
        Require(addresses.Contains(ManagedCodeReleaseManifest.DefaultAddress) &&
                addresses.Contains(
                    "Assets/Packages/com.bengine.tests.release-input/Resources/Data/value.txt"),
            "The built-in resource archive omitted its managed release or AOT package dependency.");
        if (target.Platform == BuildTargetPlatform.Windows)
            AssertWindowsPackageShaderResources(
                addresses, "built-in AOT archive", requireCompleteFallbackSet: true);
        Require(Encoding.UTF8.GetString(aotArchive.ReadBytes(fixture.AotResource)) ==
                "aot-only-resource",
            "The built-in AOT resource archive could not read and verify an AOT payload.");
        var release = ManagedCodeReleaseManifestSerializer.Deserialize(
            aotArchive.ReadBytes(ManagedCodeReleaseManifest.DefaultAddress));
        Require(new HashSet<string>(
                ["AOT", "BEngine.Tests.ReleaseInputPackage"], StringComparer.OrdinalIgnoreCase)
            .SetEquals(release.Modules.Select(module => module.Name)),
            "The built-in managed release is not the AOT package dependency closure.");
        foreach (var module in release.Modules)
            Require(aotArchive.ReadBytes(module.AssemblyAddress).Length > 0,
                $"The built-in AOT archive could not read assembly '{module.AssemblyAddress}'.");

        var validationLog = Path.Combine(cacheRoot, "packaged-player-aot-validation.log");
        await AssertPackagedValidationStopsAfterAotAsync(
                Path.Combine(output, executableName), output, runtimeRoot, validationLog,
                "Packaged executable")
            .ConfigureAwait(false);
        var sandboxRoot = bootstrap.ResolveCacheDirectory(output);
        Require(sandboxRoot.Equals(Path.Combine(output, "sandbox"),
                target.Platform == BuildTargetPlatform.Windows
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal),
            "Packaged Player did not resolve its default cache to '<Player>/sandbox'.");
        Require(Directory.Exists(sandboxRoot) &&
                Directory.Exists(Path.Combine(sandboxRoot, "objects")) &&
                Directory.Exists(Path.Combine(sandboxRoot, "catalogs")),
            $"Packaged Player did not create its flat remote cache at '{sandboxRoot}'.");
        Require(!Directory.Exists(Path.Combine(sandboxRoot, "AssetBundles")) &&
                !Directory.Exists(Path.Combine(sandboxRoot, "_aot_builtin")) &&
                !File.Exists(Path.Combine(sandboxRoot, "active.json")) &&
                !File.Exists(Path.Combine(sandboxRoot, "previous.json")),
            "An empty sandbox retained an AssetBundles wrapper, AOT cache, or active game pointer.");

        var executablePath = Path.Combine(output, executableName);
        var renamedExecutable = Path.Combine(output,
            target.Platform == BuildTargetPlatform.Windows ? "Renamed Player.exe" : "Renamed Player");
        File.Move(executablePath, renamedExecutable);
        try
        {
            await AssertPackagedValidationStopsAfterAotAsync(
                    renamedExecutable, output, runtimeRoot,
                    Path.Combine(cacheRoot, "renamed-player-aot-validation.log"),
                    "Renamed packaged executable")
                .ConfigureAwait(false);
        }
        finally
        {
            if (File.Exists(renamedExecutable)) File.Move(renamedExecutable, executablePath);
        }
        return runtimeRoot;
    }

    private static void AssertWindowsPackageShaderResources(
        IReadOnlySet<string> addresses,
        string label,
        bool requireCompleteFallbackSet)
    {
        var shaders = addresses.Where(static address =>
                address.StartsWith("Assets/Packages/", StringComparison.OrdinalIgnoreCase) &&
                address.Contains("/Resources/Shaders/", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var containsUnsupportedBackend = shaders.Any(address =>
            address.Contains(".webgpu.", StringComparison.OrdinalIgnoreCase) ||
            address.Contains(".webgl.", StringComparison.OrdinalIgnoreCase) ||
            address.Contains(".metal.", StringComparison.OrdinalIgnoreCase) ||
            address.Contains(".opengles.", StringComparison.OrdinalIgnoreCase));
        var containsCompleteFallbackSet =
            shaders.Any(address => address.Contains(".direct3d.", StringComparison.OrdinalIgnoreCase)) &&
            shaders.Any(address => address.Contains(".vulkan.", StringComparison.OrdinalIgnoreCase)) &&
            shaders.Any(address => address.Contains(".opengl.", StringComparison.OrdinalIgnoreCase));
        Require(!containsUnsupportedBackend &&
                (shaders.Length == 0 || !requireCompleteFallbackSet || containsCompleteFallbackSet),
            $"The {label} does not contain only the Windows package Shader fallback set.");
    }

    private static async Task AssertPackagedValidationStopsAfterAotAsync(
        string executable,
        string workingDirectory,
        string runtimeRoot,
        string diagnosticsLog,
        string scenario)
    {
        if (File.Exists(diagnosticsLog)) File.Delete(diagnosticsLog);
        var failure = await RunProcessExpectFailureAsync(
                executable, ["--validate"], workingDirectory, diagnosticsLog)
            .ConfigureAwait(false);
        Require(!failure.Contains("BENGINE_PLAYER_VALIDATION_OK|", StringComparison.Ordinal),
            $"{scenario} reported a complete validation without a remote HotUpdate release.");
        Require(File.Exists(diagnosticsLog),
            $"{scenario} did not write its startup diagnostics log.");
        var diagnostics = await File.ReadAllTextAsync(diagnosticsLog).ConfigureAwait(false);
        var bootstrap = diagnostics.IndexOf(
            $"01_BOOTSTRAP_STARTED|project={runtimeRoot}", StringComparison.OrdinalIgnoreCase);
        var aotActivated = diagnostics.IndexOf(
            "03_AOT_ASSEMBLY_ACTIVATED", Math.Max(bootstrap, 0), StringComparison.Ordinal);
        var aotSceneStarted = diagnostics.IndexOf(
            "03_AOT_SCENE_STARTED", Math.Max(aotActivated, 0), StringComparison.Ordinal);
        var updateFailed = diagnostics.IndexOf(
            "BENGINE_FAIL|phase=AOT_UPDATE_CHECK", Math.Max(aotSceneStarted, 0),
            StringComparison.Ordinal);
        var validationFailed = diagnostics.IndexOf(
            "BENGINE_FAIL|phase=VALIDATION", Math.Max(updateFailed, 0), StringComparison.Ordinal);
        Require(bootstrap >= 0 &&
                aotActivated > bootstrap &&
                aotSceneStarted > aotActivated &&
                updateFailed > aotSceneStarted &&
                validationFailed > updateFailed,
            $"{scenario} did not follow _Data bootstrap -> AOT activation -> AOT scene -> " +
            "manual update check -> empty-sandbox validation failure order.\n" + diagnostics);
        Require((failure + diagnostics).Contains(
                "has no remote content URL configured",
                StringComparison.Ordinal),
            $"{scenario} did not clearly reject the missing remote HotUpdate release.");
        Require(!diagnostics.Contains("04_CONTENT_ACTIVATED", StringComparison.Ordinal) &&
                !diagnostics.Contains("05_HOTUPDATE_INJECTED", StringComparison.Ordinal) &&
                !diagnostics.Contains("07_GAME_STARTED", StringComparison.Ordinal),
            $"{scenario} entered the game without sandbox HotUpdate content.");
    }

    private sealed record PlayerContentFixture(
        string AotScene,
        string AotResource,
        string HotUpdateStartupScene,
        string SelectedScene,
        string ReferencedSprite,
        string DynamicResource,
        IReadOnlyList<string> ExcludedAssets);

    private static async Task<string> RunProcessAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory)
    {
        var start = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ??
                            throw new InvalidOperationException($"Could not start '{fileName}'.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        var combined = await output.ConfigureAwait(false) + await error.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"'{fileName}' exited with code {process.ExitCode}.\n{combined}");
        return combined;
    }

    private static async Task<string> RunProcessExpectFailureAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        string diagnosticsLog)
    {
        var start = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["BENGINE_PLAYER_LOG_PATH"] = diagnosticsLog;
        using var process = Process.Start(start) ??
                            throw new InvalidOperationException($"Could not start '{fileName}'.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        var combined = await output.ConfigureAwait(false) + await error.ConfigureAwait(false);
        if (process.ExitCode == 0)
            throw new InvalidOperationException(
                $"'{fileName}' unexpectedly passed without a remote HotUpdate release.\n{combined}");
        return combined;
    }

    private static void CopyTestDirectory(string source, string destination)
    {
        var sourceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, file);
            var segments = relative.Split(Path.DirectorySeparatorChar);
            if (segments.Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                                        segment.Equals("obj", StringComparison.OrdinalIgnoreCase)) ||
                file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }

    private static void TryDeleteTestDirectory(string root)
    {
        if (!Directory.Exists(root)) return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
        catch
        {
            // Test cleanup must not hide the build or runtime assertion that caused the failure.
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "BEngine.sln")))
                return directory.FullName;
        throw new DirectoryNotFoundException("BEngine repository root was not found.");
    }

    private static async Task RequireThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try { await action().ConfigureAwait(false); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static void RequireThrows<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class SingleRuntimeFactory(IManagedCodeRuntime runtime) : IManagedCodeRuntimeFactory
    {
        public IManagedCodeRuntime CreateRuntime(ManagedCodeRuntimeRequest request) => runtime;
    }

    private sealed class ProviderRuntimeFactory(IManagedCodeRuntimeProvider provider) : IManagedCodeRuntimeFactory
    {
        public IManagedCodeRuntime CreateRuntime(ManagedCodeRuntimeRequest request)
        {
            if (request.PreferredKind != provider.Kind) throw new PlatformNotSupportedException();
            return provider.CreateRuntime();
        }
    }

    private sealed class FakeRuntime(params IHotUpdateModule[] modules) : IManagedCodeRuntime
    {
        public ManagedCodeRuntimeKind Kind => ManagedCodeRuntimeKind.CoreClr;
        public bool IsLoaded { get; private set; }
        public bool Disposed { get; private set; }

        public BValueTask<ManagedCodeLoadResult> LoadAsync(
            ManagedCodeRelease release,
            CancellationToken cancellationToken = default)
        {
            IsLoaded = true;
            return BValueTask<ManagedCodeLoadResult>.FromResult(new ManagedCodeLoadResult(modules));
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            IsLoaded = false;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ProbeModule(
        string id,
        IReadOnlyList<string> dependencies,
        List<string> events,
        bool failStart = false) : IHotUpdateModule
    {
        public HotUpdateModuleDescriptor Descriptor { get; } = new()
        {
            ModuleId = id,
            Dependencies = dependencies
        };

        public BValueTask ConfigureAsync(
            IHotUpdateModuleContext context,
            CancellationToken cancellationToken = default)
        {
            events.Add($"configure:{id}");
            return BValueTask.CompletedTask;
        }

        public BValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            events.Add($"start:{id}");
            return failStart
                ? BValueTask.FromException(new InvalidOperationException("expected start failure"))
                : BValueTask.CompletedTask;
        }

        public BValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            events.Add($"stop:{id}");
            return BValueTask.CompletedTask;
        }
    }

    private interface IProbeService { string Value { get; } }
    private sealed record ProbeService(string Value) : IProbeService;

    private sealed class EmptyServices : IServiceProvider
    {
        internal static readonly EmptyServices Instance = new();
        public object? GetService(Type serviceType) => null;
    }

    private sealed class TransactionProbeBuildTargetProvider(bool fail) : IPlayerBuildTargetProvider
    {
        internal const string MarkerFile = "transaction-probe.txt";
        public string ProviderId => "bengine.dotnet-desktop";
        public bool CanBuild(BuildTargetDescriptor target, out string reason)
        {
            reason = string.Empty;
            return target.HasBuiltInPlayerHost;
        }

        public Task BuildAsync(PlayerBuildContext context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (fail) throw new InvalidOperationException("Expected replacement build failure.");
            File.WriteAllText(Path.Combine(context.StagingDirectory, MarkerFile), "replacement");
            return Task.CompletedTask;
        }
    }
}

public sealed class LoadedHotModule : IHotUpdateModule
{
    public HotUpdateModuleDescriptor Descriptor { get; } = new() { ModuleId = "loaded-test-module" };
}

public sealed class AotLifecycleModule : IHotUpdateModule
{
    public const string EventPathVariable = "BENGINE_AOT_INTERPRETER_TEST_EVENTS";

    public HotUpdateModuleDescriptor Descriptor { get; } = new() { ModuleId = "aot-lifecycle-test-module" };

    public BValueTask ConfigureAsync(
        IHotUpdateModuleContext context,
        CancellationToken cancellationToken = default)
    {
        Record("configure");
        return BValueTask.CompletedTask;
    }

    public BValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        Record("start");
        return BValueTask.CompletedTask;
    }

    public BValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        Record("stop");
        return BValueTask.CompletedTask;
    }

    private static void Record(string value)
    {
        var path = Environment.GetEnvironmentVariable(EventPathVariable);
        if (!string.IsNullOrWhiteSpace(path)) File.AppendAllLines(path, [value]);
    }
}

public sealed class LoadedEngineServiceModule : IEngineServiceModule
{
    public void ConfigureServices(IServiceCollection services, EngineServiceContext context) =>
        services.AddSingleton<LoadedEngineRegistrationMarker>();
}

public sealed class LoadedEngineRegistrationMarker;
