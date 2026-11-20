using EditorCapabilities = BEngine.Editor.EditorServices;
using BEngine.Platform;
using BEngine.Serialization;

namespace BEngine.Editor;

[RuntimeTypeAlias("BEngine.Editor.EditorAssetIcons, BEngine.Graphics.Editor")]
[RuntimeTypeAlias("BEngine.Editor.EditorAssetIcons, BEngine.Graphics.Editor, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null")]
public static class EditorAssetIcons
{
    public const string EmptyFolder = EditorBuiltinIcons.Assets.FolderEmpty;
    public const string ClosedFolder = EditorBuiltinIcons.Assets.FolderClosed;
    public const string OpenFolder = EditorBuiltinIcons.Assets.FolderOpen;
    public const string DefaultAsset = EditorBuiltinIcons.Assets.Default;

    public static string GetIconPath(string assetPath, bool expanded = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        if (EditorCapabilities.GetRequiredService<FileUtility>().DirectoryExists(assetPath))
        {
            if (!ContainsVisibleEntry(assetPath)) return EmptyFolder;
            return expanded ? OpenFolder : ClosedFolder;
        }

        var normalized = assetPath.Replace('\\', '/');
        if (normalized.EndsWith(".asset.yaml", StringComparison.OrdinalIgnoreCase) &&
            ResolveManagedAssetIcon(assetPath) is { } managedIcon) return managedIcon;
        if (AssetTypeRegistry.ResolveIconPath(assetPath) is { } registeredIcon) return registeredIcon;
        return normalized.ToLowerInvariant() switch
        {
            var path when path.EndsWith(".prefab.yaml") => EditorBuiltinIcons.Assets.Prefab,
            var path when path.EndsWith(".scene.yaml") => EditorBuiltinIcons.Assets.Scene,
            var path when path.EndsWith(".material.yaml") =>
                EditorBuiltinIcons.Assets.Material,
            var path when path.EndsWith(".asmdef.yaml") => EditorBuiltinIcons.Assets.Assembly,
            var path when path.EndsWith(".guiskin.yaml") => EditorBuiltinIcons.Assets.Skin,
            var path when path.EndsWith(".bpackage") => "Icons/Windows/PackageManager.png",
            var path when path.EndsWith(".shader") =>
                EditorBuiltinIcons.Assets.Shader,
            var path when path.EndsWith(".cs") => EditorBuiltinIcons.Assets.Script,
            var path when path.EndsWith(".html") || path.EndsWith(".htm") =>
                EditorBuiltinIcons.Assets.Markup,
            var path when path.EndsWith(".css") => EditorBuiltinIcons.Assets.Style,
            var path when path.EndsWith(".png") || path.EndsWith(".jpg") || path.EndsWith(".jpeg") ||
                              path.EndsWith(".bmp") || path.EndsWith(".gif") || path.EndsWith(".tga") ||
                              path.EndsWith(".webp") || path.EndsWith(".svg") => EditorBuiltinIcons.Assets.Image,
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

    private static string? ResolveManagedAssetIcon(string assetPath)
    {
        try
        {
            var fullPath = EditorCapabilities.GetRequiredService<PathUtility>().IsPathRooted(assetPath)
                ? assetPath
                : AssetDatabase.ResolveAssetPath(assetPath);
            if (!EditorCapabilities.GetRequiredService<FileUtility>().FileExists(fullPath)) return null;
            var typeName = ManagedAssetSerialization.GetSerializedTypeName(
                EditorCapabilities.GetRequiredService<FileUtility>().ReadAllText(fullPath));
            var type = typeName is null ? null : RuntimeTypeCache.ResolveSerializedType(typeName);
            return type is null ? null : EditorIconRegistry.GetIconPath(type);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            return null;
        }
    }

    public static bool ContainsVisibleEntry(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        try
        {
            bool __queryResult4441;
            {
                var __querySource4441 = EditorCapabilities.GetRequiredService<FileUtility>().EnumerateFileSystemEntries(directory);
                ArgumentNullException.ThrowIfNull(__querySource4441, "source");
                __queryResult4441 = false;
                var __querySource4441Type = __querySource4441.GetType();
                if (__querySource4441Type == typeof(string[]) || __querySource4441Type == typeof(System.Collections.Generic.List<string>))
                {
                    System.ReadOnlySpan<string> __querySource4441Span = __querySource4441Type == typeof(string[]) ? (string[])(object)__querySource4441 : System.Runtime.InteropServices.CollectionsMarshal.AsSpan((System.Collections.Generic.List<string>)(object)__querySource4441);
                    foreach (var __queryItem4441 in __querySource4441Span)
                    {
                        if ((!__queryItem4441.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
                        {
                            __queryResult4441 = true;
                            break;
                        }
                    }
                }
                else
                {
                    foreach (var __queryItem4441 in __querySource4441)
                    {
                        if ((!__queryItem4441.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
                        {
                            __queryResult4441 = true;
                            break;
                        }
                    }
                }
            }
            
            return __queryResult4441;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
