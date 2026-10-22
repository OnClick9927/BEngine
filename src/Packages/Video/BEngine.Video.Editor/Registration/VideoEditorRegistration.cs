using BEngine.Editor;
using BEngine.Platform;

namespace BEngine.Video.Editor;

internal static class VideoEditorRegistration
{
    private const string Icon = "Icons/Assets/AssetVideo.png";

    [InitializeOnLoadMethod]
    private static void Register()
    {
        foreach (var extension in new[] { ".mp4", ".m4v", ".mov", ".webm", ".avi", ".mkv", ".bvideo" })
            AssetTypeRegistry.Register<VideoClip>(extension, nameof(VideoClip),
                context => VideoClip.Load(context.ImportedPath), Icon, typeof(VideoImporter));
        EditorIconRegistry.Register(typeof(VideoClip), Icon);
        EditorIconRegistry.Register(typeof(VideoPlayer), Icon);
    }

    [MenuItem("Tools/Video/Configure FFmpeg...", false, 240)]
    private static void ConfigureFFmpeg() => EditorFileDialog.Open("Select ffmpeg.exe", EditorServices.GetRequiredService<PlatformUtility>().BaseDirectory,
        "FFmpeg (ffmpeg.exe)|ffmpeg.exe", path =>
        {
            if (!EditorServices.GetRequiredService<FileUtility>().FileExists(EditorServices.GetRequiredService<PathUtility>().Combine(EditorServices.GetRequiredService<PathUtility>().GetDirectoryName(path)!, "ffprobe.exe")))
                throw new FileNotFoundException("Select an FFmpeg installation containing ffprobe.exe.");
            EditorPrefs.SetString("BEngine.Video.FFmpegPath", path);
            AssetDatabase.Refresh();
        });
}
