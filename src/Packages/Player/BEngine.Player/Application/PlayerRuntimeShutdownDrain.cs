namespace BEngine.Player;

internal static class PlayerRuntimeShutdownDrain
{
    internal const int DefaultMaximumUpdateCount = 1000;

    internal static bool TryComplete(
        BTask operation,
        Action<TimeSpan> runtimeUpdate,
        string owner,
        string diagnosticPhase,
        int maximumUpdateCount = DefaultMaximumUpdateCount) =>
        TryComplete(
            operation,
            runtimeUpdate,
            owner,
            exception => PlayerStartupDiagnostics.Failure(diagnosticPhase, exception),
            maximumUpdateCount);

    internal static bool TryComplete(
        BTask operation,
        Action<TimeSpan> runtimeUpdate,
        string owner,
        Action<Exception> reportFailure,
        int maximumUpdateCount = DefaultMaximumUpdateCount)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(runtimeUpdate);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(reportFailure);
        if (maximumUpdateCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumUpdateCount));

        for (var updateCount = 0;
             updateCount < maximumUpdateCount && !operation.IsCompleted;
             updateCount++)
        {
            try
            {
                runtimeUpdate(TimeSpan.Zero);
            }
            catch (Exception exception)
            {
                reportFailure(new InvalidOperationException(
                    $"{owner} shutdown could not be advanced by its runtime Update callback.",
                    exception));
                return false;
            }
        }

        if (!operation.IsCompleted)
        {
            reportFailure(new TimeoutException(
                $"{owner} shutdown is still pending after {maximumUpdateCount} runtime Update callbacks."));
            return false;
        }

        try
        {
            operation.GetAwaiter().GetResult();
        }
        catch (BOperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            reportFailure(exception);
        }
        return true;
    }
}
