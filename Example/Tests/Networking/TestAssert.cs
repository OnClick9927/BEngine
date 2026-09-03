namespace BEngine.ExampleTests.Networking;

internal static class TestAssert
{
    public static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message} Expected '{expected}', received '{actual}'.");
    }

    public static async Task<TException> ThrowsAsync<TException>(Func<Task> action, string message)
        where TException : Exception
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (TException exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"{message} Expected {typeof(TException).Name}, received {exception.GetType().Name}.",
                exception);
        }
        throw new InvalidOperationException($"{message} No exception was thrown.");
    }

    public static async Task<TException> ThrowsAsync<TException>(Func<BValueTask> action, string message)
        where TException : Exception
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (TException exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"{message} Expected {typeof(TException).Name}, received {exception.GetType().Name}.",
                exception);
        }
        throw new InvalidOperationException($"{message} No exception was thrown.");
    }

    public static async Task<TException> ThrowsAsync<TException, TResult>(
        Func<BValueTask<TResult>> action,
        string message)
        where TException : Exception
    {
        try
        {
            _ = await action().ConfigureAwait(false);
        }
        catch (TException exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"{message} Expected {typeof(TException).Name}, received {exception.GetType().Name}.",
                exception);
        }
        throw new InvalidOperationException($"{message} No exception was thrown.");
    }
}
