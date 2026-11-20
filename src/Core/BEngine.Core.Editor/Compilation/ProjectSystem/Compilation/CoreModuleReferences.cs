namespace BEngine.ProjectSystem;

internal static class CoreModuleReferences
{
    private static readonly string[] RuntimeAssemblies =
    [
        "BEngine.Core", "BEngine.Player"
    ];

    private static readonly string[] EditorAssemblies =
    [
        "BEngine.Core.Editor"
    ];

    internal static void AddRuntime(IDictionary<string, string> references) => Add(references, RuntimeAssemblies);

    internal static void AddEditor(IDictionary<string, string> references) => Add(references, EditorAssemblies);

    private static void Add(IDictionary<string, string> references, IEnumerable<string> assemblies)
    {
        var root = EditorHostServices.Paths.GetDirectoryName(typeof(BEngine.Editor.EditorServices).Assembly.Location)!;
        foreach (var name in assemblies)
        {
            if (references.ContainsKey(name)) continue;
            var path = EditorHostServices.Paths.Combine(root, name + ".dll");
            if (!EditorHostServices.FileSystem.FileExists(path))
                throw new FileNotFoundException($"Built-in Core assembly '{name}' is missing from the engine installation.", path);
            references[name] = path;
        }
    }
}
