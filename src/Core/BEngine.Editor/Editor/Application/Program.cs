using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace BEngine.Editor;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        EditorStartupReporter? startupReporter = null;
        try
        {
            EditorLogStore.Initialize();
            NativeDpiAwareness.EnablePerMonitorV2();
            if (args.Length > 0 && args[0].Equals("--convert-html", StringComparison.OrdinalIgnoreCase))
                return ConvertHtml(args);

            if (args.Length == 0)
            {
                throw new ArgumentException("A BEngine project path is required. Start the engine with BEngine.bat.");
            }
            startupReporter = EditorStartupReporter.Create(args);
            startupReporter?.ReportStarting();
            var projectPath = Path.GetFullPath(args[0]);
            var openEditorStatus = args.Skip(1).Any(argument =>
                argument.Equals("--editor-status", StringComparison.OrdinalIgnoreCase));
            var openUiBuilder = args.Skip(1).Any(argument =>
                argument.Equals("--ui-builder", StringComparison.OrdinalIgnoreCase));
            using var instance = EditorInstanceContext.Create(projectPath);
            EditorPreferences.Initialize();
            using var loadingWindow = GpuStartupProgressWindow.Show();
            using var serviceProvider = new ServiceCollection()
                .AddBEngineEditor(new EditorLaunchOptions(projectPath, openEditorStatus, openUiBuilder))
                .BuildServiceProvider(new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                });
            using var projectScope = serviceProvider.CreateScope();
            using var editor = projectScope.ServiceProvider.GetRequiredService<GpuEditorApplication>();
            editor.Run(() =>
            {
                loadingWindow.Complete();
                startupReporter?.TryReportReady();
            });
            return 0;
        }
        catch (Exception exception)
        {
            string? logPath = null;
            try
            {
                var candidateLogPath = EditorDataPaths.editorBootstrapLogPath;
                File.AppendAllText(candidateLogPath,
                    $"[{DateTimeOffset.Now:O}] {exception}{Environment.NewLine}",
                    new System.Text.UTF8Encoding(false));
                logPath = candidateLogPath;
            }
            catch (Exception logException)
            {
                Trace.WriteLine($"BEngine Editor startup failure could not be logged: {logException}");
            }

            var reportedToLauncher = startupReporter?.TryReportFailure(exception, logPath) == true;
            if (!reportedToLauncher)
            {
                var logInformation = logPath is null ? string.Empty : $"\n\nLog: {logPath}";
                try
                {
                    NativeStartupDialog.ShowError("BEngine",
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

    private static int ConvertHtml(IReadOnlyList<string> args)
    {
        if (args.Count is < 2 or > 3)
            throw new ArgumentException(
                "Usage: BEngine.Editor.exe --convert-html <source.html> [destination.uxml]");

        var sourcePath = Path.GetFullPath(args[1]);
        var destinationPath = args.Count == 3
            ? Path.GetFullPath(args[2])
            : Path.ChangeExtension(sourcePath, ".uxml");
        var converterType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("BEngine.UIElements.Editor.HtmlToUIElementsConverter"))
            .FirstOrDefault(type => type is not null) ?? throw new InvalidOperationException(
                "HTML conversion requires the optional com.bengine.ui-elements package.");
        var method = converterType.GetMethod("ConvertFile", System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic) ??
            throw new MissingMethodException(converterType.FullName, "ConvertFile");
        method.Invoke(null, [sourcePath, destinationPath]);
        Console.WriteLine("HTML_TO_UIELEMENTS_OK");
        Console.WriteLine($"UXML={destinationPath}");
        return 0;
    }
}
