using System.Reflection;
using BEngine.ProjectSystem;

namespace BEngine.ExampleTests.PackageSourceCompilation;

internal static class PackageSourceCompilerAdapter
{
    private const BindingFlags StaticMember = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags InstanceMember = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static IReadOnlyDictionary<string, string> CompileEnabled(
        ProjectWorkspace workspace,
        IEnumerable<BPackageDefinition> definitions)
    {
        var definitionArray = definitions.ToArray();
        var compilerType = typeof(BPackageManager).Assembly.GetType(
            "BEngine.ProjectSystem.PackageSourceCompiler", throwOnError: true)!;
        var method = compilerType.GetMethods(StaticMember)
            .Single(candidate => candidate.Name == "CompileEnabled" &&
                                 candidate.GetParameters().Length == 3);
        var result = method.Invoke(null, [workspace, definitionArray, null]) ??
                     throw new InvalidOperationException("PackageSourceCompiler returned no compilation result.");
        var pathsProperty = result.GetType().GetProperty("AssemblyPaths", InstanceMember) ??
                            throw new MissingMemberException(result.GetType().FullName, "AssemblyPaths");
        var paths = pathsProperty.GetValue(result) as IReadOnlyDictionary<string, string> ??
                    throw new InvalidDataException(
                        "PackageCompilationResult.AssemblyPaths has an invalid type.");
        var tryGet = result.GetType().GetMethod(
            "TryGetAssemblyPath",
            InstanceMember,
            binder: null,
            [typeof(string), typeof(string).MakeByRefType()],
            modifiers: null) ?? throw new MissingMethodException(
            result.GetType().FullName, "TryGetAssemblyPath(string, out string)");
        foreach (var assemblyName in definitionArray.SelectMany(definition => new[]
                 {
                     definition.Document.Runtime?.Assembly,
                     definition.Document.Editor?.Assembly
                 }).Where(name => !string.IsNullOrWhiteSpace(name)).Cast<string>())
        {
            object?[] arguments = [assemblyName, null];
            TestAssert.That(tryGet.Invoke(result, arguments) is true && arguments[1] is string resolved &&
                            paths.TryGetValue(assemblyName, out var published) &&
                            Path.GetFullPath(resolved).Equals(
                                Path.GetFullPath(published), StringComparison.OrdinalIgnoreCase),
                $"PackageCompilationResult lookup disagrees for '{assemblyName}'.");
        }
        return paths;
    }
}
