using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using BEngine.Platform;

namespace BEngine.ProjectSystem;

public static partial class RuntimeManagedCodeReleaseInputCollector
{
    private static void AddExternalManagedDependencies(Dictionary<string, Candidate> candidates,
        FileUtility fileSystem, PathUtility paths)
    {
        // Core's contract dependencies must retain their host identity. Optional package
        // dependencies must come from that package's compiled output, not the editor process.
        var hostContracts = typeof(BObject).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!).Append(typeof(BObject).Assembly.GetName().Name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<Candidate>(candidates.Values.OrderBy(item => item.Name, StringComparer.Ordinal));
        var inspected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (pending.TryDequeue(out var owner))
        {
            if (!inspected.Add(paths.GetFullPath(owner.AssemblyPath))) continue;
            var name = owner.Name;
            var directory = paths.GetDirectoryName(owner.AssemblyPath)!;
            var dependencies = owner.Dependencies.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var reference in ReadReferences(owner.AssemblyPath, fileSystem))
            {
                if (reference.Equals(name, StringComparison.OrdinalIgnoreCase) || hostContracts.Contains(reference)) continue;
                if (reference.IndexOfAny(['/', '\\', ':']) >= 0 || reference is "." or "..")
                    throw new InvalidDataException($"Invalid assembly reference '{reference}' in '{owner.AssemblyPath}'.");
                var path = paths.Combine(directory, reference + ".dll");
                if (candidates.TryGetValue(reference, out var existing))
                {
                    if (existing.Origin.StartsWith("external:", StringComparison.Ordinal) && fileSystem.FileExists(path) &&
                        !ComputeFileHash(path, fileSystem).Equals(existing.AssemblySha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Runtime dependency '{reference}' has conflicting binaries in '{existing.AssemblyPath}' and '{path}'.");
                    if (existing.Origin.StartsWith("external:", StringComparison.Ordinal) && fileSystem.FileExists(path))
                        pending.Enqueue(existing with { AssemblyPath = path });
                    dependencies.Add(reference);
                    continue;
                }
                // Framework/host references are not copied to package compilation outputs.
                // Only actual adjacent managed runtime assets belong in the release.
                if (!fileSystem.FileExists(path)) continue;
                var hash = ComputeFileHash(path, fileSystem);
                var external = CreateCandidate(reference, hash[..24], path, [], "external:" + owner.Origin, fileSystem, paths);
                candidates.Add(reference, external); pending.Enqueue(external); dependencies.Add(reference);
            }
            candidates[name] = candidates[name] with { Dependencies = dependencies.OrderBy(value => value, StringComparer.Ordinal).ToArray() };
        }
    }

    private static string[] ReadReferences(string path, FileUtility fileSystem)
    {
        using var stream = fileSystem.OpenRead(path);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        return metadata.AssemblyReferences.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }
}
