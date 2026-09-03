using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.RegularExpressions;

namespace BEngine.ExampleTests.SourceLayout;

internal static class SourceLayoutAudit
{
    // These files intentionally keep a small public contract family beside its serializer/factory.
    // Exact type lists keep the exception narrow: adding or reordering a type still fails the audit.
    private static readonly IReadOnlyDictionary<string, string[]> AllowedTypeAggregates =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Core/BEngine/AssetBundles/VirtualAssetBundleSnapshot.cs"] =
                ["VirtualAssetBundleEntry", "VirtualAssetBundleSnapshot"],
            ["Core/BEngine/Build/BuildTargetDescriptor.cs"] =
                ["BuildTargetPlatform", "BuildArchitecture", "BuildTargetDescriptor", "BuildTargetCatalog"],
            ["Core/BEngine/Build/BuildTargetManifest.cs"] =
                ["BuildTargetManifest", "BuildTargetManifestSerializer"],
            ["Core/BEngine/Content/ContentBootstrap.cs"] =
                ["ContentEnvironmentKind", "ActivatedContentRelease", "IContentBootstrapper", "WorkspaceContentBootstrapper"],
            ["Core/BEngine/HotUpdate/HotUpdateActivationGate.cs"] =
                ["HotUpdateActivationGate", "PreparedHotUpdateDomain", "HotUpdateDomain"],
            ["Core/BEngine/HotUpdate/HotUpdateModules.cs"] =
                ["HotServiceReplacementPolicy", "HotUpdateModuleDescriptor", "IHotUpdateModule", "IHotUpdateModuleContext", "IHotUpdateServiceRegistry", "HotUpdateServiceRegistry"],
            ["Core/BEngine/HotUpdate/ManagedCodeContracts.cs"] =
                ["HotUpdateContract", "ManagedCodeRuntimeKind", "ManagedCodeModule", "ManagedCodeRelease", "ManagedCodeRuntimeRequest", "IManagedCodeRuntimeProvider", "IManagedCodeRuntimeFactory", "IManagedCodeRuntime", "ManagedCodeLoadResult", "ManagedCodeGraph"],
            ["Core/BEngine/HotUpdate/ManagedCodeReleaseManifest.cs"] =
                ["ManagedCodeReleaseManifest", "ManagedCodeModuleManifest", "ManagedCodeReleaseManifestSerializer"],
            ["Core/BEngine/ProjectSystem/Workspace/RuntimeManagedCodeReleaseInputCollector.cs"] =
                ["RuntimeManagedCodeReleaseInput", "RuntimePackageResourceInput", "RuntimeManagedCodeReleaseInputSet", "RuntimeManagedCodeReleaseInputCollector"],
            ["Core/BEngine/Rendering/Rhi/Backends/GraphicsBackendSelector.cs"] =
                ["GraphicsBackendSelection", "GraphicsBackendSelector"],
            ["Core/BEngine.Player/Application/PlayerRuntimeSession.cs"] =
                ["BEnginePlayer", "PlayerRuntimeSession"],
            ["Core/BEngine.Player/HotUpdate/AotInterpreterManagedCodeRuntime.cs"] =
                ["AotInterpreterRuntimeHost", "AotInterpreterManagedCodeRuntimeProvider", "AotInterpreterManagedCodeRuntime"],
            ["Core/BEngine.Player/HotUpdate/CoreClrManagedCodeRuntime.cs"] =
                ["CoreClrManagedCodeRuntimeProvider", "PlayerManagedCodeRuntimeFactory", "CoreClrManagedCodeRuntime"],
            ["Core/BEngine.Editor/Editor/Build/DotNetBuildInfrastructure.cs"] =
                ["DotNetBuildEnvironment", "DotNetBuildEnvironmentProbe", "DotNetBuildHostLocator", "DotNetPublishRunner"],
            ["Core/BEngine.Editor/Editor/Build/DotNetPlatformBuildTargetProviders.cs"] =
                ["DotNetAndroidBuildTargetProvider", "DotNetIosBuildTargetProvider", "DotNetWebAssemblyBuildTargetProvider", "DotNetPlatformBuildTargetProvider"],
            ["Core/BEngine.Editor/Editor/Build/PlatformPlayerBuildCapabilities.cs"] =
                ["PlatformPlayerRendererCapability", "PlatformPlayerBuildCapabilities"],
            ["Core/BEngine.Editor/Editor/Build/PlayerBuildContracts.cs"] =
                ["PlayerBuildRequest", "PlayerBuildResult", "PlayerBuildPhase", "PlayerBuildProgress", "IPlayerBuildTargetProvider", "PlayerBuildPrerequisite", "IPlayerBuildPrerequisiteProvider", "PlayerBuildContext"]
        };

    private static readonly IReadOnlyDictionary<string, string> AllowedFileTypeAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Core/BEngine/Core/Async/ResourceRequest.Generic.cs"] = "ResourceRequest"
        };

    public static SourceLayoutResult Run(string startDirectory)
    {
        var repository = FindRepository(startDirectory);
        var sourceRoot = Path.Combine(repository, "src");
        var projects = Directory.EnumerateFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(IsSourcePath)
            .ToArray();
        var sourceFiles = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(IsSourcePath)
            .ToArray();
        var violations = new List<string>();
        var typeCount = 0;

        ValidatePackageLayout(sourceRoot, violations);

        foreach (var project in projects)
        {
            var looseSources = Directory.EnumerateFiles(Path.GetDirectoryName(project)!, "*.cs",
                SearchOption.TopDirectoryOnly);
            violations.AddRange(looseSources.Select(path =>
                $"Assembly root contains loose source: {Path.GetRelativePath(sourceRoot, path)}"));
        }

        foreach (var sourceFile in sourceFiles)
        {
            var relativePath = ToSolutionPath(sourceRoot, sourceFile).Replace('\\', '/');
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(sourceFile),
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14));
            var errors = tree.GetDiagnostics().Where(item => item.Severity == DiagnosticSeverity.Error).ToArray();
            if (errors.Length > 0)
            {
                violations.Add($"Invalid C# source {Path.GetRelativePath(sourceRoot, sourceFile)}: {errors[0]}");
                continue;
            }

            var types = GetTopLevelTypes(tree.GetCompilationUnitRoot()).ToArray();
            typeCount += types.Length;
            if (types.Length > 1)
            {
                var typeNames = types.Select(GetTypeName).ToArray();
                if (!AllowedTypeAggregates.TryGetValue(relativePath, out var expectedTypes) ||
                    !typeNames.SequenceEqual(expectedTypes, StringComparer.Ordinal))
                    violations.Add($"Multiple top-level types in {Path.GetRelativePath(sourceRoot, sourceFile)}: " +
                        string.Join(", ", typeNames));
                continue;
            }

            if (types.Length != 1) continue;
            var typeName = GetTypeName(types[0]);
            if (!Path.GetFileNameWithoutExtension(sourceFile).Equals(typeName, StringComparison.Ordinal) &&
                (!AllowedFileTypeAliases.TryGetValue(relativePath, out var expectedType) ||
                 !expectedType.Equals(typeName, StringComparison.Ordinal)))
                violations.Add($"File/type mismatch: {Path.GetRelativePath(sourceRoot, sourceFile)} contains {typeName}");
        }

        if (violations.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, violations));
        return new SourceLayoutResult(sourceFiles.Length, typeCount, projects.Length);
    }

    private static IEnumerable<MemberDeclarationSyntax> GetTopLevelTypes(CompilationUnitSyntax root)
    {
        foreach (var member in root.Members)
        {
            if (IsType(member)) yield return member;
            if (member is not BaseNamespaceDeclarationSyntax namespaceDeclaration) continue;
            foreach (var namespaceMember in namespaceDeclaration.Members.Where(IsType))
                yield return namespaceMember;
        }
    }

    private static bool IsType(MemberDeclarationSyntax member) =>
        member is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax;

    private static string GetTypeName(MemberDeclarationSyntax member) => member switch
    {
        BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
        DelegateDeclarationSyntax type => type.Identifier.ValueText,
        _ => throw new ArgumentOutOfRangeException(nameof(member))
    };

    private static bool IsSourcePath(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase) &&
        !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase) &&
        !path.Contains($"{Path.DirectorySeparatorChar}.vs{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);

    private static void ValidatePackageLayout(string sourceRoot, ICollection<string> violations)
    {
        var packages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Core"] = "Core",
            ["Animation"] = Path.Combine("Packages", "Animation"),
            ["Navigation2D"] = Path.Combine("Packages", "Navigation2D"),
            ["Physics2D"] = Path.Combine("Packages", "Physics2D"),
            ["PropertyAttributes"] = Path.Combine("Packages", "PropertyAttributes"),
            ["TiledMap"] = Path.Combine("Packages", "TiledMap"),
            ["UIElements"] = Path.Combine("Packages", "UIElements")
        };
        var expectedItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var expectedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (package, relativeRoot) in packages)
        {
            var packageRoot = Path.Combine(sourceRoot, relativeRoot);
            var rootReadme = Path.Combine(packageRoot, "Readme.md");
            var editorReadme = Path.Combine(packageRoot, "Editor", "Readme.md");
            var legacyEditorDirectory = Path.Combine(packageRoot, "EditorResources");
            if (File.Exists(rootReadme))
                violations.Add($"Package Readme must be in Editor: {package}/Readme.md");
            if (!File.Exists(editorReadme))
                violations.Add($"Package has no Editor/Readme.md: {package}");
            if (Directory.Exists(legacyEditorDirectory))
                violations.Add($"Package retained legacy EditorResources directory: {package}");

            var packageDefinition = Path.Combine(packageRoot, "package.yaml");
            if (File.Exists(packageDefinition))
            {
                expectedItems.Add(ToSolutionPath(sourceRoot, packageDefinition));
                var definition = File.ReadAllText(packageDefinition);
                if (!Regex.IsMatch(definition,
                        "(?m)^content:\\r?$\\n^  runtime: Resources\\r?$\\n^  editor: Editor\\r?$"))
                    violations.Add($"Package content mapping must be Resources/Editor: {package}");
            }
            foreach (var folder in new[] { "Resources", "Editor" })
            {
                foreach (var directory in Directory.EnumerateDirectories(Path.Combine(packageRoot, folder), "*",
                             SearchOption.AllDirectories))
                    expectedFolders.Add(ToSolutionPath(sourceRoot, directory));
                foreach (var file in Directory.EnumerateFiles(Path.Combine(packageRoot, folder), "*",
                             SearchOption.AllDirectories))
                    expectedItems.Add(ToSolutionPath(sourceRoot, file));
            }
        }

        var solutionPath = Path.Combine(sourceRoot, "BEngine.sln");
        var packagePrefixes = packages.Values
            .Select(path => path.Replace('/', '\\') + "\\")
            .ToArray();
        var actualItems = ReadSolutionItems(solutionPath)
            .Where(path => packagePrefixes.Any(prefix => path.StartsWith(prefix,
                StringComparison.OrdinalIgnoreCase)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var missing in expectedItems.Except(actualItems, StringComparer.OrdinalIgnoreCase))
            violations.Add($"BEngine.sln does not display physical file: {missing}");
        foreach (var stale in actualItems.Except(expectedItems, StringComparer.OrdinalIgnoreCase))
            violations.Add($"BEngine.sln displays a missing or misplaced file: {stale}");

        var solutionText = File.ReadAllText(solutionPath);
        var actualFolders = Regex.Matches(solutionText,
                "(?m)^Project\\(\"\\{2150E333-8FDC-42A3-9474-1A3956D46DE8\\}\"\\) = " +
                "\"[^\"]+\", \"(?<path>[^\"]+)\", \"\\{[0-9A-F-]+\\}\"\\r?$")
            .Select(match => match.Groups["path"].Value)
            .Where(path => packages.Values.Any(packageRoot =>
                path.StartsWith(packageRoot.Replace('/', '\\') + "\\Resources\\",
                    StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(packageRoot.Replace('/', '\\') + "\\Editor\\",
                    StringComparison.OrdinalIgnoreCase)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var missing in expectedFolders.Except(actualFolders, StringComparer.OrdinalIgnoreCase))
            violations.Add($"BEngine.sln has no explicit solution folder for: {missing}");
        foreach (var stale in actualFolders.Except(expectedFolders, StringComparer.OrdinalIgnoreCase))
            violations.Add($"BEngine.sln contains a stale solution folder: {stale}");
    }

    private static IEnumerable<string> ReadSolutionItems(string solutionPath)
    {
        var inItems = false;
        foreach (var line in File.ReadLines(solutionPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Equals("ProjectSection(SolutionItems) = preProject", StringComparison.Ordinal))
            {
                inItems = true;
                continue;
            }
            if (inItems && trimmed.Equals("EndProjectSection", StringComparison.Ordinal))
            {
                inItems = false;
                continue;
            }
            if (!inItems) continue;
            var match = Regex.Match(trimmed, "^(?<path>.+?)\\s+=\\s+\\k<path>$");
            if (match.Success) yield return match.Groups["path"].Value.Replace('/', '\\');
        }
    }

    private static string ToSolutionPath(string sourceRoot, string path) =>
        Path.GetRelativePath(sourceRoot, path).Replace('/', '\\');

    private static string FindRepository(string startDirectory)
    {
        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "BEngine.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the BEngine repository root.");
    }
}
