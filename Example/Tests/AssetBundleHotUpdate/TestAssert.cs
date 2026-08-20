namespace BEngine.ExampleTests.AssetBundleHotUpdate;

internal static class TestAssert
{
    internal static void That(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static TException Throws<TException>(Action action, string expectedMessage)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            That(exception.Message.Contains(expectedMessage, StringComparison.OrdinalIgnoreCase),
                $"Expected {typeof(TException).Name} containing '{expectedMessage}', got '{exception.Message}'.");
            return exception;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Expected {typeof(TException).Name}, got {exception.GetType().Name}: {exception.Message}", exception);
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}, but no exception was thrown.");
    }

    internal static async Task<TException> ThrowsAsync<TException>(
        Func<Task> action,
        string expectedMessage = "")
        where TException : Exception
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (TException exception)
        {
            That(string.IsNullOrEmpty(expectedMessage) ||
                 exception.Message.Contains(expectedMessage, StringComparison.OrdinalIgnoreCase),
                $"Expected {typeof(TException).Name} containing '{expectedMessage}', got '{exception.Message}'.");
            return exception;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Expected {typeof(TException).Name}, got {exception.GetType().Name}: {exception.Message}", exception);
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}, but no exception was thrown.");
    }
}
