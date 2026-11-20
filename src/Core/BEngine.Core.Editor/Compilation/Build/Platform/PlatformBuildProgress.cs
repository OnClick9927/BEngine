using BEngine.Build;

namespace BEngine.Editor;

public enum PlayerBuildPhase
{
    Preparing,
    CompilingPlayer,
    BuildingContent,
    Publishing,
    Completed
}

public sealed record PlayerBuildProgress(
    PlayerBuildPhase Phase,
    string Message,
    float Progress);

public sealed record PlayerBuildPrerequisite(
    string Id,
    bool IsSatisfied,
    string Message,
    string? Remediation = null);

public interface IPlayerBuildPrerequisiteProvider
{
    bool SupportsTarget(BuildTargetDescriptor target);
    IReadOnlyList<PlayerBuildPrerequisite> GetPrerequisites(BuildTargetDescriptor target);
}

