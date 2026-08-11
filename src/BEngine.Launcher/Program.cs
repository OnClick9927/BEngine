namespace BEngine.Launcher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
        var editorPath = ResolveEditorPath(args);
        System.Windows.Forms.Application.Run(new ProjectLauncherForm(editorPath));
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

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "BEngine.Editor", "bin", "Debug", "net9.0", "BEngine.Editor.exe"));
    }
}
