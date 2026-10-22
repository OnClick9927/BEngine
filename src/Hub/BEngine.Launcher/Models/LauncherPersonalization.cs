namespace BEngine.Launcher;

internal sealed class LauncherPersonalization
{
    public string Language { get; set; } = "system";
    public string Theme { get; set; } = "Dark";
    public string AccentColor { get; set; } = "Teal";
    public string StartupPage { get; set; } = "Projects";
    public string ProjectSort { get; set; } = "Last opened";
    public string DefaultProjectDirectory { get; set; } = string.Empty;
    public bool ShowProjectPaths { get; set; } = true;
    public bool MinimizeOnOpen { get; set; }

    internal void Normalize()
    {
        Language = Choice(Language, ["system", "zh-CN", "en"]);
        Theme = Choice(Theme, ["Dark", "Light"]);
        AccentColor = Choice(AccentColor, ["Teal", "Blue", "Purple", "Orange", "Rose"]);
        if (string.Equals(StartupPage, "Packages", StringComparison.OrdinalIgnoreCase)) StartupPage = "Engines";
        StartupPage = Choice(StartupPage, ["Projects", "Engines"]);
        ProjectSort = Choice(ProjectSort, ["Last opened", "Name A–Z", "Name Z–A"]);
        DefaultProjectDirectory = DefaultProjectDirectory?.Trim() ?? string.Empty;
    }

    internal IEnumerable<LauncherProjectRecord> SortProjects(IEnumerable<LauncherProjectRecord> projects) =>
        ProjectSort switch
        {
            "Name A–Z" => projects.OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase),
            "Name Z–A" => projects.OrderByDescending(project => project.Name, StringComparer.OrdinalIgnoreCase),
            _ => projects.OrderByDescending(project => DateTimeOffset.TryParse(project.LastOpenedUtc, out var opened)
                    ? opened : DateTimeOffset.MinValue)
                .ThenBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
        };

    private static string Choice(string? value, string[] choices) =>
        choices.FirstOrDefault(choice => choice.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? choices[0];
}
