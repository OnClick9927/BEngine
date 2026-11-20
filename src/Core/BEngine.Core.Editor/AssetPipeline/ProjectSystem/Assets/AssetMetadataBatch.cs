using BEngine.Platform;
using BEngine.Editor;

namespace BEngine.ProjectSystem.Editor;

// One scan only. Workers read bytes/text; YAML converters and importers still
// execute on the scan's owning thread. The budget includes retained UTF-16 text.
internal sealed class AssetMetadataBatch : IDisposable
{
    private sealed record Entry(long Length, DateTime Stamp, string Text);
    private readonly FileUtility _files;
    private readonly PathUtility _paths;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private long _reserved;
    internal long RetainedBytes => _entries.Values.Sum(entry => (long)entry.Text.Length * sizeof(char));

    internal AssetMetadataBatch(FileUtility files, PathUtility paths, IEnumerable<string> sourcePaths,
        CancellationToken cancellationToken, long budget = 32 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(budget);
        _files = files;
        _paths = paths;
        var inputs = ((((global::System.Collections.Generic.IEnumerable<string>)(sourcePaths.Select(path => paths.GetFullPath(path) + ".meta"))), (global::System.Collections.Generic.IEqualityComparer<string>? )(StringComparer.Ordinal)) switch
        {
            (string[] { Length: 0 }, _) => global::System.Array.Empty<string>(),
            var (distinctItems978, distinctComparer978) => distinctItems978.ToHashSet<string>(distinctComparer978).ToArray()});
        var results = EditorWorkBatch.Run(inputs.Length, index =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = inputs[index];
            try
            {
                if (!files.FileExists(path)) return null;
                var length = files.GetFileLength(path);
                if (length > budget / sizeof(char)) return null;
                var reserve = length * sizeof(char);
                if (Interlocked.Add(ref _reserved, reserve) > budget)
                { Interlocked.Add(ref _reserved, -reserve); return null; }
                try
                {
                    var stamp = files.GetLastWriteTimeUtc(path);
                    var text = files.ReadAllText(path);
                    if ((long)text.Length * sizeof(char) > reserve || files.GetFileLength(path) != length ||
                        files.GetLastWriteTimeUtc(path) != stamp)
                    { Interlocked.Add(ref _reserved, -reserve); return null; }
                    return new Entry(length, stamp, text);
                }
                catch
                {
                    Interlocked.Add(ref _reserved, -reserve);
                    throw;
                }
            }
            // Creation/repair and its diagnostics remain on the ordinary import
            // path. In particular, a vanished file must not fail the whole batch.
            catch (IOException) { return null; }
        }, null, cancellationToken);
        for (var index = 0; index < inputs.Length; index++)
            if (results[index] is { } entry) _entries.Add(inputs[index], entry);
    }

    internal string? Take(string path)
    {
        path = _paths.GetFullPath(path);
        if (!_entries.Remove(path, out var entry)) return null;
        return _files.GetFileLength(path) == entry.Length && _files.GetLastWriteTimeUtc(path) == entry.Stamp
            ? entry.Text : null;
    }

    // Importers can write other metadata, including writes preserving stamps.
    internal void Invalidate() => _entries.Clear();
    public void Dispose() => _entries.Clear();
}
