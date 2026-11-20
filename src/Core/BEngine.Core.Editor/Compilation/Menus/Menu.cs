namespace BEngine.Editor;

public static class Menu
{
    private static readonly Dictionary<string, bool> Checked = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, bool> Enabled = new(StringComparer.Ordinal);

    public static void SetChecked(string menuPath, bool isChecked)
    {
        var path = Normalize(menuPath);
        if (isChecked) Checked[path] = true;
        else Checked.Remove(path);
    }

    public static bool GetChecked(string menuPath) => Checked.GetValueOrDefault(Normalize(menuPath));

    public static void SetEnabled(string menuPath, bool isEnabled)
    {
        var path = Normalize(menuPath);
        if (isEnabled) Enabled.Remove(path);
        else Enabled[path] = false;
    }

    public static bool GetEnabled(string menuPath) => !Enabled.TryGetValue(Normalize(menuPath), out var enabled) || enabled;

    private static string Normalize(string menuPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(menuPath);
        return menuPath.Replace('\\', '/').Trim('/');
    }
}
