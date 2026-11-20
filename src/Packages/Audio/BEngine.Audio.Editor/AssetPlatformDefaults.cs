using System.Runtime.CompilerServices;
using BEngine.Build;
namespace BEngine.Editor;
internal static class AudioPlatformDefaults
{
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Register package-owned audio import policies.")] internal static void Register()
    {
        foreach(var platform in new[]{BuildTargetPlatform.Android,BuildTargetPlatform.IOS})
            PlatformAssetBuildPipeline.Register(platform,context =>
            {
            if (context.Importer == "AudioImporter")
            {
                // Keep large music/ambience streams out of the decoded PCM heap;
                // short authored cues retain their load policy and low latency.
                if (context.SourceBytes > 512 * 1024) context.Settings["loadType"] = "Streaming";
                context.Settings["loadInBackground"] = "True";
            }
            });
    }
}
