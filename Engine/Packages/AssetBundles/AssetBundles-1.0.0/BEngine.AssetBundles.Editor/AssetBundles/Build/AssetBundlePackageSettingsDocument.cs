namespace BEngine.Editor;

/// <summary>Editor-only ownership rules for incrementally built AssetBundle packages.</summary>
public sealed class AssetBundlePackageSettingsDocument
{
    public const string FileName = "AssetBundlePackages.yaml";
    public const string CurrentFormat = "BEngine.AssetBundlePackages";

    public string Format { get; set; } = CurrentFormat;
    public int Version { get; set; } = 1;
    public string DefaultPackageName { get; set; } = "default";
    public List<AssetBundlePackageRule> Packages { get; set; } = [];

    public void Validate()
    {
        if (!string.Equals(Format, CurrentFormat, StringComparison.Ordinal) || Version != 1)
            throw new InvalidDataException("Unsupported AssetBundle package settings format or version.");
        ValidateName(DefaultPackageName);
        if (Packages is null) throw new InvalidDataException("AssetBundle package rules cannot be null.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in Packages)
        {
            if (package is null) throw new InvalidDataException("AssetBundle package rules cannot contain null.");
            ValidateName(package.Name);
            if (!names.Add(package.Name))
                throw new InvalidDataException($"Duplicate AssetBundle package '{package.Name}'.");
            if (package.AssetPaths is null || package.AssetPaths.Count == 0)
                throw new InvalidDataException($"AssetBundle package '{package.Name}' needs at least one asset path.");
            foreach (var root in package.AssetPaths)
            {
                var path = NormalizeAssetPath(root);
                if (roots.TryGetValue(path, out var owner))
                    throw new InvalidDataException($"Asset path '{path}' is assigned to both '{owner}' and '{package.Name}'.");
                roots.Add(path, package.Name);
            }
        }
    }

    /// <summary>The longest matching asset or directory rule wins; unmatched assets use the default package.</summary>
    public string ResolvePackageName(string assetPath)
    {
        var path = NormalizeAssetPath(assetPath);
        var name = DefaultPackageName;
        var longest = -1;
        foreach (var package in Packages)
        foreach (var root in package.AssetPaths)
        {
            var normalized = NormalizeAssetPath(root);
            if (normalized.Length <= longest ||
                !(path.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                  path.StartsWith(normalized + "/", StringComparison.OrdinalIgnoreCase))) continue;
            name = package.Name;
            longest = normalized.Length;
        }
        return name;
    }

    internal static string NormalizeAssetPath(string? value)
    {
        var path = value?.Trim().Replace('\\', '/').TrimEnd('/') ?? string.Empty;
        if (!(path.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
              path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) ||
            PathLexicalRules.HasInvalidSegments(path.AsSpan(), separators: "/") ||
            path.AsSpan(0, path.Length).IndexOfAny(":*?\"<>|".AsSpan()) >= 0)
            throw new InvalidDataException($"AssetBundle package path '{value}' must be an asset or directory inside Assets.");
        return path;
    }

    private static void ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || !char.IsAsciiLetterOrDigit(name[0]) || Gather3684(name))
            throw new InvalidDataException($"Invalid AssetBundle package name '{name}'. Use letters, digits, '.', '_' or '-'.");
        [global::BEngine.SkipLoopGuard]
        bool Gather3684(global::System.Collections.Generic.IEnumerable<char> items3684)
        {
            ArgumentNullException.ThrowIfNull(items3684, "source");
            bool Evaluate3684(char character) => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-');
            global::System.ReadOnlySpan<char> values;
            if (items3684.GetType() == typeof(char[]))
                values = (char[])items3684;
            else if (items3684.GetType() == typeof(global::System.Collections.Generic.List<char>))
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<char>)items3684);
            else if (items3684 is string text3684)
                values = text3684.AsSpan();
            else
                return global::System.Linq.Enumerable.Any(items3684, Evaluate3684);
            foreach (var character in values)
                if (Evaluate3684(character))
                    return true;
            return false;
        }
        if (name.Equals("bengine", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("AssetBundle package name 'bengine' is reserved for generated engine content.");
    }
}

public sealed class AssetBundlePackageRule
{
    public string Name { get; set; } = string.Empty;
    public List<string> AssetPaths { get; set; } = [];
}
