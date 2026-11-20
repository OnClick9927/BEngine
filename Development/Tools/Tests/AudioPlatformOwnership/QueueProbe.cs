using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

internal static class QueueProbe
{
    private delegate bool WritePrefix(ReadOnlySpan<float> samples, int available, out int accepted);
    private delegate void WriteSamples(ReadOnlySpan<float> samples, int available);
    private delegate void ReadSamples(Span<short> samples);

    internal static object Run(Assembly assembly, string typeName)
    {
        var type = assembly.GetType(typeName, throwOnError: true)!;
        object New() => Activator.CreateInstance(type, [5760])!;
        var stream = New();
        var prefix = type.GetMethod("TryWrite")!.CreateDelegate<WritePrefix>(stream);
        var write = type.GetMethod("Write")!.CreateDelegate<WriteSamples>(stream);
        var read = type.GetMethod("Read")!.CreateDelegate<ReadSamples>(stream);
        var input = new float[960];
        var pcm = new short[1200];
        var random = new Random(94281);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (var frame = 0; frame < 256; frame++)
        {
            for (var i = 0; i < input.Length; i++) input[i] = (float)(random.NextDouble() * 3 - 1.5);
            if (frame % 13 == 0) { input[0] = float.NaN; input[1] = float.PositiveInfinity; }
            if (frame % 7 == 0) write(input, frame % 14 == 0 ? 0 : 120);
            else prefix(input, frame % 11 == 0 ? 160 : 5760, out _);
            read(pcm.AsSpan(0, frame % 3 == 0 ? 1200 : 480));
            hash.AppendData(MemoryMarshal.AsBytes(pcm.AsSpan()));
        }
        var counters = new Dictionary<string, object?>();
        foreach (var name in new[] { "Count", "CapacityFrames", "WrittenFrames", "ConsumedFrames", "UnderrunFrames", "TotalDroppedFrames", "QueueResets", "NonFiniteSamples", "PeakPcm" })
            counters[name] = type.GetProperty(name)!.GetValue(stream);
        // A fresh steady queue isolates native-device jitter from allocation measurement.
        stream = New();
        prefix = type.GetMethod("TryWrite")!.CreateDelegate<WritePrefix>(stream);
        read = type.GetMethod("Read")!.CreateDelegate<ReadSamples>(stream);
        for (var i = 0; i < 100; i++) { prefix(input, 5760, out _); read(pcm.AsSpan(0, 960)); }
        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 512; i++) { prefix(input, 5760, out _); read(pcm.AsSpan(0, 960)); }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        return new { SHA256 = Convert.ToHexString(hash.GetHashAndReset()), Frames = 256, Counters = counters, WarmAllocatedBytes = allocated };
    }
}
