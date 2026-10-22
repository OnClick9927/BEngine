using BEngine.Build;

namespace BEngine.Editor;

/// <summary>Conservative mobile import defaults. No game paths or content rules belong here.</summary>
public static class PlatformAssetImportDefaults
{
    public static void RegisterMobile(BuildTargetPlatform platform) =>
        PlatformAssetBuildPipeline.Register(platform, context =>
        {
            if (context.Importer == nameof(TextureImporter))
            {
                var settings = context.Settings;
                var maximum = settings.TryGetValue("maxTextureSize", out var size) && int.TryParse(size, out var parsed) ? parsed : 2048;
                var platformMaximum = platform == BuildTargetPlatform.Android ? 1024 : 2048;
                settings["maxTextureSize"] = Math.Min(platformMaximum, maximum).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var format = settings.TryGetValue("compressionFormat", out var authoredFormat) ? authoredFormat : "Automatic";
                var type = settings.TryGetValue("textureType", out var authoredType) ? authoredType : "Texture";
                if (format.Equals("Automatic", StringComparison.OrdinalIgnoreCase))
                    settings["compressionFormat"] = type is "Sprite" or "EditorGui" or "Cursor" ? "Rgba32" : "Etc2Rgba8";
            }
            else if (context.Importer == "AudioImporter")
            {
                // Keep large music/ambience streams out of the decoded PCM heap;
                // short authored cues retain their load policy and low latency.
                if (context.SourceBytes > 512 * 1024) context.Settings["loadType"] = "Streaming";
                context.Settings["loadInBackground"] = "True";
            }
        });
}
