using System;
using System.IO;
using System.Linq;
using Android.Content.Res;

namespace BEngine.Platform.AndroidNative;

/// <summary>Extracts selected Android assets when a consumer requires physical files.</summary>
public static class AndroidAssetFiles
{
    public static void ExtractDirectory(AssetManager assets, string assetPath, string destination,
        Predicate<string>? skip = null)
    {
        ArgumentNullException.ThrowIfNull(assets);
        if (skip?.Invoke(assetPath) == true) return;
        var children = assets.List(assetPath) ?? [];
        if (children.Length == 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var input = assets.Open(assetPath);
            var temporary = destination + ".extracting";
            using (var output = File.Create(temporary)) input.CopyTo(output);
            File.Move(temporary, destination, overwrite: true);
            return;
        }
        Directory.CreateDirectory(destination);
        foreach (var child in children.Order(StringComparer.Ordinal))
            ExtractDirectory(assets, $"{assetPath}/{child}", Path.Combine(destination, child), skip);
    }
}
