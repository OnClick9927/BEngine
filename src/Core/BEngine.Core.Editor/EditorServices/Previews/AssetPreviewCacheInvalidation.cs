namespace BEngine.Editor;

/// <summary>A source path to retire, or null when all temporary preview data is invalid.</summary>
public readonly record struct AssetPreviewCacheInvalidation(string? Path);
