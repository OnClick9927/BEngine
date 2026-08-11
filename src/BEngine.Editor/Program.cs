namespace BEngine.Editor;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 0)
            {
                throw new ArgumentException("A BEngine project path is required. Start the engine with BEngine.bat.");
            }
            var projectPath = Path.GetFullPath(args[0]);
            var openEditorStatus = args.Skip(1).Any(argument =>
                argument.Equals("--editor-status", StringComparison.OrdinalIgnoreCase));
            var openUiBuilder = args.Skip(1).Any(argument =>
                argument.Equals("--ui-builder", StringComparison.OrdinalIgnoreCase));
            using var editor = new EditorHostApplication(projectPath, openEditorStatus, openUiBuilder);
            editor.Run();
            return 0;
        }
        catch (Exception exception)
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BEngine");
            Directory.CreateDirectory(logDirectory);
            var logPath = Path.Combine(logDirectory, "EditorBootstrap.log");
            File.AppendAllText(logPath,
                $"[{DateTimeOffset.Now:O}] {exception}{Environment.NewLine}",
                new System.Text.UTF8Encoding(false));
            MessageBox.Show($"BEngine Editor 启动失败。\n\n{exception.Message}\n\n日志：{logPath}",
                "BEngine", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
