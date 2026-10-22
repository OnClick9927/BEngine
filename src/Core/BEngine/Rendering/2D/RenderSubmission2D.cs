namespace BEngine;

public readonly record struct RenderSubmission2D(
    RenderSortKey2D SortKey,
    RenderBatchKey2D BatchKey,
    object Payload)
{
    // One-based frame-local index; zero is an ordinary package-owned payload.
    internal int SceneQuadIndex { get; init; }
    // Internal storage metadata must not change the public record's value semantics.
    public bool Equals(RenderSubmission2D other) => SortKey == other.SortKey && BatchKey == other.BatchKey &&
        EqualityComparer<object>.Default.Equals(Payload, other.Payload);
    public override int GetHashCode() => HashCode.Combine(SortKey, BatchKey, Payload);
}
