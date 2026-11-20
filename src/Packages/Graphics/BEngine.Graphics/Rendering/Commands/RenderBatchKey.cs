namespace BEngine;

[RuntimeTypeAlias("BEngine.RenderBatchKey2D")]
[RuntimeTypeAlias("BEngine.RenderBatchKey2D, BEngine.Graphics")]
[RuntimeTypeAlias("BEngine.RenderBatchKey2D, BEngine.Graphics, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null")]
public readonly record struct RenderBatchKey(Guid Material, string Shader, string Atlas)
{
    /// <summary>Frame-local mask group. Zero means no mask.</summary>
    public int MaskGroup { get; init; }
    /// <summary>Optional payload contract identity for contributors that share a material but cannot render each other's payloads.</summary>
    public Type? PayloadType { get; init; }
    public RenderBatchKey(Material material, string atlas)
        : this(material?.Id ?? throw new ArgumentNullException(nameof(material)),
            material.shader.shaderName, atlas?.Trim() ?? string.Empty)
    {
    }
}
