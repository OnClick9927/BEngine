using Microsoft.Extensions.DependencyInjection;

namespace BEngine.Launcher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
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

    private static void Run(string[] args)
    {
        System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
        var editorPath = ResolveEditorPath(args);
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

        var message = $"BEngine Launcher failed to start.{Environment.NewLine}{Environment.NewLine}" +
                      $"{exception.GetBaseException().Message}";
        if (logPath is not null)
            message += $"{Environment.NewLine}{Environment.NewLine}Log:{Environment.NewLine}{logPath}";

        try
        {
            MessageBox.Show(message, "BEngine", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch
        {
            System.Diagnostics.Trace.WriteLine(exception);
        }
    }

    private static string ResolveEditorPath(string[] args)
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

        var besideLauncher = Path.Combine(AppContext.BaseDirectory, "BEngine.Editor.exe");
        if (File.Exists(besideLauncher)) return besideLauncher;

        var baseDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = ResolveBuildConfiguration(baseDirectory);
        var sourceRoot = FindSourceRoot(baseDirectory);
        var candidates = sourceRoot is null
            ? Array.Empty<string>()
            : new[]
            {
                Path.Combine(sourceRoot.FullName, ".artifacts", "bin", "BEngine.Editor",
                    configuration.ToLowerInvariant(), "BEngine.Editor.exe")
            };
        foreach (var candidate in candidates)
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);

        var checkedPaths = new[] { besideLauncher }.Concat(candidates)
            .Select(static path => $"  {Path.GetFullPath(path)}");
        throw new FileNotFoundException(
            "BEngine Editor executable was not found. Build BEngine.Editor or pass --editor <path>." +
            Environment.NewLine + "Checked:" + Environment.NewLine + string.Join(Environment.NewLine, checkedPaths));
    }

    private static DirectoryInfo? FindSourceRoot(DirectoryInfo baseDirectory)
    {
        for (var directory = baseDirectory; directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "BEngine.sln")))
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
