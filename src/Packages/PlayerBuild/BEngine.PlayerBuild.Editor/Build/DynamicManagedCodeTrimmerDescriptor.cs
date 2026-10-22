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
        "BEngine.Player",
        "BEngine.AssetBundles", "BEngine.Diagnostics", "BEngine.HotUpdate", "BEngine.Networking",
        "BEngine.Platform.DotNet", "SkiaSharp", "SkiaSharp.HarfBuzz", "HarfBuzzSharp"
    };

    internal static IReadOnlyList<string> CollectAssemblyRoots(IEnumerable<string> assemblyPaths)
        => CollectTypeRoots(assemblyPaths).Keys.ToArray();

    internal static bool IsHostAssembly(string name) => StaticallyRootedAssemblyNames.Contains(name);

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> CollectTypeRoots(
        IEnumerable<string> assemblyPaths, bool full = false)
    {
        ArgumentNullException.ThrowIfNull(assemblyPaths);
        var pathComparer = EditorHostServices.Platform.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var snapshots = assemblyPaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(EditorHostServices.Paths.GetFullPath)
            .Distinct(pathComparer)
            .Select(ReadAssembly)
            .ToArray();
        if (snapshots.Length == 0)
            throw new InvalidOperationException(
                "Conservative Player trimming requires at least one runtime managed-code assembly input.");

        var dynamicAssemblyNames = snapshots
            .Select(static snapshot => snapshot.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var platformAssemblies = GetTrustedPlatformAssemblies();
        var roots = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var snapshot in snapshots.Where(snapshot =>
                     !StaticallyRootedAssemblyNames.Contains(snapshot.Name)))
        {
            foreach (var type in snapshot.TypeReferences)
            {
                if (!ShouldPreserve(type.AssemblyName, dynamicAssemblyNames, full)) continue;
                var resolved = ResolveForwardedImplementation(
                    type.AssemblyName, type.FullName, platformAssemblies);
                AddTypeRoot(roots, type, dynamicAssemblyNames, full);
                if (resolved != type) AddTypeRoot(roots, resolved, dynamicAssemblyNames, full);
            }
        }

        return roots
            .OrderBy(static item => item.Key, StringComparer.Ordinal)
            .ToDictionary(
                static item => item.Key,
                static item => (IReadOnlyList<string>)item.Value
                    .OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static void AddTypeRoot(
        IDictionary<string, HashSet<string>> roots,
        TypeReferenceSnapshot type,
        IReadOnlySet<string> dynamicAssemblyNames, bool full)
    {
        if (!ShouldPreserve(type.AssemblyName, dynamicAssemblyNames, full)) return;
        if (!roots.TryGetValue(type.AssemblyName, out var types))
        {
            types = new HashSet<string>(StringComparer.Ordinal);
            roots.Add(type.AssemblyName, types);
        }
        types.Add(type.FullName);
    }

    internal static string Write(string descriptorPath, IEnumerable<string> assemblyPaths, bool full = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptorPath);
        if (full)
        {
            var fullPath = EditorHostServices.Paths.GetFullPath(descriptorPath);
            EditorHostServices.FileSystem.CreateDirectory(EditorHostServices.Paths.GetDirectoryName(fullPath)!);
            new System.Xml.Linq.XDocument(PlayerHostContractRoots.Build(assemblyPaths)).Save(fullPath);
            return fullPath;
        }
        var roots = CollectTypeRoots(assemblyPaths, full);
        var path = EditorHostServices.Paths.GetFullPath(descriptorPath);
        EditorHostServices.FileSystem.CreateDirectory(EditorHostServices.Paths.GetDirectoryName(path)!);
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace
        };
        using var output = EditorHostServices.FileSystem.OpenFile(path, FileMode.Create,
            FileAccess.Write, FileShare.None, 4096, FileOptions.SequentialScan);
        using var writer = XmlWriter.Create(output, settings);
        writer.WriteStartDocument();
        writer.WriteStartElement("linker");
        foreach (var root in roots)
        {
            writer.WriteStartElement("assembly");
            writer.WriteAttributeString("fullname", root.Key);
            foreach (var type in root.Value)
            {
                writer.WriteStartElement("type");
                writer.WriteAttributeString("fullname", type);
                writer.WriteAttributeString("preserve", "all");
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteEndDocument();
        return path;
    }

    private static AssemblySnapshot ReadAssembly(string path)
    {
        if (!EditorHostServices.FileSystem.FileExists(path))
            throw new FileNotFoundException("A runtime managed-code assembly input is missing.", path);
        try
        {
            using var stream = EditorHostServices.FileSystem.OpenFile(path, FileMode.Open,
                FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
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
            var typeReferences = metadata.TypeReferences
                .Select(handle => ReadTypeReference(metadata, handle, name))
                .Where(static reference => reference is not null)
                .Select(static reference => reference!)
                .Distinct()
                .ToArray();
            return new AssemblySnapshot(name, references, typeReferences);
        }
        catch (BadImageFormatException exception)
        {
            throw new InvalidDataException(
                $"Runtime managed-code input '{path}' is not a valid managed PE assembly.", exception);
        }
    }

    private static bool ShouldPreserve(string name, IReadOnlySet<string> dynamicAssemblyNames, bool full) =>
        full && StaticallyRootedAssemblyNames.Contains(name) ||
        !dynamicAssemblyNames.Contains(name) && !StaticallyRootedAssemblyNames.Contains(name);

    private static TypeReferenceSnapshot? ReadTypeReference(
        MetadataReader metadata,
        TypeReferenceHandle handle,
        string currentAssemblyName)
    {
        var assemblyName = ResolveTypeReferenceAssembly(
            metadata, handle, currentAssemblyName, []);
        if (string.IsNullOrWhiteSpace(assemblyName)) return null;
        var fullName = ResolveTypeReferenceFullName(metadata, handle, []);
        return string.IsNullOrWhiteSpace(fullName)
            ? null
            : new TypeReferenceSnapshot(assemblyName, fullName);
    }

    private static string? ResolveTypeReferenceAssembly(
        MetadataReader metadata,
        TypeReferenceHandle handle,
        string currentAssemblyName,
        HashSet<TypeReferenceHandle> visited)
    {
        if (!visited.Add(handle))
            throw new InvalidDataException("A managed assembly contains a cyclic type-reference scope.");
        var scope = metadata.GetTypeReference(handle).ResolutionScope;
        return scope.Kind switch
        {
            HandleKind.AssemblyReference => metadata.GetString(metadata.GetAssemblyReference(
                (AssemblyReferenceHandle)scope).Name),
            HandleKind.TypeReference => ResolveTypeReferenceAssembly(
                metadata, (TypeReferenceHandle)scope, currentAssemblyName, visited),
            HandleKind.ModuleDefinition or HandleKind.ModuleReference => currentAssemblyName,
            _ => null
        };
    }

    private static string ResolveTypeReferenceFullName(
        MetadataReader metadata,
        TypeReferenceHandle handle,
        HashSet<TypeReferenceHandle> visited)
    {
        if (!visited.Add(handle))
            throw new InvalidDataException("A managed assembly contains a cyclic nested type reference.");
        var type = metadata.GetTypeReference(handle);
        var name = metadata.GetString(type.Name);
        if (type.ResolutionScope.Kind == HandleKind.TypeReference)
            return ResolveTypeReferenceFullName(
                       metadata, (TypeReferenceHandle)type.ResolutionScope, visited) + "/" + name;
        var @namespace = metadata.GetString(type.Namespace);
        return string.IsNullOrWhiteSpace(@namespace) ? name : @namespace + "." + name;
    }

    private static IReadOnlyDictionary<string, string> GetTrustedPlatformAssemblies()
    {
        var value = EditorHostServices.Execution.GetRuntimeData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(value))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var assemblies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in value.Split(EditorHostServices.Paths.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                     .Select(EditorHostServices.Paths.GetFullPath)
                     .OrderBy(static path => path, StringComparer.Ordinal))
        {
            if (!EditorHostServices.FileSystem.FileExists(path)) continue;
            try
            {
                var name = ReadAssembly(path).Name;
                if (!string.IsNullOrWhiteSpace(name)) assemblies.TryAdd(name, path);
            }
            catch (BadImageFormatException)
            {
                // The runtime owns this list; ignore a non-managed host entry if one is present.
            }
        }
        return assemblies;
    }

    private static TypeReferenceSnapshot ResolveForwardedImplementation(
        string assemblyName,
        string fullName,
        IReadOnlyDictionary<string, string> platformAssemblies)
    {
        var current = assemblyName;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (visited.Add(current) && platformAssemblies.TryGetValue(current, out var path))
        {
            var target = ReadForwardedAssemblyName(path, fullName);
            if (string.IsNullOrWhiteSpace(target) ||
                target.Equals(current, StringComparison.OrdinalIgnoreCase)) break;
            current = target;
        }
        return new TypeReferenceSnapshot(current, fullName);
    }

    private static string? ReadForwardedAssemblyName(string path, string fullName)
    {
        try
        {
            using var stream = EditorHostServices.FileSystem.OpenFile(path, FileMode.Open,
                FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
            using var peReader = new PEReader(stream, PEStreamOptions.PrefetchMetadata);
            if (!peReader.HasMetadata) return null;
            var metadata = peReader.GetMetadataReader();
            foreach (var handle in metadata.ExportedTypes)
            {
                if (!ResolveExportedTypeFullName(metadata, handle, [])
                        .Equals(fullName, StringComparison.Ordinal)) continue;
                return ResolveForwardedAssemblyName(
                    metadata, metadata.GetExportedType(handle).Implementation, []);
            }
            return null;
        }
        catch (BadImageFormatException exception)
        {
            throw new InvalidDataException(
                $"Trusted platform assembly '{path}' is not a valid managed PE assembly.", exception);
        }
    }

    private static string ResolveExportedTypeFullName(
        MetadataReader metadata,
        ExportedTypeHandle handle,
        HashSet<ExportedTypeHandle> visited)
    {
        if (!visited.Add(handle))
            throw new InvalidDataException("A trusted platform assembly contains a cyclic exported type.");
        var type = metadata.GetExportedType(handle);
        var name = metadata.GetString(type.Name);
        if (type.Implementation.Kind == HandleKind.ExportedType)
            return ResolveExportedTypeFullName(
                       metadata, (ExportedTypeHandle)type.Implementation, visited) + "/" + name;
        var @namespace = metadata.GetString(type.Namespace);
        return string.IsNullOrWhiteSpace(@namespace) ? name : @namespace + "." + name;
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

    private sealed record AssemblySnapshot(
        string Name,
        IReadOnlyList<string> References,
        IReadOnlyList<TypeReferenceSnapshot> TypeReferences);

    private sealed record TypeReferenceSnapshot(string AssemblyName, string FullName);
}
