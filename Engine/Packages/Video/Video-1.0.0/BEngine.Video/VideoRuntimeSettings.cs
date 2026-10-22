namespace BEngine.Video;

/// <summary>Admission uses a conservative estimate, not a measurement of codec-owned native allocations.</summary>
public static class VideoRuntimeSettings
{
    static VideoRuntimeSettings() => Diagnostics.RuntimeDiagnosticRegistry.Register("budget.video", "Video admission",
        new("active", "Active decoders", Diagnostics.RuntimeDiagnosticUnit.Number, () => activeDecoders),
        new("estimated", "Estimated decoder/frame bytes", Diagnostics.RuntimeDiagnosticUnit.Bytes, () => estimatedMemoryBytes),
        new("limit", "Effective estimated memory budget", Diagnostics.RuntimeDiagnosticUnit.Bytes, () => effectiveEstimatedMemoryBudgetBytes));
    public static int maximumActiveDecoders { get; set; } = 32;
    public static long maximumEstimatedMemoryBytes { get; set; } = 256L * 1024 * 1024;
    public static int activeDecoders { get; private set; }
    public static long estimatedMemoryBytes { get; private set; }
    // Codec allocations include native memory and cannot be measured by a
    // managed GC heap estimate. Enforce the finite video-specific admission
    // limit instead of subtracting unrelated managed scene allocations.
    public static long effectiveEstimatedMemoryBudgetBytes => Math.Max(0, maximumEstimatedMemoryBytes);
    internal static IDisposable Reserve(int width, int height)
    {
        var bytes = Estimate(width, height);
        var limit = effectiveEstimatedMemoryBudgetBytes;
        if (maximumActiveDecoders < 1 || activeDecoders >= maximumActiveDecoders ||
            bytes > limit - estimatedMemoryBytes)
            throw new InvalidOperationException($"Video decoder/frame memory budget exceeded: requested={bytes}, reserved={estimatedMemoryBytes}, budget={limit}, configured={maximumEstimatedMemoryBytes}, active={activeDecoders}/{maximumActiveDecoders}, managed={GC.GetTotalMemory(false)}. Close unused players or explicitly increase VideoRuntimeSettings.");
        activeDecoders++; estimatedMemoryBytes += bytes; return new Reservation(bytes);
    }
    internal static VideoFrameReader Open(Func<VideoFrameReader> factory)
        => Open(_ => factory());
    internal static VideoFrameReader Open(Func<Action<int, int>, VideoFrameReader> factory)
    {
        var reservation = (Reservation)Reserve(1, 1); VideoFrameReader? reader = null;
        try { reader = factory(reservation.Resize); reservation.Resize(reader.width, reader.height); return new BudgetedReader(reader, reservation); }
        catch (InvalidDataException error) when (error.InnerException is OutOfMemoryException)
        {
            try { reader?.Dispose(); } finally { reservation.Dispose(); }
            throw new InvalidOperationException("Video native decoder exhausted its memory budget; close players or select lower-resolution content.", error);
        }
        catch { try { reader?.Dispose(); } finally { reservation.Dispose(); } throw; }
    }
    private static long Estimate(int width, int height) => checked(8L * 1024 * 1024 + (long)width * height * 4 * 16);
    private sealed class Reservation(long bytes) : IDisposable
    {
        private bool _disposed; private long _bytes = bytes;
        internal void Resize(int width, int height)
        {
            var next = Estimate(width, height);
            if (next > _bytes && next - _bytes > effectiveEstimatedMemoryBudgetBytes - estimatedMemoryBytes)
                throw new InvalidOperationException("Video frame dimensions exceed the global decoder memory estimate budget.");
            estimatedMemoryBytes += next - _bytes; _bytes = next;
        }
        public void Dispose() { if (_disposed) return; _disposed = true; activeDecoders--; estimatedMemoryBytes -= _bytes; }
    }
    private sealed class BudgetedReader(VideoFrameReader raw, IDisposable reservation) : VideoFrameReader
    {
        public override int width => raw.width; public override int height => raw.height;
        public override double frameRate => raw.frameRate; public override double duration => raw.duration;
        public override double timestamp => raw.timestamp; public override bool canSeek => raw.canSeek;
        public override string backend => raw.backend; public override bool hasAudio => raw.hasAudio;
        public override bool requiresPolling => raw.requiresPolling; public override bool hasPendingFrame => raw.hasPendingFrame;
        public override long hardwareDecodedFrames => raw.hardwareDecodedFrames; public override long decodedFrames => raw.decodedFrames;
        protected override void ReadFrameCore(double seconds, Span<byte> rgba) => raw.ReadFrame(seconds, rgba);
        protected override void DisposeCore() { try { raw.Dispose(); } finally { reservation.Dispose(); } }
    }
}
