namespace BEngine.Launcher;

internal sealed class LauncherPackageInfo
{
    public required string Id { get; init; }
    public string Type { get; init; } = "package";
    public required string DisplayName { get; init; }
    public required string Version { get; init; }
    public string MinimumEngineVersion { get; init; } = "0.0.0";
    public required string Description { get; init; }
    public required string DirectoryPath { get; init; }
    public string? RuntimeAssembly { get; init; }
    public string? EditorAssembly { get; init; }
    public string? ProjectDirectory { get; init; }
    public string[] ExampleNames { get; init; } = [];
    public bool HasDocumentation { get; init; }
    public string Contents => DescribeContents(new HubLocalization("en"));
    public bool HasRuntime { get; init; }
    public bool HasEditor { get; init; }
    public IReadOnlyList<LauncherPackageDependency> Dependencies { get; init; } = [];
    public string SelectionLabel => $"{DisplayName}  {Version}";

    internal string DescribeContents(HubLocalization language)
    {
        var lines = new List<string> { language.Text("Type: {0}", language.Text(Type)) };
        lines.Add(language.Text("Engine compatibility: {0}", $"≥ {MinimumEngineVersion}"));
        if (Type == "project") lines.Add(language.Text("Import destination: {0}", $"Assets/3rd/{ProjectDirectory}"));
        lines.Add(language.Text("Runtime assembly: {0}", RuntimeAssembly ?? language.Text("None")));
        lines.Add(language.Text("Editor assembly: {0}", EditorAssembly ?? language.Text("None")));
        lines.Add(language.Text("Examples ({0}): {1}", ExampleNames.Length,
            ExampleNames.Length == 0 ? language.Text("None") : string.Join(", ", ExampleNames)));
        lines.Add(language.Text("Documentation: {0}", language.Text(HasDocumentation ? "Included — Editor/Doc/index.html" : "Not included")));
        return string.Join("\n", lines);
    }
}
