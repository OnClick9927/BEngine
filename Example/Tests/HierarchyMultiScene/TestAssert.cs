using System.Reflection;

namespace BEngine.ExampleTests.HierarchyMultiScene;

internal static class TestAssert
{
    public static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static Type RequireType(Assembly assembly, string fullName) =>
        assembly.GetType(fullName, throwOnError: false) ??
        throw new TypeLoadException($"Required type '{fullName}' was not found in {assembly.GetName().Name}.");

    public static MethodInfo RequireMethod(Type type, string name, BindingFlags flags,
        params Type[] parameterTypes) =>
        type.GetMethod(name, flags, binder: null, parameterTypes, modifiers: null) ??
        throw new MissingMethodException(type.FullName, name);
}
