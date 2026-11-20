using System.Text.Json;

namespace BEngine.Launcher;

// Package-owned templates are data. Hub has no knowledge of their assembly names or game framework.
internal static class LauncherPackageProjectTemplates
{
    internal static void Copy(LauncherProjectWorkspace workspace, IReadOnlyList<LauncherPackageInfo> packages)
    {
        var stageName = Path.GetFileName(workspace.RootPath);
        if (!stageName.StartsWith(".bengine-project-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(stageName[17..], "N", out _))
            throw new InvalidOperationException("Package project templates can only be installed into a new creation workspace.");
        RejectLinks(workspace.RootPath);
        var files = new List<(string Source, string Target)>();
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in packages.OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase))
        {
            var editor = Path.GetFullPath(Path.Combine(package.DirectoryPath, "Editor"));
            var metadata = Path.Combine(editor, "tooling.json");
            RejectLinks(metadata);
            if (!File.Exists(metadata)) continue;
            using var document = JsonDocument.Parse(File.ReadAllText(metadata));
            if (!document.RootElement.TryGetProperty("projectTemplateDirectory", out var property)) continue;
            if (property.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Project template directory must be a relative path.");
            var relative = property.GetString()!;
            var source = ResolveInside(editor, relative);
            RejectLinks(source);
            if (!Directory.Exists(source)) throw new DirectoryNotFoundException($"Package project template is missing: {source}");
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
                    if (!targets.Add(target)) throw new InvalidDataException($"Package project templates conflict at '{target}'.");
                    files.Add((entry, target));
                }
            }
        }
        // Validate the complete template before touching even the staging project's files.
        foreach (var (source, target) in files)
        {
            RejectLinks(source); RejectLinks(target);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
        }
    }

    private static string ResolveInside(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || Query3140(relative.Split('/', '\\')))
            throw new InvalidDataException($"Project template path must remain relative: {relative}");
        [global::BEngine.SkipLoopGuard]
        bool Query3140(global::System.Collections.Generic.IEnumerable<string> querySource3140)
        {
            ArgumentNullException.ThrowIfNull(querySource3140, "source");
            bool Matches3140(string segment) => segment is "" or "." or ".." || segment.Contains(':');
            global::System.ReadOnlySpan<string> queryValues3140;
            if (querySource3140.GetType() == typeof(string[]))
                queryValues3140 = (string[])querySource3140;
            else if (querySource3140.GetType() == typeof(global::System.Collections.Generic.List<string>))
                queryValues3140 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<string>)querySource3140);
            else
            {
                foreach (var segment in querySource3140)
                    if (Matches3140(segment))
                        return true;
                return false;
            }
        
            foreach (var segment in queryValues3140)
                if (Matches3140(segment))
                    return true;
            return false;
        }
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var result = Path.GetFullPath(Path.Combine(root, relative));
        if (!result.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Project template path escapes its root: {relative}");
        return result;
    }

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
