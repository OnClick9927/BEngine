using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using BEngine.Editor;

namespace BEngine.ProjectSystem;

// Reuses immutable, verified on-disk images across editor instances. An open
// reader lease belongs to the collectible context, not to the manager's shorter
// lifetime: Unload() does not mean the context has actually been collected.
internal static class PackageImageCache
{
    private const string LeaseName = ".lease";
    private const int RetainedVersions = 4;
    private static readonly ConditionalWeakTable<object, IDisposable> ContextLeases = new();

    internal sealed record Image(string Root, IDisposable Lease);
    internal sealed record Source(string Name, byte[] Bytes);
    internal sealed record SourceFile(string Name, string Path);

    internal static Image Publish(SourceFile[] sources, string stagingRoot, string packageRoot)
    {
        var files = EditorHostServices.FileSystem;
        var paths = EditorHostServices.Paths;
        // A cache hit needs no retained source buffers or newly copied PE files.
        // Source I/O errors still propagate. A miss captures exact bytes and
        // computes their identity again, so concurrent source edits cannot put
        // new bytes under a key calculated from an earlier stream read.
        var fingerprints = sources.OrderBy(source => source.Name, StringComparer.Ordinal)
            .Select(source => new Fingerprint(source.Name, Hash(source.Path))).ToArray();
        files.CreateDirectory(packageRoot);
        using (AcquirePublicationLock(paths.Combine(packageRoot, ".publish.lock")))
        {
            var root = paths.Combine(packageRoot, Identity(fingerprints));
            var lease = TryOpenLease(root);
            if (lease is not null)
            {
                try
                {
                    if (Matches(root, fingerprints))
                    {
                        files.SetLastWriteTimeUtc(paths.Combine(root, LeaseName), EditorHostServices.Platform.UtcNow.UtcDateTime);
                        Prune(packageRoot, root);
                        return new(root, lease);
                    }
                }
                catch { lease.Dispose(); throw; }
                lease.Dispose();
            }
        }
        return Publish((sources is { } projectionSource2319 ? projectionSource2319.Length == 0 ? global::System.Array.Empty<global::BEngine.ProjectSystem.PackageImageCache.Source>() : global::System.Array.ConvertAll<global::BEngine.ProjectSystem.PackageImageCache.SourceFile, global::BEngine.ProjectSystem.PackageImageCache.Source>(projectionSource2319, source => new Source(source.Name, files.ReadAllBytes(source.Path))) : throw new global::System.ArgumentNullException("source")),
            stagingRoot, packageRoot);
    }

    internal static Image Publish(string stagingRoot, string packageRoot) => Publish(
        EditorHostServices.FileSystem.EnumerateFiles(stagingRoot)
            .Select(path => new Source(EditorHostServices.Paths.GetFileName(path),
                EditorHostServices.FileSystem.ReadAllBytes(path))).ToArray(), stagingRoot, packageRoot);

    internal static Image Publish(Source[] sources, string stagingRoot, string packageRoot)
    {
        var files = EditorHostServices.FileSystem;
        var paths = EditorHostServices.Paths;
        var fingerprints = sources.OrderBy(source => source.Name, StringComparer.Ordinal)
            .Select(source => new Fingerprint(source.Name, SHA256.HashData(source.Bytes))).ToArray();
        var key = Identity(fingerprints);
        files.CreateDirectory(packageRoot);
        using var publication = AcquirePublicationLock(paths.Combine(packageRoot, ".publish.lock"));
        var root = paths.Combine(packageRoot, key);
        IDisposable? lease = null;
        try
        {
            if (files.DirectoryExists(root))
            {
                lease = TryOpenLease(root);
                if (lease is null || !Matches(root, fingerprints))
                {
                    lease?.Dispose();
                    lease = null;
                    // Never repair bytes under an assembly still used by a live
                    // context. Its own lease excludes deletion and replacement.
                    if (!TryDelete(root)) root += "-" + Guid.NewGuid().ToString("N");
                }
            }
            if (lease is null)
            {
                // Readers acquire this same publication lock and verify every file.
                // Publish directly into the immutable generation, with the lease as
                // the final completion marker. Moving freshly written PE directories
                // can fail on Windows while an IDE or scanner is inspecting them.
                // A crashed partial generation has no lease and is never reused.
                files.CreateDirectory(root);
                foreach (var source in sources)
                    files.WriteAllBytes(paths.Combine(root, source.Name), source.Bytes);
                files.WriteAllText(paths.Combine(root, LeaseName), "");
                lease = OpenLease(root);
            }
            if (files.DirectoryExists(stagingRoot))
            {
                try { files.DeleteDirectory(stagingRoot, true); }
                // This private shadow staging directory is not part of the published
                // image. Its owner also retries cleanup after the context is disposed.
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            files.SetLastWriteTimeUtc(paths.Combine(root, LeaseName), EditorHostServices.Platform.UtcNow.UtcDateTime);
            Prune(packageRoot, root);
            return new(root, lease);
        }
        catch
        {
            lease?.Dispose();
            throw;
        }
    }

    internal static void RetainUntilCollected(WeakReference lifetime, IDisposable lease)
    {
        if (lifetime.Target is { } owner) ContextLeases.Add(owner, lease);
        else lease.Dispose();
    }

    private static byte[] Hash(string path)
    {
        using var stream = EditorHostServices.FileSystem.OpenRead(path);
        return SHA256.HashData(stream);
    }

    private static bool Matches(string root, Fingerprint[] expected)
    {
        try { return MatchesCore(root, expected); }
        // Only cached image errors become a miss. The original source snapshot
        // and required publication are outside this recovery boundary.
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static bool MatchesCore(string root, Fingerprint[] expected)
    {
        var files = EditorHostServices.FileSystem;
        var paths = EditorHostServices.Paths;
        var actual = files.EnumerateFiles(root).Where(path => paths.GetFileName(path) != LeaseName)
            .OrderBy(paths.GetFileName, StringComparer.Ordinal).ToArray();
        if (actual.Length != expected.Length || files.EnumerateDirectories(root).Any()) return false;
        for (var index = 0; index < actual.Length; index++)
            if (paths.GetFileName(actual[index]) != expected[index].Name ||
                !Hash(actual[index]).AsSpan().SequenceEqual(expected[index].Digest)) return false;
        return true;
    }

    private static Stream OpenLease(string root) => EditorHostServices.FileSystem.OpenFile(
        EditorHostServices.Paths.Combine(root, LeaseName), FileMode.Open, FileAccess.Read,
        FileShare.Read, 1, FileOptions.None);

    private static Stream? TryOpenLease(string root)
    {
        try { return OpenLease(root); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static string Identity(Fingerprint[] fingerprints)
    {
        using var identity = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in fingerprints)
        {
            identity.AppendData(Encoding.UTF8.GetBytes(file.Name));
            identity.AppendData([0]);
            identity.AppendData(file.Digest);
        }
        return Convert.ToHexString(identity.GetHashAndReset());
    }

    private static Stream AcquirePublicationLock(string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return EditorHostServices.FileSystem.OpenFile(path, FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None);
            }
            catch (IOException error) when (attempt < 200 && (error.HResult & 0xffff) is 32 or 33)
            {
                Thread.Sleep(15);
            }
        }
    }

    // Publication and acquisition both hold the package lock. After the exclusive
    // lease succeeds, no new context can race between the close and deletion.
    private static bool TryDelete(string root)
    {
        var files = EditorHostServices.FileSystem;
        var leasePath = EditorHostServices.Paths.Combine(root, LeaseName);
        try
        {
            if (files.FileExists(leasePath))
            {
                using var exclusive = files.OpenFile(leasePath, FileMode.Open, FileAccess.ReadWrite,
                    FileShare.None, 1, FileOptions.None);
            }
            files.DeleteDirectory(root, true);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static void Prune(string packageRoot, string current)
    {
        var files = EditorHostServices.FileSystem;
        var paths = EditorHostServices.Paths;
        string[] candidates;
        try { candidates = files.EnumerateDirectories(packageRoot).ToArray(); }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }
        var previous = new List<(string Root, DateTime Used)>();
        foreach (var root in candidates)
        {
            if (root.Equals(current, StringComparison.OrdinalIgnoreCase)) continue;
            try { previous.Add((root, files.GetLastWriteTimeUtc(paths.Combine(root, LeaseName)))); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        // At most four idle generations survive. Active versions may temporarily
        // exceed this bound and will be retried on a later preparation.
        foreach (var entry in previous.OrderByDescending(entry => entry.Used).Skip(RetainedVersions - 1))
            TryDelete(entry.Root);
    }

    private sealed record Fingerprint(string Name, byte[] Digest);
}
