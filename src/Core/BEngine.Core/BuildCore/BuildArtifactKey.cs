using System.Security.Cryptography;
using System.Text;
using BEngine.Rendering.Rhi;

namespace BEngine.Build;

/// <summary>Stable content identity shared by build producers and runtime artifact consumers.</summary>
public static class BuildArtifactKey
{
    public static string Shader(GraphicsBackend backend, int stage, int language, string code, string entryPoint)
    {
        if (!Enum.IsDefined(backend) || stage is < 0 or > 1 || language is < 0 or > 4)
            throw new ArgumentOutOfRangeException(nameof(backend));
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPoint);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("BEngine.ShaderArtifact.1");
            writer.Write((int)backend); writer.Write(stage); writer.Write(language);
            writer.Write(entryPoint); writer.Write(code);
        }
        var hash = Convert.ToHexStringLower(SHA256.HashData(stream.GetBuffer().AsSpan(0, (int)stream.Length)));
        return $"Shaders/Compiled/{backend.ToString().ToLowerInvariant()}/{hash}.bytes";
    }
}
