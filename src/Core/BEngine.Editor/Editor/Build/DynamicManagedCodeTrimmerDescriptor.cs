using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Xml;

namespace BEngine.Editor;

internal static class DynamicManagedCodeTrimmerDescriptor
{
    private static readonly HashSet<string> StaticallyRootedAssemblyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "BEngine",
        "BEngine.Player"
    };

    internal static IReadOnlyList<string> CollectAssemblyRoots(IEnumerable<string> assemblyPaths)
    {
        ArgumentNullException.ThrowIfNull(assemblyPaths);
        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var snapshots = assemblyPaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(pathComparer)
            .Select(ReadAssembly)
            .ToArray();
        if (snapshots.Length == 0)
            throw new InvalidOperationException(
                "Conservative Player trimming requires at least one runtime managed-code assembly input.");

        var dynamicAssemblyNames = snapshots
            .Select(static snapshot => snapshot.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roots = snapshots
            .SelectMany(static snapshot => snapshot.References)
            .Where(name => ShouldPreserve(name, dynamicAssemblyNames))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        AddForwardedImplementationRoots(roots, dynamicAssemblyNames);
        return roots.OrderBy(static name => name, StringComparer.Ordinal).ToArray();
    }

    internal static string Write(string descriptorPath, IEnumerable<string> assemblyPaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptorPath);
        var roots = CollectAssemblyRoots(assemblyPaths);
        var path = Path.GetFullPath(descriptorPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace
        };
        using var writer = XmlWriter.Create(path, settings);
        writer.WriteStartDocument();
        writer.WriteStartElement("linker");
        foreach (var root in roots)
        {
            writer.WriteStartElement("assembly");
            writer.WriteAttributeString("fullname", root);
            writer.WriteAttributeString("preserve", "all");
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteEndDocument();
        return path;
    }

    private static AssemblySnapshot ReadAssembly(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("A runtime managed-code assembly input is missing.", path);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var peReader = new PEReader(stream, PEStreamOptions.PrefetchMetadata);
            if (!peReader.HasMetadata)
                throw new InvalidDataException($"Runtime managed-code input '{path}' has no CLR metadata.");
            var metadata = peReader.GetMetadataReader();
            if (!metadata.IsAssembly)
                throw new InvalidDataException($"Runtime managed-code input '{path}' is not an assembly.");
            var name = metadata.GetString(metadata.GetAssemblyDefinition().Name);
            var references = metadata.AssemblyReferences
                .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
                .Where(static reference => !string.IsNullOrWhiteSpace(reference))
                .ToArray();
            return new AssemblySnapshot(name, references);
        }
        catch (BadImageFormatException exception)
        {
            throw new InvalidDataException(
                $"Runtime managed-code input '{path}' is not a valid managed PE assembly.", exception);
        }
    }

    private static bool ShouldPreserve(string name, IReadOnlySet<string> dynamicAssemblyNames) =>
        !dynamicAssemblyNames.Contains(name) &&
        !StaticallyRootedAssemblyNames.Contains(name);

    private static void AddForwardedImplementationRoots(
        ISet<string> roots,
        IReadOnlySet<string> dynamicAssemblyNames)
    {
        var platformAssemblies = GetTrustedPlatformAssemblies();
        if (platformAssemblies.Count == 0) return;
        var pending = new Queue<string>(roots.OrderBy(static name => name, StringComparer.Ordinal));
        var inspected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (pending.TryDequeue(out var assemblyName))
        {
            if (!inspected.Add(assemblyName) ||
                !platformAssemblies.TryGetValue(assemblyName, out var assemblyPath)) continue;
            foreach (var target in ReadForwardedAssemblyNames(assemblyPath))
            {
                if (!ShouldPreserve(target, dynamicAssemblyNames) || !roots.Add(target)) continue;
                pending.Enqueue(target);
            }
        }
    }

    private static IReadOnlyDictionary<string, string> GetTrustedPlatformAssemblies()
    {
        var value = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(value))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var assemblies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                     .Select(Path.GetFullPath)
                     .OrderBy(static path => path, StringComparer.Ordinal))
        {
            if (!File.Exists(path)) continue;
            try
            {
                var name = AssemblyName.GetAssemblyName(path).Name;
                if (!string.IsNullOrWhiteSpace(name)) assemblies.TryAdd(name, path);
            }
            catch (BadImageFormatException)
            {
                // The runtime owns this list; ignore a non-managed host entry if one is present.
            }
        }
        return assemblies;
    }

    private static IReadOnlyList<string> ReadForwardedAssemblyNames(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var peReader = new PEReader(stream, PEStreamOptions.PrefetchMetadata);
            if (!peReader.HasMetadata) return [];
            var metadata = peReader.GetMetadataReader();
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var handle in metadata.ExportedTypes)
            {
                var exportedType = metadata.GetExportedType(handle);
                var target = ResolveForwardedAssemblyName(metadata, exportedType.Implementation, []);
                if (!string.IsNullOrWhiteSpace(target)) targets.Add(target);
            }
            return targets.OrderBy(static name => name, StringComparer.Ordinal).ToArray();
        }
        catch (BadImageFormatException exception)
        {
            throw new InvalidDataException(
                $"Trusted platform assembly '{path}' is not a valid managed PE assembly.", exception);
        }
    }

    private static string? ResolveForwardedAssemblyName(
        MetadataReader metadata,
        EntityHandle implementation,
        HashSet<ExportedTypeHandle> visited)
    {
        if (implementation.Kind == HandleKind.AssemblyReference)
            return metadata.GetString(metadata.GetAssemblyReference(
                (AssemblyReferenceHandle)implementation).Name);
        if (implementation.Kind != HandleKind.ExportedType) return null;
        var exportedTypeHandle = (ExportedTypeHandle)implementation;
        if (!visited.Add(exportedTypeHandle))
            throw new InvalidDataException("A trusted platform assembly contains a cyclic type forwarder.");
        return ResolveForwardedAssemblyName(
            metadata,
            metadata.GetExportedType(exportedTypeHandle).Implementation,
            visited);
    }

    private sealed record AssemblySnapshot(string Name, IReadOnlyList<string> References);
}
