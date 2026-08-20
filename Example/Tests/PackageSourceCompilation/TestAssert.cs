namespace BEngine.ExampleTests.PackageSourceCompilation;

internal static class TestAssert
{
    public static void That(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
