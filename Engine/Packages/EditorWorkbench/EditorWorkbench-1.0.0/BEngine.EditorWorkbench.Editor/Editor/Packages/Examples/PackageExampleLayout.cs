using EditorCapabilities = BEngine.Editor.EditorServices;
using BEngine.Platform;
namespace BEngine.Editor;

internal static class PackageExampleLayout
{
    private static readonly string[] ContentDirectories = ["Editor", "runtime", "res"];

    internal static string GetImportPath(BPackageManifest manifest, string fallbackName)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var requested = string.IsNullOrWhiteSpace(manifest.DefaultImportPath)
            ? $"Examples/{SanitizeName(fallbackName)}"
            : manifest.DefaultImportPath;
        var normalized = BPackagePathUtility.NormalizeRelativePath(requested);
        var segments = normalized.Split('/');
        if (segments.Length != 2 || !segments[0].Equals("Examples", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Package examples must import into Examples/<package-or-feature-name>.");
        return $"Examples/{segments[1]}";
    }

    internal static void Validate(BPackageManifest manifest, string fallbackName)
    {
        _ = GetImportPath(manifest, fallbackName);
        foreach (var entry in manifest.Entries)
        {
            var path = BPackagePathUtility.NormalizeRelativePath(entry.RelativePath);
            var separator = path.IndexOf('/');
            var topLevel = separator < 0 ? path : path[..separator];
            if (ContentDirectories.Contains(topLevel, StringComparer.Ordinal) || Query1352() || !path.Contains('/') && (path.Equals("Readme.md", StringComparison.OrdinalIgnoreCase) || path.Equals("Readme.md.meta", StringComparison.OrdinalIgnoreCase)))
                continue;
            bool Query1352()
            {
                var querySource1352 = ContentDirectories;
                ArgumentNullException.ThrowIfNull(querySource1352, "source");
                if (querySource1352.GetType() == typeof(string[]) || querySource1352.GetType() == typeof(global::System.Collections.Generic.List<string>))
                {
                    global::System.ReadOnlySpan<string> queryValues1352 = querySource1352.GetType() == typeof(string[]) ? (string[])(object)querySource1352 : global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<string>)(object)querySource1352);
                    foreach (var directory in queryValues1352)
                    {
                        if (path.Equals($"{directory}.meta", StringComparison.Ordinal))
                            return true;
                    }
                }
                else
                {
                    foreach (var directory in querySource1352)
                    {
                        if (path.Equals($"{directory}.meta", StringComparison.Ordinal))
                            return true;
                    }
                }
            
                return false;
            }
            throw new InvalidDataException(
                $"Example entry '{path}' must be inside Editor, runtime or res.");
        }
    }

    private static string SanitizeName(string value)
    {
        var invalid = EditorCapabilities.GetRequiredService<PathUtility>().GetInvalidFileNameChars();
        var result = new string(value.Trim().Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray());
        return string.IsNullOrWhiteSpace(result) ? "Example" : result;
    }
}
