using Veldrid;
#if BENGINE_RUNTIME_SHADER_COMPILER
using Veldrid.SPIRV;
#endif

namespace BEngine.Rendering.Rhi;

/// <summary>Bounded cache of device-independent shader bytecode. Compilation stays on the caller's thread.</summary>
public static class SpirvShaderCache
{
    private static readonly Dictionary<(ShaderStages Stage, string Source, string Entry), LinkedListNode<Entry>> Entries = [];
    private static readonly LinkedList<Entry> Recency = new();
    private static long _bytes;
    private static long _maximumBytes = 32L * 1024 * 1024;
    private static int _maximumEntries = 128;
    public static long maximumBytes { get => _maximumBytes; set { if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); _maximumBytes = value; Trim(); } }
    public static int maximumEntries { get => _maximumEntries; set { if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); _maximumEntries = value; Trim(); } }
    public static long minimumCompilationCommitBytes { get; set; } = 512L * 1024 * 1024;
    public static long hits { get; private set; }
    public static long misses { get; private set; }
    public static long cachedBytes => _bytes;
    private sealed record Entry((ShaderStages Stage, string Source, string Entry) Key, byte[] Bytes, long Size);
    internal static byte[] Compile(GraphicsShaderSource source)
    {
        if (source.Code.Length > 1024 * 1024) throw new InvalidDataException("Shader source exceeds the 1 MiB compilation limit.");
        var stage = source.Stage == GraphicsShaderStage.Vertex ? ShaderStages.Vertex : ShaderStages.Fragment;
        var key = (stage, source.Code, source.EntryPoint);
        if (Entries.TryGetValue(key, out var existing)) { Recency.Remove(existing); Recency.AddLast(existing); hits++; return existing.Value.Bytes; }
        misses++;
        var bytes = GraphicsShaderArtifacts.Load(source, GraphicsBackend.Vulkan);
        if (bytes is not null)
        {
            if (bytes.Length < 20 || bytes.Length % 4 != 0 ||
                System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 0x07230203)
                throw new InvalidDataException("Invalid offline SPIR-V shader artifact.");
            offlineHits++;
        }
        else
        {
#if BENGINE_RUNTIME_SHADER_COMPILER
            if (VulkanStartupMemory.GetRejectionReason("SPIR-V compilation", minimumCompilationCommitBytes) is { } rejection)
                throw new InvalidOperationException(rejection);
            bytes = SpirvCompilation.CompileGlslToSpirv(source.Code, "bengine.glsl", stage, GlslCompileOptions.Default).SpirvBytes;
            runtimeCompilations++;
#else
            throw new InvalidOperationException("No offline Vulkan shader artifact: " +
                GraphicsShaderArtifacts.GetResourcePath(source, GraphicsBackend.Vulkan) +
                ". Rebuild content with a shader source provider or enable Runtime Shader Compilation.");
#endif
        }
        var size = ((long)source.Code.Length + source.EntryPoint.Length) * sizeof(char) + bytes.Length;
        if (maximumEntries > 0 && size <= maximumBytes)
        {
            while (Recency.First is { } oldest && (Entries.Count >= maximumEntries || _bytes + size > maximumBytes))
            { Recency.RemoveFirst(); Entries.Remove(oldest.Value.Key); _bytes -= oldest.Value.Size; }
            Entries[key] = Recency.AddLast(new Entry(key, bytes, size)); _bytes += size;
        }
        return bytes;
    }
    public static void Clear() { Entries.Clear(); Recency.Clear(); _bytes = 0; }
    public static long offlineHits { get; private set; }
    public static long runtimeCompilations { get; private set; }
    private static void Trim()
    {
        while (Recency.First is { } oldest && (Entries.Count > _maximumEntries || _bytes > _maximumBytes))
        { Recency.RemoveFirst(); Entries.Remove(oldest.Value.Key); _bytes -= oldest.Value.Size; }
    }
}
