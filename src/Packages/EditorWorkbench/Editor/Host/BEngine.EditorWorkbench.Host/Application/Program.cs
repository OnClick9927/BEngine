using EditorCapabilities = BEngine.Editor.EditorServices;
using BEngine.Editor;
using BEngine.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using BEngine.Platform;
using BEngine.ProjectSystem.Editor;

namespace BEngine.Workbench;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        using var startupProfile = EditorStartupProfile.Start();
        BEngine.Tooling.ToolEnvironment.ConfigureEditor();
        if (EditorCommandLineHelp.TryHandle(args)) return 0;
        // Set the shell identity before creating any project-loading windows.
        WindowsEditorPresentation.InitializeProcess();
        WindowsPlatform.Initialize();
        WindowsEditorPlatform.Initialize();
        EditorHostPackages.Initialize();
        using var startupPlatformServices = new ServiceCollection()
            .AddBEnginePlatformServices()
            .AddBEngineEditorCompilation()
            .AddBEngineEditorPlatformServices()
            .AddBEnginePlatformServices()
            .BuildServiceProvider();
        using var startupServiceAttachment = EditorServiceScope.Attach(startupPlatformServices);
        EditorStartupReporter? startupReporter = null;
        try
        {
            RegisterPublishedResourceRoot();
            EditorLogStore.Initialize();
            WindowsEditorPresentation.EnablePerMonitorDpi();
            if (EditorCommandExtensions.TryRun(args, out var commandResult)) return commandResult;
            if (args.Length > 0 && args[0].StartsWith("--", StringComparison.Ordinal))
                return startupPlatformServices.GetRequiredService<IEditorCommandLineRunner>().Run(args);

            if (args.Length == 0)
            {
                throw new ArgumentException("A BEngine project path is required. Start the engine with Engine/Engine/BEngine.Hub.exe.");
            }
            startupReporter = EditorStartupReporter.Create(args);
            startupReporter?.ReportStarting();
            var projectPath = PathService.GetFullPath(args[0]);
            bool __queryResult2002;
            {
                var __querySource2002 = args.Skip(1);
                ArgumentNullException.ThrowIfNull(__querySource2002, "source");
                __queryResult2002 = false;
                foreach (var __queryItem2002 in __querySource2002)
                {
                    if ((__queryItem2002.Equals("--editor-status", StringComparison.OrdinalIgnoreCase)))
                    {
                        __queryResult2002 = true;
                        break;
                    }
                }
            }
            
            var openEditorStatus = __queryResult2002;
            bool __queryResult2154;
            {
                var __querySource2154 = args.Skip(1);
                ArgumentNullException.ThrowIfNull(__querySource2154, "source");
                __queryResult2154 = false;
                foreach (var __queryItem2154 in __querySource2154)
                {
                    if ((__queryItem2154.Equals("--ui-builder", StringComparison.OrdinalIgnoreCase)))
                    {
                        __queryResult2154 = true;
                        break;
                    }
                }
            }
            
            var openUiBuilder = __queryResult2154;
            EditorStartupProfile.Mark("Project lock and preferences");
            using var instance = EditorInstanceContext.Create(projectPath);
            EditorPreferences.Initialize();
            EditorStartupProfile.Mark("Startup progress window");
            using var loadingWindow = GpuStartupProgressWindow.Show(
                startupPlatformServices.GetRequiredService<NativeWindowUtility>(),
                startupPlatformServices.GetRequiredService<ExecutionUtility>(),
                startupPlatformServices.GetRequiredService<PlatformUtility>().IsWindows
                    ? startupPlatformServices.GetRequiredService<IEditorUtilityPlatform>() : null);
            EditorStartupProfile.Mark("Editor services");
            using var serviceProvider = new ServiceCollection()
                .AddBEngineEditor(new EditorLaunchOptions(projectPath, openEditorStatus, openUiBuilder))
                .BuildServiceProvider(new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                });
            using var projectScope = serviceProvider.CreateScope();
            GpuEditorApplication editor;
            using (EditorCallbacks.BeginLoopScope("BEngine.Editor startup"))
                editor = projectScope.ServiceProvider.GetRequiredService<GpuEditorApplication>();
            try
            {
                editor.Run(() =>
                {
                    loadingWindow.Complete();
                    startupProfile.Complete(projectPath);
                    startupReporter?.TryReportReady();
                });
            }
            finally
            {
                try { editor.Dispose(); }
                catch (Exception exception)
                {
                    Console.Error.WriteLine($"BENGINE_EDITOR_SHUTDOWN_FAILED|{exception}");
                    throw;
                }
            }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"BENGINE_EDITOR_FAILED|{exception}");
            string? logPath = null;
            try
            {
                // Project construction or disposal may have failed. Diagnostics must use
                // the bootstrap services, whose lifetime encloses this catch block.
                using var diagnosticsServices = EditorServiceScope.Attach(startupPlatformServices);
                var fileSystem = FileSystem;
                var platformService = PlatformService;
                var projectArgument = args.Length > 0 &&
                                      (args[0].Equals("--build-player", StringComparison.OrdinalIgnoreCase) ||
                                       args[0].Equals("--build-content-update", StringComparison.OrdinalIgnoreCase))
                    ? args.ElementAtOrDefault(1)
                    : args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal)
                        ? args[0]
                        : null;
                var candidateLogPath = projectArgument is null
                    ? EditorDataPaths.editorBootstrapLogPath
                    : EditorDataPaths.GetProjectLogPath(projectArgument, "EditorBootstrap.log");
                using var stream = fileSystem.OpenFile(
                    candidateLogPath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite,
                    4096,
                    FileOptions.SequentialScan);
                using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false),
                    bufferSize: 4096, leaveOpen: true);
                writer.Write($"[{platformService.UtcNow.ToLocalTime():O}] {exception}" +
                             platformService.NewLine);
                writer.Flush();
                fileSystem.Flush(stream);
                logPath = candidateLogPath;
            }
            catch (Exception logException)
            {
                Trace.WriteLine($"BEngine Editor startup failure could not be logged: {logException}");
            }

            var commandLineBuild = args.Length > 0 && args[0].StartsWith("--", StringComparison.Ordinal);
            var reportedToLauncher = startupReporter?.TryReportFailure(exception, logPath) == true;
            if (commandLineBuild)
                Console.Error.WriteLine($"BENGINE_PLAYER_BUILD_FAILED|{exception}");
            else if (!reportedToLauncher)
            {
                var logInformation = logPath is null ? string.Empty : $"\n\nLog: {logPath}";
                try
                {
                    WindowsEditorPresentation.ShowStartupError("BEngine",
                        $"BEngine Editor failed to start.\n\n{exception.GetBaseException().Message}" +
                        logInformation);
                }
                catch (Exception dialogException)
                {
                    Trace.WriteLine($"BEngine Editor startup dialog failed: {dialogException}");
                }
            }
            return 1;
        }
        finally
        {
            startupReporter?.Dispose();
            try
            {
                EditorUtility.ClearProgressBar();
            }
            catch (Exception progressException)
            {
                Trace.WriteLine($"BEngine Editor progress cleanup failed: {progressException}");
            }
        }
    }

    private static void RegisterPublishedResourceRoot()
    {
        foreach(var package in BPackageCatalog.DiscoverAvailable().packages)
        {
            var root=Path.GetDirectoryName(package.Path)!;
            Resources.RegisterResourceRoot(root);
            EditorResource.RegisterResourceRoot(root);
            Resources.RegisterResourceRoot(Path.Combine(root,package.Document.Content.Editor));
        }
    }

    private static FileUtility FileSystem =>
        EditorCapabilities.GetRequiredService<FileUtility>();

    private static PathUtility PathService =>
        EditorCapabilities.GetRequiredService<PathUtility>();

    private static PlatformUtility PlatformService =>
        EditorCapabilities.GetRequiredService<PlatformUtility>();

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
