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
            var topLevel = path.Split('/', 2)[0];
            if (ContentDirectories.Contains(topLevel, StringComparer.Ordinal) ||
                ContentDirectories.Any(directory => path.Equals($"{directory}.meta", StringComparison.Ordinal)) ||
                !path.Contains('/') && (path.Equals("Readme.md", StringComparison.OrdinalIgnoreCase) ||
                                        path.Equals("Readme.md.meta", StringComparison.OrdinalIgnoreCase)))
                continue;
            throw new InvalidDataException(
                $"Example entry '{path}' must be inside Editor, runtime or res.");
        }
    }

    private static string SanitizeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(value.Trim().Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray());
        return string.IsNullOrWhiteSpace(result) ? "Example" : result;
    }
}
