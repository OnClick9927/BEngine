using BEngine.Serialization;
namespace BEngine.AssetBundles;
internal static class AssetBundleSettingsValidation
{
    internal static void Validate(AssetBundleSettingsDocument document)
    {
        if (document.Format != "BEngine.AssetBundleSettings" || document.Version != 1)
            throw new InvalidDataException(
                $"Unsupported AssetBundle settings '{document.Format}' v{document.Version}.");
        if (string.IsNullOrWhiteSpace(document.PackageName) || document.PackageName.Length > 128 ||
            document.PackageName[0] is not (>= 'a' and <= 'z') and
                not (>= 'A' and <= 'Z') and not (>= '0' and <= '9') ||
            document.PackageName.Any(character => character is not (>= 'a' and <= 'z') and
                not (>= 'A' and <= 'Z') and not (>= '0' and <= '9') and not '.' and not '_' and not '-'))
            throw new InvalidDataException("AssetBundle package name is invalid.");
        AssetDataValidation.ValidateOptionalRelativePath(document.BuiltInDirectory, nameof(document.BuiltInDirectory));
        AssetDataValidation.ValidateOptionalRelativePath(document.CacheDirectory, nameof(document.CacheDirectory));
        if (!Enum.IsDefined(document.LoadMode))
            throw new InvalidDataException("AssetBundle load mode is invalid.");
        if (document.Enabled && document.LoadMode == AssetBundleLoadMode.StreamingAssets &&
            (string.IsNullOrWhiteSpace(document.BuiltInDirectory) ||
             !string.IsNullOrWhiteSpace(document.RemoteBaseUrl) || document.CheckForUpdatesOnStartup ||
             document.ApplyUpdatesOnStartup))
            throw new InvalidDataException(
                "StreamingAssets mode requires a built-in directory and disabled remote/startup updates.");
        if (!string.IsNullOrWhiteSpace(document.RemoteBaseUrl) &&
            (!Uri.TryCreate(document.RemoteBaseUrl, UriKind.Absolute, out var remote) ||
             remote.Scheme is not ("http" or "https")))
            throw new InvalidDataException("AssetBundle remote base URL must be an absolute HTTP(S) URL.");
        if (document.RequireHttps && !string.IsNullOrWhiteSpace(document.RemoteBaseUrl) &&
            !document.RemoteBaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("AssetBundle remote base URL must use HTTPS.");
        if (document.MaxRetries < 0)
            throw new InvalidDataException("AssetBundle retry value is invalid.");
    }

}
