namespace BEngine.Player;

internal static class PlayerMainThread
{
    internal static void Tick(TimeSpan elapsed) => BTaskScheduler.Main.Tick(elapsed);

    internal static void Complete(BValueTask operation) => Complete(operation.AsBTask());

    internal static TResult Complete<TResult>(BValueTask<TResult> operation) =>
        Complete(operation.AsBTask());

    internal static void Complete(BTask operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!operation.IsCompleted)
            throw new InvalidOperationException(
                "A pending BTask can only be advanced by the Player Update loop.");
        operation.GetAwaiter().GetResult();
    }

    internal static TResult Complete<TResult>(BTask<TResult> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!operation.IsCompleted)
            throw new InvalidOperationException(
                "A pending BTask can only be advanced by the Player Update loop.");
        return operation.GetAwaiter().GetResult();
    }
}
