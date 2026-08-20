using System.Reflection;

namespace BEngine.Editor;

public class AssetImporter : BObject
{
    public string assetPath { get; internal set; } = string.Empty;
    public ulong assetTimeStamp => File.Exists(FullPath)
        ? unchecked((ulong)File.GetLastWriteTimeUtc(FullPath).Ticks)
        : 0;
    public string userData { get; set; } = string.Empty;

    public static AssetImporter? GetAtPath(string path)
    {
        var record = EditorBridge.Host?.GetAsset(path);
        if (record is null) return null;
        return new AssetImporter { name = Path.GetFileName(path), assetPath = record.Value.AssetPath };
    }

    public void SaveAndReimport() => AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

    private string FullPath => EditorBridge.Host is { } host
        ? Path.GetFullPath(Path.Combine(host.ProjectRootPath, assetPath.Replace('/', Path.DirectorySeparatorChar)))
        : string.Empty;
}
