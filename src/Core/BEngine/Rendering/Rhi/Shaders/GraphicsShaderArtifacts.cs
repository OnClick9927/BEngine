using System.Security.Cryptography;
using System.Text;

namespace BEngine.Rendering.Rhi;

/// <summary>Content-addressed, backend-specific shader artifacts supplied by any resource provider.</summary>
public static class GraphicsShaderArtifacts
{
    public static string GetResourcePath(GraphicsShaderSource source, GraphicsBackend backend)
    {
        if (!Enum.IsDefined(source.Stage) || !Enum.IsDefined(source.Language) || !Enum.IsDefined(backend))
            throw new ArgumentOutOfRangeException(nameof(source));
        ArgumentException.ThrowIfNullOrWhiteSpace(source.EntryPoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.Code);
        // Length-prefixed fields avoid ambiguous concatenation and normalize no source text.
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("BEngine.ShaderArtifact.1");
            writer.Write((int)backend); writer.Write((int)source.Stage); writer.Write((int)source.Language);
            writer.Write(source.EntryPoint); writer.Write(source.Code);
        }
        var hash = Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, (int)stream.Length))).ToLowerInvariant();
        return $"Shaders/Compiled/{backend.ToString().ToLowerInvariant()}/{hash}.bytes";
    }

    public static byte[]? Load(GraphicsShaderSource source, GraphicsBackend backend) =>
        Resources.Load<byte[]>(GetResourcePath(source, backend));
}
