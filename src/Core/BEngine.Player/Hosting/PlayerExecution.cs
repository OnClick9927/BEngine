namespace BEngine.Player;

/// <summary>Execution and transition operations available to Player content providers.</summary>
public static class PlayerExecution
{
    /// <summary>Observes an already completed operation; a pending operation must be advanced by Update.</summary>
    public static void Complete(BTask operation) => PlayerMainThread.Complete(operation);
    /// <inheritdoc cref="Complete(BTask)" />
    public static void Complete(BValueTask operation) => PlayerMainThread.Complete(operation);
    /// <inheritdoc cref="Complete(BTask)" />
    public static TResult Complete<TResult>(BTask<TResult> operation) => PlayerMainThread.Complete(operation);
    /// <inheritdoc cref="Complete(BTask)" />
    public static TResult Complete<TResult>(BValueTask<TResult> operation) => PlayerMainThread.Complete(operation);

    /// <summary>Advances shutdown through the owning runtime's Update callback with a bounded update count.</summary>
    /// <returns>False if shutdown is still pending or Update failed. Completed failures are reported to the callback.</returns>
    public static bool TryCompleteShutdown(BTask operation, Action<TimeSpan> runtimeUpdate,
        string owner, Action<Exception> reportFailure,
        int maximumUpdateCount = PlayerRuntimeShutdownDrain.DefaultMaximumUpdateCount) =>
        PlayerRuntimeShutdownDrain.TryComplete(operation, runtimeUpdate, owner, reportFailure, maximumUpdateCount);

    /// <summary>Advances bounded shutdown and reports failures through the engine log.</summary>
    public static bool TryCompleteShutdown(BTask operation, Action<TimeSpan> runtimeUpdate,
        string owner, string diagnosticPhase,
        int maximumUpdateCount = PlayerRuntimeShutdownDrain.DefaultMaximumUpdateCount) =>
        PlayerRuntimeShutdownDrain.TryComplete(operation, runtimeUpdate, owner, diagnosticPhase, maximumUpdateCount);

    /// <summary>Activates and commits a prepared stage. On failure, rolls back content and restores the previous stage.</summary>
    /// <returns>Null on success; otherwise the original failure, including any rollback failures.</returns>
    public static BValueTask<Exception?> RunTransitionAsync(Action startNextStage,
        Func<BValueTask> commitContent, Action completeTransition,
        Func<Exception, BValueTask> rollback, Action restorePreviousStage) =>
        PlayerStageTransitionTransaction.ExecuteAsync(startNextStage, commitContent,
            completeTransition, rollback, restorePreviousStage);
}
