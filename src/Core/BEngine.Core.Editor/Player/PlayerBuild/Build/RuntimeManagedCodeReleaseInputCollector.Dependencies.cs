using BEngine.ProjectSystem;
using BEngine.Platform;

namespace BEngine.Editor.Build.ProjectSystem;

public static partial class RuntimeManagedCodeReleaseInputCollector
{
    private static void AddExternalManagedDependencies(Dictionary<string, Candidate> candidates,
        FileUtility fileSystem, PathUtility paths, AssemblyInputBatch assemblyInputs)
    {
        // Explicit host contracts retain their host identity. Other dependencies come
        // from the compiled output rather than arbitrary assemblies in the editor process.
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
            foreach (var reference in assemblyInputs.Read(owner.AssemblyPath).References)
            {
                if (reference.Equals(name, StringComparison.OrdinalIgnoreCase) || hostContracts.Contains(reference) ||
                    BEngine.Hosting.HostAssemblyRegistry.Find(reference) is not null) continue;
                if (reference.AsSpan(0, reference.Length).IndexOfAny('/', '\\', ':') >= 0 || reference is "." or "..")
                    throw new InvalidDataException($"Invalid assembly reference '{reference}' in '{owner.AssemblyPath}'.");
                var path = paths.Combine(directory, reference + ".dll");
                if (candidates.TryGetValue(reference, out var existing))
                {
                    if (existing.Origin.StartsWith("external:", StringComparison.Ordinal) && fileSystem.FileExists(path) &&
                        !assemblyInputs.Read(path).Sha256.Equals(existing.AssemblySha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Runtime dependency '{reference}' has conflicting binaries in '{existing.AssemblyPath}' and '{path}'.");
                    if (existing.Origin.StartsWith("external:", StringComparison.Ordinal) && fileSystem.FileExists(path))
                        pending.Enqueue(existing with { AssemblyPath = path });
                    dependencies.Add(reference);
                    continue;
                }
                // Framework/host references are not copied to package compilation outputs.
                // Only actual adjacent managed runtime assets belong in the release.
                if (!fileSystem.FileExists(path)) continue;
                var hash = assemblyInputs.Read(path).Sha256;
                var external = CreateCandidate(reference, hash[..24], path, [], "external:" + owner.Origin,
                    fileSystem, paths, assemblyInputs);
                candidates.Add(reference, external); pending.Enqueue(external); dependencies.Add(reference);
            }
            candidates[name] = candidates[name] with { Dependencies = dependencies.OrderBy(value => value, StringComparer.Ordinal).ToArray() };
        }
    }
}
