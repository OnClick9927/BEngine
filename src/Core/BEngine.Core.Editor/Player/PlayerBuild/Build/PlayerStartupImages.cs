using BEngine.ProjectSystem;

namespace BEngine.Editor.Build;

/// <summary>Encoded startup artwork. The original application icon is kept separate from branding.</summary>
public readonly record struct PlayerStartupImages(ReadOnlyMemory<byte> LogoPng, ReadOnlyMemory<byte> ApplicationIconPng);

/// <summary>Drawing modules validate and compose startup artwork; Core only stages the encoded result.</summary>
public interface IPlayerStartupImageBuilder
{
    PlayerStartupImages Build(ProjectWorkspace workspace, PlayerBuildRequest request);
}
