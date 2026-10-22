namespace BEngine.Editor.Diagnostics;

public readonly record struct FrameDebugMarker(
    string Group = "",
    string BatchName = "",
    Guid? Material = null,
    string Shader = "",
    string Atlas = "",
    string SourceName = "",
    int? SourceInstanceId = null);
