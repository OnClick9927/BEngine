using Microsoft.Extensions.DependencyInjection;

namespace BEngine.Editor;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
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
            var projectPath = Path.GetFullPath(args[0]);
            var openEditorStatus = args.Skip(1).Any(argument =>
                argument.Equals("--editor-status", StringComparison.OrdinalIgnoreCase));
            var openUiBuilder = args.Skip(1).Any(argument =>
                argument.Equals("--ui-builder", StringComparison.OrdinalIgnoreCase));
            using var instance = EditorInstanceContext.Create(projectPath);
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
            editor.Run(loadingWindow.Complete);
            return 0;
        }
        catch (Exception exception)
        {
            var logPath = EditorDataPaths.editorBootstrapLogPath;
            File.AppendAllText(logPath,
                $"[{DateTimeOffset.Now:O}] {exception}{Environment.NewLine}",
                new System.Text.UTF8Encoding(false));
            NativeStartupDialog.ShowError("BEngine",
                $"BEngine Editor failed to start.\n\n{exception.Message}\n\nLog: {logPath}");
            return 1;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
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
