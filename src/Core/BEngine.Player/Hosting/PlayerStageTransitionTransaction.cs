namespace BEngine.Player;

internal static class PlayerStageTransitionTransaction
{
    internal static async BValueTask<Exception?> ExecuteAsync(
        Action startGameStage,
        Func<BValueTask> commitContent,
        Action completeTransition,
        Func<Exception, BValueTask> rollback,
        Action restoreAotStage)
    {
        ArgumentNullException.ThrowIfNull(startGameStage);
        ArgumentNullException.ThrowIfNull(commitContent);
        ArgumentNullException.ThrowIfNull(completeTransition);
        ArgumentNullException.ThrowIfNull(rollback);
        ArgumentNullException.ThrowIfNull(restoreAotStage);
        try
        {
            startGameStage();
            await commitContent();
            completeTransition();
            return null;
        }
        catch (Exception failure)
        {
            List<Exception>? rollbackFailures = null;
            try { await rollback(failure); }
            catch (Exception exception) { (rollbackFailures ??= []).Add(exception); }
            try { restoreAotStage(); }
            catch (Exception exception) { (rollbackFailures ??= []).Add(exception); }
            return rollbackFailures is not { Count: > 0 }
                ? failure
                : new AggregateException(
                    "The game-stage transition and rollback both failed.",
                    [failure, .. rollbackFailures]);
        }
    }
}
