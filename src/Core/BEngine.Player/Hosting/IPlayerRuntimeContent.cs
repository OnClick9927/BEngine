
namespace BEngine.Player;

/// <summary>Prepared content and its optional stage policy, independent of its container or transport.</summary>
public interface IPlayerRuntimeContent
{
    PlayerSceneStage InitialStage { get; }
    IPlayerStageTransitionFlow? TransitionFlow => null;
    BValueTask CommitStartupAsync(BCancellationToken cancellationToken = default) => BValueTask.CompletedTask;
    BValueTask RollbackStartupAsync(Exception failure, BCancellationToken cancellationToken = default) => BValueTask.CompletedTask;
    BValueTask<PlayerSceneStage> PrepareTransitionAsync(BCancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This content provider has no additional runtime stage.");
    PlayerSceneStage CommitTransition(PlayerSceneStage next) =>
        throw new NotSupportedException("This content provider has no additional runtime stage.");
    BValueTask AbortTransitionAsync(PlayerSceneStage next) =>
        throw new NotSupportedException("This content provider has no additional runtime stage.");
    BValueTask ReleasePreviousStageAsync(PlayerSceneStage previous) => previous.DisposeAsync();
    BValueTask DisposeAsync();
}

/// <summary>Content policy requests a transition; Core owns execution, compensation and scene switching.</summary>
public interface IPlayerStageTransitionFlow
{
    IPlayerStartupControl? StartupControl => null;
    bool TransitionRequested { get; }
    void Start();
    void Pump();
    void ReportTransitionFailure(Exception failure);
}
