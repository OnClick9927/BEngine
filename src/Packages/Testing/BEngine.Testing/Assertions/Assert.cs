using System.Collections;

namespace BEngine.Testing;

public sealed class AssertionException(string message) : Exception(message);

public static class Assert
{
    public static void IsTrue(bool condition, string? message = null)
    {
        if (!condition) Fail(message ?? "Expected true, but was false.");
    }

    public static void IsFalse(bool condition, string? message = null) =>
        IsTrue(!condition, message ?? "Expected false, but was true.");

    public static void AreEqual<T>(T expected, T actual, string? message = null)
    {
        if (Equals(expected, actual)) return;
        Fail(message ?? $"Expected <{expected}>, but was <{actual}>.");
    }

    public static void AreSequenceEqual(IEnumerable expected, IEnumerable actual, string? message = null)
    {
        var left = expected.Cast<object?>().ToArray();
        var right = actual.Cast<object?>().ToArray();
        if (left.SequenceEqual(right)) return;
        Fail(message ?? $"Sequences differ. Expected [{string.Join(", ", left)}], " +
             $"but was [{string.Join(", ", right)}].");
    }

    public static T Throws<T>(Action action, string? message = null) where T : Exception
    {
        ArgumentNullException.ThrowIfNull(action);
        try { action(); }
        catch (T exception) { return exception; }
        catch (Exception exception)
        {
            Fail(message ?? $"Expected {typeof(T).Name}, but {exception.GetType().Name} was thrown.");
        }
        Fail(message ?? $"Expected {typeof(T).Name}, but no exception was thrown.");
        throw new InvalidOperationException("Unreachable assertion path.");
    }

    public static void Fail(string message) => throw new AssertionException(message);
}
