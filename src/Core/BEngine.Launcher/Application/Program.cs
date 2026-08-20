using Microsoft.Extensions.DependencyInjection;

namespace BEngine.Launcher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            Run(args);
        }
        catch (Exception exception)
        {
            ReportStartupFailure(exception);
        }
    }

    private static void Run(string[] args)
    {
        RegisterCoreResourceRoot();
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
            logPath = Path.Combine(BEngine.Editor.EditorDataPaths.logsPath, "Launcher.log");
            File.AppendAllText(logPath,
                $"[{DateTimeOffset.Now:O}] BEngine Launcher failed to start.{Environment.NewLine}" +
                $"{exception}{Environment.NewLine}{Environment.NewLine}");
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

    private static void RegisterCoreResourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
        {
            var coreRoot = Path.Combine(directory.FullName, "src", "Core");
            if (!Directory.Exists(Path.Combine(coreRoot, "EditorResources"))) continue;
            BEngine.Resources.RegisterResourceRoot(coreRoot);
            return;
        }
    }

    private static string ResolveEditorPath(string[] args)
    {
        var optionIndex = Array.IndexOf(args, "--editor");
        if (optionIndex >= 0 && optionIndex + 1 < args.Length)
        {
            return Path.GetFullPath(args[optionIndex + 1]);
        }

        var besideLauncher = Path.Combine(AppContext.BaseDirectory, "BEngine.Editor.exe");
        if (File.Exists(besideLauncher)) return besideLauncher;

        var outputDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = outputDirectory.Parent?.Name ?? "Debug";
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "BEngine.Editor", "bin", configuration, "net9.0-windows", "BEngine.Editor.exe"));
    }
}
