using System.Reflection;

namespace BEngine.Testing;

public static class TestDiscovery
{
    public static IReadOnlyList<TestCase> Discover(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        var tests = new List<TestCase>();
        foreach (var assembly in assemblies.Distinct())
        foreach (var type in LoadableTypes(assembly).OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            if (type.ContainsGenericParameters) continue;
            var fixtureIgnore = type.GetCustomAttribute<IgnoreAttribute>(inherit: true)?.Reason;
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static |
                                                   BindingFlags.Public | BindingFlags.NonPublic |
                                                   BindingFlags.DeclaredOnly)
                         .OrderBy(method => method.MetadataToken))
            {
                var attribute = method.GetCustomAttribute<TestAttribute>(inherit: true);
                if (attribute is null) continue;
                Validate(method, attribute);
                var ignore = method.GetCustomAttribute<IgnoreAttribute>(inherit: true)?.Reason ??
                             fixtureIgnore ?? string.Empty;
                var typeName = type.FullName ?? type.Name;
                tests.Add(new TestCase
                {
                    Id = $"{assembly.GetName().Name}:{typeName}.{method.Name}:{attribute.Mode}",
                    AssemblyName = assembly.GetName().Name ?? string.Empty,
                    TypeName = typeName,
                    MethodName = method.Name,
                    Mode = attribute.Mode,
                    Category = attribute.Category?.Trim() ?? string.Empty,
                    TimeoutMilliseconds = attribute.TimeoutMilliseconds,
                    IgnoreReason = ignore,
                    Method = method
                });
            }
        }
        return tests.OrderBy(test => test.Mode).ThenBy(test => test.AssemblyName, StringComparer.Ordinal)
            .ThenBy(test => test.TypeName, StringComparer.Ordinal)
            .ThenBy(test => test.MethodName, StringComparer.Ordinal).ToArray();
    }

    private static void Validate(MethodInfo method, TestAttribute attribute)
    {
        if (method.ContainsGenericParameters || method.GetParameters().Length != 0)
            throw new InvalidDataException($"Test '{method.DeclaringType?.FullName}.{method.Name}' must be non-generic and parameterless.");
        if (!method.IsStatic && method.DeclaringType?.GetConstructor(BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null) is null)
            throw new InvalidDataException($"Test fixture '{method.DeclaringType?.FullName}' requires a parameterless constructor.");
        if (attribute.TimeoutMilliseconds < 1)
            throw new InvalidDataException($"Test '{method.DeclaringType?.FullName}.{method.Name}' has an invalid timeout.");
    }

    private static Type[] LoadableTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException exception)
        { return exception.Types.Where(type => type is not null).Cast<Type>().ToArray(); }
        catch { return []; }
    }
}
