using System.Text.Json;

namespace BEngine.App.Editor.ProjectSystem;

/// <summary>Installs the package-owned gameplay template only during new-project creation.</summary>
public static class PlayerGameProjectTemplate
{
    public static void Copy(ProjectWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var name = Path.GetFileName(workspace.RootPath);
        // IProjectTemplate is a creation hook. Never infer whether an existing user's project is pristine.
        if (!name.StartsWith(".bengine-project-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(name[17..], "N", out _)) return;
        RejectLinks(workspace.RootPath);
        var package = Gather723(BPackageCatalog.Discover(workspace).packages) ?? throw new InvalidDataException("The Application package's project template could not be located.");
        [global::BEngine.SkipLoopGuard]
        global::BEngine.ProjectSystem.BPackageDefinition? Gather723(global::System.Collections.Generic.IEnumerable<global::BEngine.ProjectSystem.BPackageDefinition> items723)
        {
            ArgumentNullException.ThrowIfNull(items723, "source");
            bool Evaluate723(global::BEngine.ProjectSystem.BPackageDefinition definition) => definition.Document.Runtime?.Assembly == typeof(GameDomain).Assembly.GetName().Name;
            global::System.ReadOnlySpan<global::BEngine.ProjectSystem.BPackageDefinition> values;
            if (items723.GetType() == typeof(global::BEngine.ProjectSystem.BPackageDefinition[]))
                values = (global::BEngine.ProjectSystem.BPackageDefinition[])items723;
            else if (items723.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.ProjectSystem.BPackageDefinition>))
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.ProjectSystem.BPackageDefinition>)items723);
            else
                return global::System.Linq.Enumerable.FirstOrDefault(items723, Evaluate723);
            foreach (var definition in values)
                if (Evaluate723(definition))
                    return definition;
            return default!;
        }
        var editor = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(package.Path)!, "Editor"));
        var metadata = Path.Combine(editor, "tooling.json");
        RejectLinks(metadata);
        using var document = JsonDocument.Parse(File.ReadAllText(metadata));
        if (!document.RootElement.TryGetProperty("projectTemplateDirectory", out var property) ||
            property.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("The Application package is missing its project template metadata. Re-export the package.");
        var relative = property.GetString()!;
        var source = ResolveInside(editor, relative);
        RejectLinks(source);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException($"Package project template is missing: {source}");
        var files = new List<(string Source, string Target)>();
        var directories = new Stack<string>(); directories.Push(source);
        while (directories.TryPop(out var directory))
        {
            RejectLinks(directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                RejectLinks(entry);
                if (Directory.Exists(entry)) { directories.Push(entry); continue; }
                var target = ResolveInside(workspace.RootPath, Path.GetRelativePath(source, entry));
                RejectLinks(target);
                files.Add((entry, target));
            }
        }
        foreach (var (input, target) in files)
        {
            RejectLinks(input); RejectLinks(target);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(input, target, overwrite: true);
        }
    }

    private static string ResolveInside(string root, string relative) =>
        BEngine.Platform.PathUtility.Current.ResolveChildPath(root, relative);

    private static void RejectLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"Project templates cannot traverse symbolic links or junctions: {current}");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
