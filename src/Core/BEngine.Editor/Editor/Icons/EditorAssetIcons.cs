namespace BEngine.Editor;

public static class EditorAssetIcons
{
    public const string EmptyFolder = EditorBuiltinIcons.Assets.FolderEmpty;
    public const string ClosedFolder = EditorBuiltinIcons.Assets.FolderClosed;
    public const string OpenFolder = EditorBuiltinIcons.Assets.FolderOpen;
    public const string DefaultAsset = EditorBuiltinIcons.Assets.Default;

    public static string GetIconPath(string assetPath, bool expanded = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        if (Directory.Exists(assetPath))
        {
            if (!ContainsVisibleEntry(assetPath)) return EmptyFolder;
            return expanded ? OpenFolder : ClosedFolder;
        }

        if (AssetTypeRegistry.ResolveIconPath(assetPath) is { } registeredIcon) return registeredIcon;
        var normalized = assetPath.Replace('\\', '/');
        return normalized.ToLowerInvariant() switch
        {
            var path when path.EndsWith(".prefab.yaml") => EditorBuiltinIcons.Assets.Prefab,
            var path when path.EndsWith(".scene.yaml") => EditorBuiltinIcons.Assets.Scene,
            var path when path.EndsWith(".material.yaml") || path.EndsWith(".physics-material.yaml") =>
                EditorBuiltinIcons.Assets.Material,
            var path when path.EndsWith(".asmdef.yaml") => EditorBuiltinIcons.Assets.Assembly,
            var path when path.EndsWith(".bpackage") => "Icons/Windows/PackageManager.png",
            var path when path.EndsWith(".controller.yaml") || path.EndsWith(".anim.yaml") =>
                EditorBuiltinIcons.Assets.Animation,
            var path when path.EndsWith(".shader") || path.EndsWith(".shadergraph") =>
                EditorBuiltinIcons.Assets.Shader,
            var path when path.EndsWith(".cs") => EditorBuiltinIcons.Assets.Script,
            var path when path.EndsWith(".uxml") || path.EndsWith(".html") || path.EndsWith(".htm") =>
                EditorBuiltinIcons.Assets.Markup,
            var path when path.EndsWith(".uss") || path.EndsWith(".css") => EditorBuiltinIcons.Assets.Style,
            var path when path.EndsWith(".png") || path.EndsWith(".jpg") || path.EndsWith(".jpeg") ||
                              path.EndsWith(".bmp") || path.EndsWith(".gif") || path.EndsWith(".tga") ||
                              path.EndsWith(".webp") || path.EndsWith(".svg") => EditorBuiltinIcons.Assets.Image,
            var path when path.EndsWith(".wav") || path.EndsWith(".mp3") || path.EndsWith(".ogg") ||
                              path.EndsWith(".flac") => EditorBuiltinIcons.Assets.Audio,
            var path when path.EndsWith(".fbx") || path.EndsWith(".obj") || path.EndsWith(".gltf") ||
                              path.EndsWith(".glb") => EditorBuiltinIcons.Assets.Model,
            var path when path.EndsWith(".ttf") || path.EndsWith(".otf") || path.EndsWith(".woff") ||
                              path.EndsWith(".woff2") => EditorBuiltinIcons.Assets.Font,
            var path when path.EndsWith(".yaml") || path.EndsWith(".yml") || path.EndsWith(".json") ||
                              path.EndsWith(".xml") => EditorBuiltinIcons.Assets.Data,
            var path when path.EndsWith(".txt") || path.EndsWith(".md") || path.EndsWith(".log") =>
                EditorBuiltinIcons.Assets.Text,
            _ => DefaultAsset
        };
    }

    public static bool ContainsVisibleEntry(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        try
        {
            return Directory.EnumerateFileSystemEntries(directory)
                .Any(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
