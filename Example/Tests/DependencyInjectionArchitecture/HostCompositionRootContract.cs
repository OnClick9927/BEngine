using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.ExampleTests.DependencyInjectionArchitecture;

internal static class HostCompositionRootContract
{
    public static void Verify()
    {
        VerifyAssembly("BEngine.Editor", "AddBEngineEditor");
        VerifyAssembly("BEngine.Launcher", "AddBEngineLauncher");
        VerifyAssembly("BEngine.Player", "AddBEnginePlayer");
    }

    private static void VerifyAssembly(string assemblyName, string methodName)
    {
        var assembly = Assembly.Load(new AssemblyName(assemblyName));
        var registrationMethod = GetLoadableTypes(assembly)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                BindingFlags.Static))
            .FirstOrDefault(method => method.Name == methodName &&
                                      method.GetParameters() is [var first, ..] &&
                                      first.ParameterType == typeof(IServiceCollection));
        Require(registrationMethod is not null,
            $"{assemblyName} has no IServiceCollection composition-root registration named {methodName}.");
    }

    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type is not null).Cast<Type>().ToArray();
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
