using System.Reflection;

namespace BEngine.ExampleTests.EditorKeyboardCommands;

internal static class TestAssert
{
    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static Type RequireType(Assembly assembly, string fullName) =>
        assembly.GetType(fullName, throwOnError: false) ?? throw new TypeLoadException(fullName);
}
