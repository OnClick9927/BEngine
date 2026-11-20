using BEngine.Editor.Documents;
using BEngine.ProjectSystem;

namespace BEngine.Launcher;

// Pure metadata checks matching the Core catalog, without loading Editor or
// rejecting unrelated packages that the release resolver intentionally omitted.
internal static class LauncherPackageValidation
{
    internal static void ValidateCompatibleReleases(IEnumerable<BPackageDefinition> definitions)
    {
        var identities = new Dictionary<string, (string Type, HashSet<string> Versions)>(StringComparer.OrdinalIgnoreCase);
        foreach (var release in definitions)
        {
            var document = release.Document;
            if (!identities.TryGetValue(document.Id, out var identity))
                identities.Add(document.Id, identity = (document.Type, new HashSet<string>(StringComparer.Ordinal)));
            if (!identity.Versions.Add(document.PackageVersion))
                throw new InvalidDataException($"Duplicate package '{document.Id}' version '{document.PackageVersion}' in '{release.Path}'.");
            if (identity.Type != document.Type)
                throw new InvalidDataException($"Package '{document.Id}' cannot change type between versions.");
        }
    }

    internal static void ValidateRequiredMetadata(IReadOnlyList<BPackageDefinition> releases,
        IEnumerable<string> requiredIds)
    {
        var required = requiredIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = BuildIndex1446(releases.Where(release => required.Contains(release.Document.Id)), StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.BPackageDefinition> BuildIndex1446(global::System.Collections.Generic.IEnumerable<global::BEngine.ProjectSystem.BPackageDefinition> indexSource1446, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer1446)
        {
            ArgumentNullException.ThrowIfNull(indexSource1446, "source");
            var knownCount = indexSource1446.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.BPackageDefinition>(indexComparer1446);
            global::System.ReadOnlySpan<global::BEngine.ProjectSystem.BPackageDefinition> values;
            if (knownCount && indexSource1446 is global::BEngine.ProjectSystem.BPackageDefinition[] array)
                values = array;
            else if (knownCount && indexSource1446 is global::System.Collections.Generic.List<global::BEngine.ProjectSystem.BPackageDefinition> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.BPackageDefinition>(capacity, indexComparer1446);
                foreach (var item in indexSource1446)
                    result.Add(IndexKey1446(item), item);
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.BPackageDefinition>(values.Length, indexComparer1446);
                foreach (var item in values)
                    result.Add(IndexKey1446(item), item);
                return result;
            }
        
            string IndexKey1446(global::BEngine.ProjectSystem.BPackageDefinition release) => release.Document.Id;
        }
        foreach (var id in required)
            if (!selected.ContainsKey(id))
                throw new InvalidDataException($"Required package '{id}' has no compatible, resolvable release.");

        var assemblies = new Dictionary<string, (string PackageId, string Kind)>(StringComparer.OrdinalIgnoreCase);
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var release in selected.Values)
        {
            AddAssembly(release.Document, "runtime", release.Document.Runtime);
            AddAssembly(release.Document, "editor", release.Document.Editor);
            foreach (var retired in release.Document.Replaces)
            {
                if (selected.ContainsKey(retired) || replacements.TryGetValue(retired, out var owner) &&
                    !owner.Equals(release.Document.Id, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Package replacement '{retired}' has conflicting owners.");
                replacements[retired] = release.Document.Id;
            }
        }

        void AddAssembly(PackageDefinitionDocument package, string kind, PackageAssemblyDocument? assembly)
        {
            if (assembly is null) return;
            foreach (var name in assembly.Aliases.Append(assembly.Assembly))
            {
                if (assemblies.TryGetValue(name, out var owner))
                    throw new InvalidDataException(
                        $"Assembly name or alias '{name}' is already owned by package '{owner.PackageId}' " +
                        $"({owner.Kind}) and cannot also be owned by '{package.Id}' ({kind}).");
                assemblies[name] = (package.Id, kind);
            }
        }
    }
}
