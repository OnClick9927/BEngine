namespace BEngine;

[RuntimeTypeAlias("BEngine.RenderSubmission2D")]
[RuntimeTypeAlias("BEngine.RenderSubmission2D, BEngine.Graphics")]
[RuntimeTypeAlias("BEngine.RenderSubmission2D, BEngine.Graphics, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null")]
public readonly record struct RenderSubmission(
    RenderOrderKey SortKey,
    RenderBatchKey BatchKey,
    object Payload)
{
    // One-based index into the rendering host's frame-local payload store.
    // Zero selects the ordinary contributor-owned payload instead.
    internal int FramePayloadIndex { get; init; }
    // Internal storage metadata must not change the public record's value semantics.
    public bool Equals(RenderSubmission other) => SortKey == other.SortKey && BatchKey == other.BatchKey &&
        EqualityComparer<object>.Default.Equals(Payload, other.Payload);
    public override int GetHashCode() => HashCode.Combine(SortKey, BatchKey, Payload);
}
