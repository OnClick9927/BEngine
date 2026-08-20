namespace BEngine;

public readonly record struct RenderSubmission2D(
    RenderSortKey2D SortKey,
    RenderBatchKey2D BatchKey,
    object Payload);
