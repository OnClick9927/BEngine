using Microsoft.Extensions.DependencyInjection;
using System.Runtime.InteropServices;

namespace BEngine.Launcher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        InitializeTaskbarIdentity();
        BEngine.Platform.WindowsPlatform.Initialize();
        try
        {
            Run(args);
            return 0;
        }
        catch (Exception exception)
        {
            ReportStartupFailure(exception);
            return 1;
        }
    }

    private static void InitializeTaskbarIdentity()
    {
        if (OperatingSystem.IsWindows())
            Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID("BEngine.Hub"));
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    private static void Run(string[] args)
    {
        System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
        var launcherDirectory = Environment.ProcessPath is { Length: > 0 } processPath
            ? Path.GetDirectoryName(Path.GetFullPath(processPath))!
            : AppContext.BaseDirectory;
        var editorPath = ResolveEditorPath(args, launcherDirectory, requireInstalled: false);
        BEngine.Editor.EditorResource.RegisterResourceRoot(Path.GetDirectoryName(editorPath)!);
        using var services = new ServiceCollection()
            .AddBEngineLauncher(editorPath)
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
        System.Windows.Forms.Application.Run(services.GetRequiredService<ProjectLauncherForm>());
    }

    private static void ReportStartupFailure(Exception exception)
    {
        var language = new HubLocalization("system");
        try
        {
            var settings = new LauncherHistoryStore(BEngine.Editor.EditorDataPaths.launcherSettingsPath).Load();
            language = new HubLocalization(settings.Personalization.Language);
        }
        catch { /* Startup diagnostics must remain available even when preferences cannot be read. */ }
        string? logPath = null;
        try
        {
            var candidateLogPath = Path.Combine(BEngine.Editor.EditorDataPaths.logsPath, "Launcher.log");
            File.AppendAllText(candidateLogPath,
                $"[{DateTimeOffset.Now:O}] BEngine Launcher failed to start.{Environment.NewLine}" +
                $"{exception}{Environment.NewLine}{Environment.NewLine}");
            logPath = candidateLogPath;
        }
        catch (Exception logException)
        {
            System.Diagnostics.Trace.WriteLine(logException);
        }

        var message = language.Text("BEngine Hub failed to start.") + Environment.NewLine + Environment.NewLine +
                      $"{exception.GetBaseException().Message}";
        if (logPath is not null)
            message += Environment.NewLine + Environment.NewLine + language.Text("Log:") + Environment.NewLine + logPath;

        try
        {
            MessageBox.Show(message, "BEngine", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch
        {
            System.Diagnostics.Trace.WriteLine(exception);
        }
    }

    internal static string ResolveEditorPath(string[] args, string basePath, bool requireInstalled = true)
    {
        var optionIndex = Array.IndexOf(args, "--editor");
        if (optionIndex >= 0)
        {
            if (optionIndex + 1 >= args.Length)
                throw new ArgumentException("The --editor option requires an executable path.", nameof(args));
            var explicitPath = Path.GetFullPath(args[optionIndex + 1]);
            return File.Exists(explicitPath)
                ? explicitPath
                : throw new FileNotFoundException(
                    $"The BEngine Editor specified by --editor was not found at '{explicitPath}'.", explicitPath);
        }

        var besideLauncher = Path.Combine(basePath, "BEngine.Editor.exe");
        if (File.Exists(besideLauncher)) return besideLauncher;

        var composedHost = Path.Combine(basePath, "BEngine.Editor.Host.exe");
        if (File.Exists(composedHost)) return composedHost;

        var installationRoot = LauncherInstallationPaths.ResolveRoot(basePath);
        var enginesRoot = Path.Combine(installationRoot, "BEgine");
        var installedEditor = Path.Combine(enginesRoot, $"BEgine-{EngineVersion.Current}", "BEngine.Editor.exe");
        if (File.Exists(installedEditor)) return installedEditor;
        if (!requireInstalled && Directory.Exists(enginesRoot))
            return Path.Combine(enginesRoot, $"BEgine-{EngineVersion.Current}", "BEngine.Editor.exe");
        if (new DirectoryInfo(basePath).Name.Equals("Hub", StringComparison.OrdinalIgnoreCase) ||
            File.Exists(Path.Combine(installationRoot, "BEngine.Hub.exe")))
        {
            if (!requireInstalled) return installedEditor;
            throw new FileNotFoundException(
                $"BEngine {EngineVersion.Current} is not installed. Export this engine version or pass --editor <path>. " +
                $"Expected: {installedEditor}", installedEditor);
        }

        var baseDirectory = new DirectoryInfo(basePath);
        var configuration = ResolveBuildConfiguration(baseDirectory);
        var repositoryRoot = FindRepositoryRoot(baseDirectory);
        var candidates = repositoryRoot is null
            ? Array.Empty<string>()
            : new[]
            {
                Path.Combine(repositoryRoot.FullName, "Temp", "Engine", "Build", "bin", "BEngine.Editor.Host",
                    configuration.ToLowerInvariant(), "BEngine.Editor.Host.exe")
            };
        foreach (var candidate in candidates)
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);

        if (!requireInstalled)
            return Path.Combine(new LauncherEngineCatalog(basePath).ResolveEnginesRoot(),
                $"BEgine-{EngineVersion.Current}", "BEngine.Editor.exe");

        var checkedPaths = new[] { installedEditor, besideLauncher, composedHost }.Concat(candidates)
            .Select(static path => $"  {Path.GetFullPath(path)}");
        throw new FileNotFoundException(
            "BEngine Editor executable was not found. Build BEngine.Editor or pass --editor <path>." +
            Environment.NewLine + "Checked:" + Environment.NewLine + string.Join(Environment.NewLine, checkedPaths));
    }

    private static DirectoryInfo? FindRepositoryRoot(DirectoryInfo baseDirectory)
    {
        for (var directory = baseDirectory; directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Engine", "src", "BEngine.sln")))
                return directory;
        return null;
    }

    private static string ResolveBuildConfiguration(DirectoryInfo baseDirectory)
    {
        for (var directory = baseDirectory; directory is not null; directory = directory.Parent)
        {
            if (directory.Name.Equals("debug", StringComparison.OrdinalIgnoreCase)) return "Debug";
            if (directory.Name.Equals("release", StringComparison.OrdinalIgnoreCase)) return "Release";
        }
        return "Debug";
    }
}
