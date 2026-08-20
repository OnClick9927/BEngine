using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Serialization;

namespace BEngine.Editor;

internal static class EditorInitialization
{
    private static readonly HashSet<string> Invoked = new(StringComparer.Ordinal);

    internal static void Run(bool scriptsReloaded = true)
    {
        Run(AppDomain.CurrentDomain.GetAssemblies(), scriptsReloaded);
    }

    internal static void Run(IEnumerable<Assembly> assemblies, bool scriptsReloaded = true)
    {
        var candidates = assemblies.Distinct().ToHashSet();
        foreach (var type in candidates.SelectMany(RuntimeTypeCache.GetTypes)
                     .Where(HasInitializeOnLoad)
                     .OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            EditorFeatureGuard.Invoke($"InitializeOnLoad {type.FullName}",
                () => RuntimeHelpers.RunClassConstructor(type.TypeHandle));
        }

        var methods = TypeCache.GetMethodsWithAttribute<InitializeOnLoadMethodAttribute>()
            .Where(method => candidates.Contains(method.DeclaringType!.Assembly))
            .OrderBy(method => method.DeclaringType?.FullName, StringComparer.Ordinal)
            .ThenBy(method => method.MetadataToken);
        foreach (var method in methods) InvokeOnce(method, "Editor initialization");

        if (!scriptsReloaded) return;
        foreach (var method in TypeCache.GetMethodsWithAttribute<DidReloadScriptsAttribute>()
                     .Where(method => candidates.Contains(method.DeclaringType!.Assembly))
                     .Select(ReadReloadAttribute)
                     .Where(item => item.Attribute is not null)
                     .OrderBy(item => item.Attribute!.callbackOrder)
                     .ThenBy(item => item.Method.DeclaringType?.FullName, StringComparer.Ordinal)
                     .ThenBy(item => item.Method.MetadataToken))
            Invoke(method.Method, "DidReloadScripts");
    }

    internal static void ForgetAssemblies(IEnumerable<Assembly> assemblies)
    {
        var prefixes = assemblies.SelectMany(assembly => assembly.Modules)
            .Select(module => $"{module.ModuleVersionId:N}:")
            .ToArray();
        foreach (var key in Invoked.Where(key => prefixes.Any(key.StartsWith)).ToArray()) Invoked.Remove(key);
    }

    private static MethodInfo[] GetMethods(Type type)
    {
        try
        {
            return type.GetMethods(BindingFlags.Static | BindingFlags.Instance |
                                   BindingFlags.Public | BindingFlags.NonPublic);
        }
        catch { return []; }
    }

    private static void InvokeOnce(MethodInfo method, string callbackName)
    {
        var key = $"{method.Module.ModuleVersionId:N}:{method.MetadataToken}";
        if (!Invoked.Add(key)) return;
        Invoke(method, callbackName);
    }

    private static void Invoke(MethodInfo method, string callbackName)
    {
        if (!method.IsStatic || method.ReturnType != typeof(void) || method.GetParameters().Length != 0)
        {
            Debug.LogWarning($"{callbackName} method must be static void with no parameters: " +
                             $"{method.DeclaringType?.FullName}.{method.Name}");
            return;
        }
        EditorFeatureGuard.Invoke($"{callbackName} {method.DeclaringType?.FullName}.{method.Name}",
            () => method.Invoke(null, null));
    }

    private static bool HasInitializeOnLoad(Type type)
    {
        EditorFeatureGuard.TryInvoke($"InitializeOnLoad attribute {type.FullName}",
            () => type.IsDefined(typeof(InitializeOnLoadAttribute), false), false, out var defined);
        return defined;
    }

    private static (MethodInfo Method, DidReloadScriptsAttribute? Attribute) ReadReloadAttribute(MethodInfo method)
    {
        EditorFeatureGuard.TryInvoke($"DidReloadScripts attribute {method.DeclaringType?.FullName}.{method.Name}",
            () => method.GetCustomAttribute<DidReloadScriptsAttribute>(), null, out var attribute);
        return (method, attribute);
    }
}
