namespace BEngine.Editor.Diagnostics;

public sealed record FrameDebugProgramState(
    string Label,
    IReadOnlyList<FrameDebugShaderStage> Stages);
