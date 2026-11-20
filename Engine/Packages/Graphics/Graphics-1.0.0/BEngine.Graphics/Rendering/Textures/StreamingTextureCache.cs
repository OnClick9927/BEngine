using BEngine.Rendering.Rhi;
using BEngine.Platform;

namespace BEngine.Rendering;

internal sealed class StreamingTextureCache(IGraphicsDevice device, FileUtility files, Action? changed = null) : IDisposable
{
    private sealed class Entry
    {
        internal required string Path; internal DateTime Stamp; internal required TextureMipStream Index;
        internal required IGraphicsTexture2D Texture; internal int Level; internal int Requested; internal long Used; internal long LastFrame;
        internal TextureFilterMode Filter; internal TextureWrapMode Wrap;
        internal BTask<TextureImageData>? Pending; internal CancellationTokenSource? Cancellation;
        internal MemoryBudget.Lease? Reservation;
        internal MemoryBudget.Lease? PendingReservation;
    }
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal); private long _usage;
    private readonly Dictionary<string, PendingLoad> _pending = new(StringComparer.Ordinal);
    private long _residentBytes;
    private bool _disposed;
    internal IGraphicsTexture2D? Resolve(string source, string path, TextureFilterMode filter, TextureWrapMode wrap)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var stamp = files.GetLastWriteTimeUtc(path);
        if (_entries.TryGetValue(source, out var previous) && (previous.Path != path || previous.Stamp != stamp || previous.Filter != filter || previous.Wrap != wrap))
        { Remove(source, previous); previous = null; }
        if (_pending.TryGetValue(source, out var pending) &&
            (pending.Path != path || pending.Stamp != stamp || pending.Filter != filter || pending.Wrap != wrap))
        { CancelPending(source, pending); pending = null; }
        var entry = previous;
        if (entry is null)
        {
            if (BackgroundWorkScheduler.IsSupported)
            {
                if (pending is null)
                {
                    var cancellation = new BCancellationSource();
                    var task = LoadInitialAsync(path, cancellation.Token);
                    _pending[source] = pending = new PendingLoad(path, stamp, filter, wrap, cancellation, task);
                }
                if (!pending.Task.IsCompleted) return null;
                try
                {
                    var prepared = pending.Task.GetAwaiter().GetResult();
                    if (!Reserve(ResidentSize(prepared.Index, prepared.Index.MipCount - 1), source, out var reservation)) return null;
                    IGraphicsTexture2D initial;
                    try { initial = TextureUpload.Create(device, source, prepared.Data, filter, wrap); }
                    catch { reservation!.Dispose(); throw; }
                    entry = new()
                    {
                        Path = path, Stamp = stamp, Index = prepared.Index, Texture = initial,
                        Reservation = reservation, Level = prepared.Index.MipCount - 1,
                        Filter = filter, Wrap = wrap
                    };
                    _entries[source] = entry;
                    AdjustBytes(initial.Description.DataByteSize);
                    _pending.Remove(source);
                    pending.Cancellation.Dispose();
                    changed?.Invoke();
                }
                catch (BOperationCanceledException) { CancelPending(source, pending); return null; }
                catch (Exception error)
                {
                    CancelPending(source, pending);
                    Debug.LogWarning($"Streaming texture '{source}' could not be prepared: {error.Message}");
                    return null;
                }
            }
        }
        if (entry is null)
        {
            using var stream = files.OpenFile(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.RandomAccess);
            var index = new TextureMipStream(stream); var level = index.MipCount - 1;
            if (!Reserve(ResidentSize(index, level), source, out var reservation)) return null;
            IGraphicsTexture2D initial;
            try { initial = TextureUpload.Create(device, source, index.ReadLevels(stream, level), filter, wrap); }
            catch { reservation!.Dispose(); throw; }
            entry = new() { Path = path, Stamp = stamp, Index = index, Texture = initial, Reservation = reservation, Level = level, Filter = filter, Wrap = wrap }; _entries[source] = entry;
            AdjustBytes(initial.Description.DataByteSize);
            changed?.Invoke();
        }
        entry.Used = ++_usage;
        entry.LastFrame = BTaskScheduler.Main.FrameCount;
        var desired = TextureStreaming.DesiredMip(source, entry.Index.Width, entry.Index.Height, entry.Index.MipCount);
        var budget = Math.Max(0, TextureStreaming.memoryBudgetBytes);
        while (desired < entry.Index.MipCount - 1 && ResidentSize(entry.Index, desired) > budget) desired++;
        if (entry.Pending is { IsCompleted: true } complete)
        {
            entry.Pending = null; entry.Cancellation?.Dispose(); entry.Cancellation = null;
            var reservation = entry.PendingReservation; entry.PendingReservation = null;
            try
            {
                if (!complete.IsCanceled && entry.Requested == desired)
                {
                    var data = complete.GetAwaiter().GetResult();
                    var replacement = TextureUpload.Create(device, source, data, filter, wrap);
                    AdjustBytes((long)replacement.Description.DataByteSize - entry.Texture.Description.DataByteSize);
                    GraphicsTextureMemory.Retire(device, entry.Texture, entry.Reservation);
                    entry.Texture = replacement; entry.Reservation = reservation; reservation = null; entry.Level = desired;
                    changed?.Invoke();
                }
            }
            finally { reservation?.Dispose(); }
        }
        if (desired != entry.Level && (entry.Pending is null || entry.Requested != desired))
        {
            entry.Cancellation?.Cancel(); entry.Cancellation?.Dispose(); entry.PendingReservation?.Dispose();
            entry.Pending = null; entry.PendingReservation = null; entry.Cancellation = null;
            // Admission precedes decoding and GPU upload. A denied upgrade keeps its
            // coarse texture without decoding the same unaffordable mip every frame.
            if (!Reserve(ResidentSize(entry.Index, desired), source, out var pendingReservation)) return entry.Texture;
            entry.PendingReservation = pendingReservation;
            var cancellation = new CancellationTokenSource();
            entry.Cancellation = cancellation; entry.Requested = desired;
            var token = cancellation.Token;
            entry.Pending = ReadDeferred();
            async BTask<TextureImageData> ReadDeferred()
            {
                await BTask.Yield(); token.ThrowIfCancellationRequested();
                using var stream = files.OpenFile(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.RandomAccess);
                return await entry.Index.ReadLevelsAsync(stream, desired, token);
            }
        }
        return entry.Texture;
    }

    private async BTask<(TextureMipStream Index, TextureImageData Data)> LoadInitialAsync(
        string path, BCancellationToken cancellationToken)
    {
        return await BackgroundWorkScheduler.Main.Run(token =>
        {
            using var stream = files.OpenFile(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.RandomAccess);
            var index = new TextureMipStream(stream, token);
            var level = index.MipCount - 1;
            return (index, index.ReadLevels(stream, level, token));
        }, cancellationToken);
    }
    private bool Reserve(long bytes, string exclude, out MemoryBudget.Lease? lease)
    {
        if (GraphicsTextureMemory.TryReserve(bytes, out lease)) return true;
        // Eviction is already bounded by the amount of memory we need. Select the
        // oldest entry on each pass so admission does not allocate LINQ iterators or
        // a sorted snapshot on the render thread.
        while (true)
        {
            string? oldestKey = null;
            Entry? oldest = null;
            foreach (var pair in _entries)
            {
                if (pair.Key == exclude || oldest is not null && pair.Value.Used >= oldest.Used) continue;
                oldestKey = pair.Key;
                oldest = pair.Value;
            }
            if (oldest is null) return false;
            Remove(oldestKey!, oldest);
            if (GraphicsTextureMemory.TryReserve(bytes, out lease)) return true;
        }
    }
    private long ResidentSize(TextureMipStream image, int first) => TextureUpload.GetUploadByteSize(device,
        Math.Max(1, image.Width >> first), Math.Max(1, image.Height >> first), image.Format, image.MipCount - first);
    private void AdjustBytes(long delta) { _residentBytes += delta; TextureStreaming.AdjustResidentBytes(delta); }
    private void Remove(string key, Entry entry)
    {
        if (!_entries.Remove(key)) return;
        AdjustBytes(-entry.Texture.Description.DataByteSize);
        entry.Cancellation?.Cancel(); entry.Cancellation?.Dispose(); entry.PendingReservation?.Dispose();
        GraphicsTextureMemory.Retire(device, entry.Texture, entry.Reservation); changed?.Invoke();
    }
    internal void Trim()
    {
        while (_entries.Count > 0)
        {
            using var enumerator = _entries.GetEnumerator();
            if (!enumerator.MoveNext()) break;
            var pair = enumerator.Current;
            Remove(pair.Key, pair.Value);
        }
    }
    internal int TrimInactive(long frame, long minimumIdleFrames, int maximumEntries)
    {
        if (maximumEntries <= 0) return 0;
        var removed = 0;
        while (removed < maximumEntries)
        {
            string? oldestKey = null;
            Entry? oldest = null;
            foreach (var pair in _entries)
            {
                if (frame - pair.Value.LastFrame < minimumIdleFrames ||
                    oldest is not null && pair.Value.Used >= oldest.Used) continue;
                oldestKey = pair.Key;
                oldest = pair.Value;
            }
            if (oldest is null) break;
            Remove(oldestKey!, oldest);
            removed++;
        }
        return removed;
    }
    internal void Release(string source)
    {
        if (_entries.TryGetValue(source, out var entry)) Remove(source, entry);
        if (_pending.TryGetValue(source, out var pending)) CancelPending(source, pending);
    }
    internal bool Contains(string source) => _entries.ContainsKey(source);
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        while (_pending.Count > 0)
        {
            using var enumerator = _pending.GetEnumerator();
            if (!enumerator.MoveNext()) break;
            var pair = enumerator.Current;
            CancelPending(pair.Key, pair.Value);
        }
        Trim();
    }

    private void CancelPending(string source, PendingLoad pending)
    {
        if (!_pending.Remove(source)) return;
        pending.Cancellation.Cancel(); pending.Cancellation.Dispose();
    }

    private sealed record PendingLoad(string Path, DateTime Stamp, TextureFilterMode Filter,
        TextureWrapMode Wrap, BCancellationSource Cancellation,
        BTask<(TextureMipStream Index, TextureImageData Data)> Task);
}
