namespace BEngine.Editor;

/// <summary>A source resource made available to shader contributors, independent of any release workflow.</summary>
public sealed record ShaderBuildResourceInput(string Address, string FilePath);
