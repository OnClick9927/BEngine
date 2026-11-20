using BEngine.Editor;
using BEngine.Platform;

namespace BEngine.ProjectSystem;

// Owns shadow files and uncommitted collectible contexts. File preparation and PE
// loading use bounded workers; executable initialization remains on the caller.
internal sealed class PackageShadowPreparation : IDisposable
{
    internal sealed record Entry(string PackageId, string[] AssemblyPaths, string ShadowRoot)
    {
        internal AssemblyScope? Context { get; set; }
        internal System.Reflection.Assembly[] Assemblies { get; set; } = [];
        internal string ImageRoot { get; set; } = ShadowRoot;
        internal IDisposable? ImageLease { get; set; }
    }
    private readonly FileUtility _files = EditorHostServices.FileSystem;
    private readonly PathUtility _paths = EditorHostServices.Paths;
    private readonly Dictionary<string, Entry> _pending;

    internal PackageShadowPreparation(Entry[] entries, IReadOnlySet<string> sharedAssemblies,
        Action<int, int>? progress = null, int? maximumConcurrency = null, string? imageCacheRoot = null)
    {
        _pending = BuildIndex1086(entries, StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.PackageShadowPreparation.Entry> BuildIndex1086(global::System.Collections.Generic.IEnumerable<global::BEngine.ProjectSystem.PackageShadowPreparation.Entry> indexSource1086, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer1086)
        {
            ArgumentNullException.ThrowIfNull(indexSource1086, "source");
            var knownCount = indexSource1086.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.PackageShadowPreparation.Entry>(indexComparer1086);
            global::System.ReadOnlySpan<global::BEngine.ProjectSystem.PackageShadowPreparation.Entry> values;
            if (knownCount && indexSource1086 is global::BEngine.ProjectSystem.PackageShadowPreparation.Entry[] array)
                values = array;
            else if (knownCount && indexSource1086 is global::System.Collections.Generic.List<global::BEngine.ProjectSystem.PackageShadowPreparation.Entry> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.PackageShadowPreparation.Entry>(capacity, indexComparer1086);
                foreach (var item in indexSource1086)
                    result.Add(IndexKey1086(item), item);
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.PackageShadowPreparation.Entry>(values.Length, indexComparer1086);
                foreach (var item in values)
                    result.Add(IndexKey1086(item), item);
                return result;
            }
        
            string IndexKey1086(global::BEngine.ProjectSystem.PackageShadowPreparation.Entry entry) => entry.PackageId;
        }
        try
        {
            EditorWorkBatch.Run(entries.Length, index =>
            {
                var entry = entries[index];
                using var measurement = EditorStartupTimings.Measure($"Package shadow copy: {entry.PackageId}");
                Prepare(entry, sharedAssemblies, imageCacheRoot);
                return index;
            }, progress, maximumConcurrency: maximumConcurrency);
        }
        catch { Dispose(); throw; }
    }

    internal Entry this[string packageId] => _pending[packageId];
    internal void Retain(string packageId) => _pending.Remove(packageId);

    private string Sanitize(string value)
    {
        var invalid = _paths.GetInvalidFileNameChars();
        return string.Create(value.Length, (Value: value, Invalid: invalid), static (destination, state) =>
        {
            for (var index = 0; index < destination.Length; index++)
            {
                var character = state.Value[index];
                destination[index] = state.Invalid.AsSpan().Contains(character) ? '_' : character;
            }
        });
    }

    private void Prepare(Entry entry, IReadOnlySet<string> sharedAssemblies, string? imageCacheRoot)
    {
        var sources = entry.AssemblyPaths;
        var shadowRoot = entry.ShadowRoot;
        var copies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Add(string source)
        {
            copies[_paths.GetFileName(source)] = source;
            var pdb = EditorHostServices.ChangeExtension(source, ".pdb");
            if (_files.FileExists(pdb)) copies[_paths.GetFileName(pdb)] = pdb;
        }
        foreach (var directory in sources.Select(path => _paths.GetDirectoryName(path)!)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        foreach (var file in _files.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            // Resolution already prefers hosted assemblies to local companions.
            var name = _paths.GetFileNameWithoutExtension(file);
            if (name is "BEngine.Core" or "BEngine.Core.Editor" or "BEngine.Player" || sharedAssemblies.Contains(name)) continue;
            Add(file);
        }
        // Explicit runtime/editor inputs retain priority over companion files.
        foreach (var source in sources) Add(source);
        if (imageCacheRoot is not null && sources.Length > 0)
        {
            // Hash stable inputs before creating any new PE files. Cache misses
            // capture exact bytes; only that bounded worker owns those buffers.
            var inputs = copies.Select(copy => new PackageImageCache.SourceFile(copy.Key, copy.Value)).ToArray();
            var image = PackageImageCache.Publish(inputs, shadowRoot,
                _paths.Combine(imageCacheRoot, Sanitize(entry.PackageId)));
            entry.ImageRoot = image.Root;
            entry.ImageLease = image.Lease;
            return;
        }
        _files.CreateDirectory(shadowRoot);
        foreach (var (name, source) in copies)
            _files.CopyFile(source, _paths.Combine(shadowRoot, name), true);
    }

    public void Dispose()
    {
        foreach (var entry in _pending.Values)
        {
            entry.ImageLease?.Dispose();
            entry.ImageLease = null;
            if (entry.Context is { } context)
            {
                RuntimeTypeCache.UnregisterAssemblies(context.Assemblies);
                context.Unload();
                entry.Context = null;
                entry.Assemblies = [];
            }
            try
            {
                if (_files.DirectoryExists(entry.ShadowRoot)) _files.DeleteDirectory(entry.ShadowRoot, true);
            }
            // A failed load can still hold a file. Startup's stale-shadow cleanup
            // retries after the collectible context has actually gone away.
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        _pending.Clear();
    }
}
