using BEngine.Editor.Documents;

namespace BEngine.ExampleTests.PackageSourceCompilation;

internal sealed class SourcePackageFixture(string repositoryRoot)
{
    public const string BasePackageId = "com.bengine.tests.source-base";
    public const string ConsumerPackageId = "com.bengine.tests.source-consumer";
    public const string BaseRuntimeAssembly = "BEngine.Tests.SourceBase";
    public const string BaseEditorAssembly = "BEngine.Tests.SourceBase.Editor";
    public const string ConsumerRuntimeAssembly = "BEngine.Tests.SourceConsumer";
    public const string ConsumerEditorAssembly = "BEngine.Tests.SourceConsumer.Editor";

    public string RepositoryRoot { get; } = Path.GetFullPath(repositoryRoot);

    public void Create()
    {
        CreateBasePackage();
        CreateConsumerPackage();
    }

    public string CachedPackageRoot(string projectPackagesPath, string packageId) =>
        Path.Combine(projectPackagesPath, packageId);

    public string CachedBaseSource(string projectPackagesPath) => Path.Combine(
        CachedPackageRoot(projectPackagesPath, BasePackageId),
        BaseRuntimeAssembly,
        "BaseValue.cs");

    private void CreateBasePackage()
    {
        var root = Path.Combine(RepositoryRoot, "SourceBase");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "package.yaml"), $$"""
            format: BEngine.Package
            version: 2
            id: {{BasePackageId}}
            packageVersion: 1.0.0
            displayName: Source Base
            description: Source package compilation base fixture.
            enabledByDefault: false
            runtime:
              assembly: {{BaseRuntimeAssembly}}
              rootNamespace: BEngine.Tests.SourceBase
              dependencies: []
            editor:
              assembly: {{BaseEditorAssembly}}
              rootNamespace: BEngine.Tests.SourceBase.Editor
              dependencies:
              - packageId: {{BasePackageId}}
                target: runtime
            """);
        WriteSource(root, BaseRuntimeAssembly, "BaseValue.cs", """
            #if !BENGINE || !BENGINE_1_0 || !BENGINE_1_0_OR_NEWER
            #error BEngine identity and version symbols were not passed to a runtime package.
            #endif
            #if !BENGINE || BENGINE_EDITOR
            #error Runtime package context symbols are invalid.
            #endif
            #if !DEBUG || RELEASE
            #error Runtime package build configuration symbols are invalid.
            #endif
            #if !BENGINE_WINDOWS && !BENGINE_LINUX && !BENGINE_OSX
            #error No supported BEngine platform symbol was passed to a runtime package.
            #endif

            namespace BEngine.Tests.SourceBase;

            public static class BaseValue
            {
                public static string Get() => "base-v1";
            }
            """);
        WriteAssemblyDefinition(root, BaseRuntimeAssembly, "BEngine.Tests.SourceBase", [], false);
        WriteSource(root, BaseEditorAssembly, "BaseEditorValue.cs", """
            #if !BENGINE || !BENGINE_EDITOR
            #error Editor package context symbols are invalid.
            #endif
            #if !BENGINE_1_0 || !BENGINE_1_0_OR_NEWER || !DEBUG || RELEASE
            #error Editor package version or configuration symbols are invalid.
            #endif

            using BEngine.Tests.SourceBase;

            namespace BEngine.Tests.SourceBase.Editor;

            public static class BaseEditorValue
            {
                public static string Get() => $"editor:{BaseValue.Get()}";
            }
            """);
        WriteAssemblyDefinition(root, BaseEditorAssembly, "BEngine.Tests.SourceBase.Editor",
            [BaseRuntimeAssembly], true);
    }

    private void CreateConsumerPackage()
    {
        var root = Path.Combine(RepositoryRoot, "SourceConsumer");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "package.yaml"), $$"""
            format: BEngine.Package
            version: 2
            id: {{ConsumerPackageId}}
            packageVersion: 1.0.0
            displayName: Source Consumer
            description: Source package compilation dependency fixture.
            enabledByDefault: false
            runtime:
              assembly: {{ConsumerRuntimeAssembly}}
              rootNamespace: BEngine.Tests.SourceConsumer
              dependencies:
              - packageId: {{BasePackageId}}
                target: runtime
            editor:
              assembly: {{ConsumerEditorAssembly}}
              rootNamespace: BEngine.Tests.SourceConsumer.Editor
              dependencies:
              - packageId: {{ConsumerPackageId}}
                target: runtime
              - packageId: {{BasePackageId}}
                target: editor
            """);
        WriteSource(root, ConsumerRuntimeAssembly, "ConsumerValue.cs", """
            using BEngine.Tests.SourceBase;

            namespace BEngine.Tests.SourceConsumer;

            public static class ConsumerValue
            {
                public static string Get() => $"consumer:{BaseValue.Get()}";
            }
            """);
        WriteAssemblyDefinition(root, ConsumerRuntimeAssembly, "BEngine.Tests.SourceConsumer",
            [BaseRuntimeAssembly], false);
        WriteSource(root, ConsumerEditorAssembly, "ConsumerEditorValue.cs", """
            using BEngine.Tests.SourceBase.Editor;
            using BEngine.Tests.SourceConsumer;

            namespace BEngine.Tests.SourceConsumer.Editor;

            public static class ConsumerEditorValue
            {
                public static string Get() => $"{ConsumerValue.Get()}:{BaseEditorValue.Get()}";
            }
            """);
        WriteAssemblyDefinition(root, ConsumerEditorAssembly, "BEngine.Tests.SourceConsumer.Editor",
            [ConsumerRuntimeAssembly, BaseEditorAssembly], true);
    }

    private static void WriteSource(string packageRoot, string assemblyName, string fileName, string source)
    {
        var directory = Path.Combine(packageRoot, assemblyName);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), source);
    }

    private static void WriteAssemblyDefinition(
        string packageRoot,
        string assemblyName,
        string rootNamespace,
        IEnumerable<string> references,
        bool editorOnly)
    {
        var directory = Path.Combine(packageRoot, assemblyName);
        new AssemblyDefinitionDocument
        {
            Name = assemblyName,
            RootNamespace = rootNamespace,
            References = references.ToList(),
            EditorOnly = editorOnly
        }.Save(Path.Combine(directory, $"{assemblyName}.asmdef.yaml"));
    }
}
