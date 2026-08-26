using BEngine.Rendering.Rhi;

namespace BEngine.ExampleTests.DefaultShaderResources;

internal static class Program
{
    private static int Main()
    {
        try
        {
            var repository = FindRepositoryRoot();
            var core = Path.Combine(repository, "src", "Core");
            var uiElements = Path.Combine(repository, "src", "Packages", "UIElements");
            Resources.RegisterResourceRoot(core);
            Resources.RegisterResourceRoot(uiElements);
            var coreShaders = EnumerateShaders(Path.Combine(core, "Resources", "Shaders"));
            var uiShaders = EnumerateShaders(Path.Combine(uiElements, "Resources", "Shaders"));
            var shaders = coreShaders.Concat(uiShaders).ToArray();

            Require(coreShaders.Length == 25, $"Core owns {coreShaders.Length} shaders instead of 25.");
            Require(uiShaders.Length == 14, $"UIElements owns {uiShaders.Length} shaders instead of 14.");
            Require(coreShaders.Count(path => Path.GetFileName(path).StartsWith(
                        "Textured.", StringComparison.OrdinalIgnoreCase) &&
                    path.Contains($"{Path.DirectorySeparatorChar}PortableScene{Path.DirectorySeparatorChar}",
                        StringComparison.OrdinalIgnoreCase)) == 7,
                "PortableScene does not expose all seven textured shader sources.");
            foreach (var shader in shaders)
            {
                var packageRoot = shader.StartsWith(core, StringComparison.OrdinalIgnoreCase) ? core : uiElements;
                var resourcePath = Path.GetRelativePath(Path.Combine(packageRoot, "Resources"), shader)
                    .Replace(Path.DirectorySeparatorChar, '/');
                var source = BEngine.Rendering.Rhi.DefaultShaderResources.Load(resourcePath);
                Require(!string.IsNullOrWhiteSpace(source), $"Shader '{resourcePath}' is empty.");
                Require(Path.GetExtension(shader) switch
                {
                    ".glsl" => source.Contains("#version", StringComparison.Ordinal),
                    ".hlsl" => source.Contains("SV_Position", StringComparison.Ordinal),
                    ".wgsl" => source.Contains("@vertex", StringComparison.Ordinal) &&
                               source.Contains("@fragment", StringComparison.Ordinal),
                    _ => false
                }, $"Shader '{resourcePath}' does not contain a valid language signature.");
            }

            var embeddedSources = Directory.EnumerateFiles(Path.Combine(repository, "src"), "*.cs",
                    SearchOption.AllDirectories)
                .Where(path => File.ReadAllText(path).Contains("#version", StringComparison.Ordinal))
                .Where(path => !path.EndsWith(
                    Path.Combine("Editor", "Assets", "ProjectAssetCreation.cs"),
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            Require(embeddedSources.Length == 0,
                $"Shader source remains embedded in C#: {string.Join(", ", embeddedSources)}");
            Console.WriteLine($"DEFAULT_SHADER_RESOURCES_OK|core={coreShaders.Length}|ui-elements={uiShaders.Length}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"DEFAULT_SHADER_RESOURCES_FAILED|{exception}");
            return 1;
        }
    }

    private static string[] EnumerateShaders(string path) =>
        Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Where(file => Path.GetExtension(file) is ".glsl" or ".hlsl" or ".wgsl")
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase).ToArray();

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "BEngine.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("BEngine repository root was not found.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
