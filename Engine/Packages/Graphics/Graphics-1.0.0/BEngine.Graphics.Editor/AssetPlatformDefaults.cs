using System.Runtime.CompilerServices;
using BEngine.Build;
namespace BEngine.Editor;
internal static class GraphicsPlatformDefaults
{
    [ModuleInitializer] internal static void Register()
    {
        foreach(var platform in new[]{BuildTargetPlatform.Android,BuildTargetPlatform.IOS})
            PlatformAssetBuildPipeline.Register(platform,context =>
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
            });
    }
}
