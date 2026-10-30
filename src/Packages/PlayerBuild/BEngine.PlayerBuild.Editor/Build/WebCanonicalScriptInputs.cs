using BEngine.ProjectSystem;

namespace BEngine.Editor;

/// <summary>Chooses one code image for packages shared by precompiled Web stages.</summary>
internal static class WebCanonicalScriptInputs
{
    internal static RuntimeManagedCodeReleaseInputSet Select(
        RuntimeManagedCodeReleaseInputSet builtIn,
        RuntimeManagedCodeReleaseInputSet originals,
        bool canonicalEntry = false)
    {
        ArgumentNullException.ThrowIfNull(builtIn);
        ArgumentNullException.ThrowIfNull(originals);
        var byName = originals.Assemblies.ToDictionary(static input => input.Name, StringComparer.OrdinalIgnoreCase);
        var selected = new Dictionary<string, RuntimeManagedCodeReleaseInput>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Full Web builds already linked startup and gameplay together. Other
        // builds retain their existing startup-only entry/original package policy.
        foreach (var input in builtIn.Assemblies)
        {
            if (input.Name.Equals(PlayerContentBuildPipeline.AotAssemblyName, StringComparison.OrdinalIgnoreCase))
            {
                var entry = canonicalEntry ? byName[input.Name] : input;
                selected.Add(input.Name, entry);
                if (canonicalEntry)
                    foreach (var dependency in entry.Dependencies) Include(dependency);
            }
            else Include(input.Name);
        }
        var names = selected.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var assemblies = selected.Values.OrderBy(static input => input.Name, StringComparer.Ordinal)
            .Select(input => input with
            {
                Dependencies = input.Dependencies.Where(names.Contains)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray()
            }).ToArray();
        // Resource roots are deliberately unchanged: canonical code identity is
        // resolved after asset discovery and does not root additional assets.
        return new RuntimeManagedCodeReleaseInputSet(assemblies, builtIn.PackageResources, builtIn.PackageResourceRoots);

        void Include(string name)
        {
            if (DynamicManagedCodeTrimmerDescriptor.IsHostAssembly(name)) return;
            if (name.Equals(PlayerContentBuildPipeline.AotAssemblyName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Canonical Web packages cannot depend on the AOT entry assembly.");
            if (visiting.Contains(name))
                throw new InvalidDataException($"Canonical Web script dependency cycle at '{name}'.");
            if (selected.ContainsKey(name)) return;
            if (!byName.TryGetValue(name, out var original))
                throw new InvalidDataException($"Canonical Web script dependency '{name}' has no release image.");
            if (original.Origin.StartsWith("project:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"AOT packages cannot depend on project script '{name}'.");
            selected.Add(name, original);
            visiting.Add(name);
            foreach (var dependency in original.Dependencies) Include(dependency);
            visiting.Remove(name);
        }
    }
}
