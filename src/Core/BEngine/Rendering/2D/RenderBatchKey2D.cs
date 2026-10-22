namespace BEngine;

public readonly record struct RenderBatchKey2D(Guid Material, string Shader, string Atlas)
{
    /// <summary>Frame-local mask group. Zero means no mask.</summary>
    public int MaskGroup { get; init; }
    /// <summary>Optional payload contract identity for contributors that share a material but cannot render each other's payloads.</summary>
    public Type? PayloadType { get; init; }
    public RenderBatchKey2D(Material material, string atlas)
        : this(material?.Id ?? throw new ArgumentNullException(nameof(material)),
            material.shader.shaderName, atlas?.Trim() ?? string.Empty)
    {
    }
}
