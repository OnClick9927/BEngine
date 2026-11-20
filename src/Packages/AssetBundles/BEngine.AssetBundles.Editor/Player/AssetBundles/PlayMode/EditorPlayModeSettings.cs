namespace BEngine.Editor.Build;

public enum EditorPlayModeContentMode
{
    Virtual,
    Real
}

/// <summary>Editor-only content selection; independent of Player startup/load settings.</summary>
public sealed class EditorPlayModeSettings
{
    public EditorPlayModeContentMode Mode { get; set; }
    public string ReleaseDirectory { get; set; } = string.Empty;
    /// <summary>The release identity, not an assetbundle package group such as fonts or images.</summary>
    public string PackageName { get; set; } = "main";
}
