using System.Security.Cryptography;
using BEngine.Build;
using BEngine.Platform;

namespace BEngine.Editor;

/// <summary>Target defaults are supplied by platform assemblies; explicit asset overrides win.</summary>
public sealed record PlatformAssetImportContext(string SourcePath, string AssetType, string Importer,
    BuildTargetPlatform Platform, long SourceBytes, IReadOnlyDictionary<string, string> AuthoredSettings,
    IDictionary<string, string> Settings);

public static class PlatformAssetBuildPipeline
{
    private static readonly Dictionary<BuildTargetPlatform, Action<PlatformAssetImportContext>> Policies = [];
    private static readonly object Gate = new();

    public static void Register(BuildTargetPlatform platform, Action<PlatformAssetImportContext> policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        lock (Gate) Policies[platform] = policy;
    }

    /// <summary>Builds an isolated target artifact without changing source metadata or editor artifacts.</summary>
    public static bool TryBuildArtifact(string sourcePath, string sourceHash, string artifactPath,
        string assetType, string importerName, IReadOnlyDictionary<string, string> authored,
        BuildTargetPlatform platform, FileUtility files, PathUtility paths, CancellationToken cancellation,
        out byte[] content, out IReadOnlyDictionary<string, string> effectiveSettings)
    {
        content = []; effectiveSettings = authored;
        if (!Enum.IsDefined(platform)) throw new ArgumentOutOfRangeException(nameof(platform));
        var settings = new Dictionary<string, string>(authored, StringComparer.Ordinal);
        Action<PlatformAssetImportContext>? policy;
        lock (Gate) Policies.TryGetValue(platform, out policy);
        policy?.Invoke(new(sourcePath, assetType, importerName, platform, files.GetFileLength(sourcePath), authored, settings));
        var prefix = $"platform.{platform}.";
        if (authored.TryGetValue(prefix + "overridden", out var enabled) && bool.TryParse(enabled, out var overridden) && overridden)
            foreach (var (key, value) in authored)
                if (key.StartsWith(prefix, StringComparison.Ordinal) && key != prefix + "overridden")
                    settings[key[prefix.Length..] == "format" ? "compressionFormat" : key[prefix.Length..]] = value;
        if (settings.Count == authored.Count && settings.All(pair => authored.TryGetValue(pair.Key, out var value) && value == pair.Value))
            return false;
        cancellation.ThrowIfCancellationRequested();
        using (var source = files.OpenRead(sourcePath))
            if (!Convert.ToHexString(SHA256.HashData(source)).Equals(sourceHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Asset source changed after the build snapshot: {sourcePath}");
        settings["builtForPlatform"] = platform.ToString();
        var importer = AssetImporter.CreateForImport(sourcePath, sourcePath, settings);
        if (importer.GetType().Name != importerName)
            throw new InvalidDataException($"Platform importer '{importerName}' is not registered for '{sourcePath}'.");
        // AssetImportContext owns and removes the uncommitted temporary file.
        using var context = new AssetImportContext(sourcePath, sourcePath, artifactPath, settings,
            cancellation, files, paths);
        importer.Import(context);
        cancellation.ThrowIfCancellationRequested();
        content = files.ReadAllBytes(context.artifactPath);
        effectiveSettings = settings;
        return true;
    }
}
