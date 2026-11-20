using System.Diagnostics;
using System.Text.Json;
using BEngine.ProjectSystem;

namespace BEngine.Launcher;

/// <summary>Reads provenance from the engine release, independently of package-authored metadata.</summary>
internal sealed class LauncherBundledPackages
{
    internal const string FileName = "bundled-packages.json";
    private readonly Dictionary<string, (string Id, string Version)>? _releases;

    private LauncherBundledPackages(Dictionary<string, (string Id, string Version)>? releases) => _releases = releases;

    internal static LauncherBundledPackages Load(string packagesRoot, string engineVersion)
    {
        var path = Path.Combine(Path.GetDirectoryName(packagesRoot)!, "BEgine", "BEgine-" + engineVersion, FileName);
        if (!File.Exists(path)) return new(null);
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var document = json.RootElement;
            if (document.GetProperty("format").GetString() != "BEngine.BundledPackages" ||
                document.GetProperty("version").GetInt32() != 1 ||
                document.GetProperty("engineVersion").GetString() != engineVersion)
                throw new InvalidDataException("Unsupported engine package distribution manifest.");
            var releases = new Dictionary<string, (string Id, string Version)>(
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in document.GetProperty("packages").EnumerateArray())
            {
                var id = entry.GetProperty("id").GetString();
                var version = entry.GetProperty("version").GetString();
                var relative = entry.GetProperty("path").GetString()?.Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version) ||
                    string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) ||
                    relative.Split('/').Any(part => part is "" or "." or ".." || part.Contains(':')) ||
                    !relative.EndsWith("/package.yaml", StringComparison.Ordinal) || !ids.Add(id))
                    throw new InvalidDataException("Invalid bundled package release.");
                BPackageVersion.Validate(version);
                releases.Add(Path.GetFullPath(Path.Combine(packagesRoot, relative)), (id, version));
            }
            return new(releases);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or
            JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or FormatException)
        {
            // Provenance must not prevent an otherwise usable old/damaged installation from opening projects.
            Trace.WriteLine($"Could not read bundled package provenance '{path}': {exception.Message}");
            return new(null);
        }
    }

    internal PackageDistributionKind Classify(BPackageDefinition definition)
    {
        if (_releases is null) return PackageDistributionKind.Unknown;
        return _releases.TryGetValue(Path.GetFullPath(definition.Path), out var release) &&
               release.Id.Equals(definition.Document.Id, StringComparison.OrdinalIgnoreCase) &&
               release.Version == definition.Document.PackageVersion
            ? PackageDistributionKind.Bundled : PackageDistributionKind.External;
    }
}
