using BEngine.ProjectSystem.Editor;

namespace BEngine.ExampleTests.ProjectShaderCompilation;

internal static class Program
{
    private static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), $"BEngine-ShaderCompilation-{Guid.NewGuid():N}");
        try
        {
            var workspace = ProjectWorkspaceFactory.Create(root, "Shader Compilation");
            var shaderDirectory = Path.Combine(workspace.AssetsPath, "Shaders");
            Directory.CreateDirectory(shaderDirectory);
            var shaderPath = Path.Combine(shaderDirectory, "Color.frag.glsl");
            File.WriteAllText(shaderPath, Fragment("1.0, 0.0, 0.0, 1.0"));

            var first = ProjectShaderCompiler.CompileChanged(workspace, [shaderPath]);
            Require(first.Succeeded && first.CompiledArtifacts.Count == 1,
                "The first shader compilation failed.");
            var firstArtifact = first.CompiledArtifacts.Single().Value;
            Require(IsSpirv(firstArtifact), "The shader compiler did not emit SPIR-V bytecode.");

            var reused = ProjectShaderCompiler.CompileChanged(workspace, [shaderPath]);
            Require(reused.Succeeded && reused.CompiledArtifacts.Single().Value == firstArtifact,
                "An unchanged shader did not reuse its content-addressed artifact.");

            File.WriteAllText(shaderPath, Fragment("0.0, 1.0, 0.0, 1.0"));
            var changed = ProjectShaderCompiler.CompileChanged(workspace, [shaderPath]);
            var changedArtifact = changed.CompiledArtifacts.Single().Value;
            Require(changedArtifact != firstArtifact && IsSpirv(changedArtifact),
                "A changed shader did not produce a new SPIR-V artifact.");

            File.WriteAllText(shaderPath, "#pragma stage fragment\nthis is not valid glsl");
            var failed = ProjectShaderCompiler.CompileChanged(workspace, [shaderPath]);
            Require(!failed.Succeeded && failed.Errors.ContainsKey("Assets/Shaders/Color.frag.glsl"),
                "An invalid shader was not reported as a per-asset compilation error.");
            Require(ProjectShaderCompiler.ResolveCurrentArtifact(workspace, shaderPath) == changedArtifact,
                "A failed shader compilation replaced the last valid artifact.");

            File.Delete(shaderPath);
            _ = ProjectShaderCompiler.CompileChanged(workspace, [shaderPath]);
            Require(ProjectShaderCompiler.ResolveCurrentArtifact(workspace, shaderPath) is null,
                "Deleting a shader did not invalidate its current artifact pointer.");

            Console.WriteLine("PROJECT_SHADER_COMPILATION_OK|spirv,cache,changed,rollback,delete");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"PROJECT_SHADER_COMPILATION_FAILED|{exception}");
            return 1;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static string Fragment(string color) =>
        "#version 450\nlayout(location = 0) out vec4 OutputColor;\n" +
        $"void main() {{ OutputColor = vec4({color}); }}\n";

    private static bool IsSpirv(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return bytes.Length >= 4 && bytes[0] == 0x03 && bytes[1] == 0x02 &&
               bytes[2] == 0x23 && bytes[3] == 0x07;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
