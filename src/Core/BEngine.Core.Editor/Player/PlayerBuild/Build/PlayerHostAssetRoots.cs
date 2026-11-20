using System.Xml.Linq;

namespace BEngine.Editor.Build;

/// <summary>Serialized hot-update assets are outside both the startup archive and the Host IL graph.</summary>
internal static class PlayerHostAssetRoots
{
    internal static string Write(string path, IEnumerable<string> assemblyPaths,
        IEnumerable<string> assetFiles, PlayerStrippingSettings settings)
    {
        var roots = Build(assemblyPaths, assetFiles, settings);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        new XDocument(roots).Save(path);
        return path;
    }

    internal static XElement Build(IEnumerable<string> assemblyPaths,
        IEnumerable<string> assetFiles, PlayerStrippingSettings settings)
    {
        var paths = ((((global::System.Collections.Generic.IEnumerable<string>)(assemblyPaths.Concat(PlayerSerializationRoots.HostAssemblyPaths).Append(typeof(Transform).Assembly.Location))), (global::System.Collections.Generic.IEqualityComparer<string>? )(StringComparer.OrdinalIgnoreCase)) switch
        {
            (string[] { Length: 0 }, _) => global::System.Array.Empty<string>(),
            var (distinctItems741, distinctComparer741) => distinctItems741.ToHashSet<string>(distinctComparer741).ToArray()});
        // Reuse the scene/prefab/DTO contract discovery: constructors, serializable
        // fields, properties and callbacks need the same metadata in every stage.
        var roots = PlayerManagedCodeTrimmer.BuildRoots(paths, assetFiles, settings,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        foreach (var assembly in roots.Elements("assembly").ToArray())
            if (!DynamicManagedCodeTrimmerDescriptor.IsHostAssembly((string)assembly.Attribute("fullname")!))
                assembly.Remove();
        return roots;
    }
}
