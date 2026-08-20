using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;

namespace BEngine.ExampleTests.ThreadingArchitecture;

internal static class ShaderCompilationPipelineTests
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"BEngineShaderPipeline_{Guid.NewGuid():N}");
        try
        {
            var workspace = ProjectWorkspaceFactory.Create(root, "Shader Compile Apply Threading");
            const string assetPath = "Assets/Shaders/Transactional.frag.glsl";
            var sourcePath = workspace.ResolveInside(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllText(sourcePath, Fragment("1.0, 0.0, 0.0, 1.0"));

            var ownerThreadId = Environment.CurrentManagedThreadId;
            var compileThreadId = 0;
            var initial = Task.Run(() =>
            {
                compileThreadId = Environment.CurrentManagedThreadId;
                return ProjectShaderCompiler.CompileChangedInBackground(workspace, [assetPath]);
            }).GetAwaiter().GetResult();
            var initialArtifact = initial.CompiledArtifacts[assetPath];
            TestAssert.Require(compileThreadId != ownerThreadId && initial.Succeeded &&
                               File.Exists(initialArtifact),
                "The initial shader was not compiled successfully on a background worker.");
            TestAssert.Require(ProjectShaderCompiler.ResolveCurrentArtifact(workspace, assetPath) is null,
                "Background shader compilation created a current pointer before Apply.");

            ProjectShaderCompiler.ApplyCompilationResult(initial);
            TestAssert.Require(PathsEqual(
                    ProjectShaderCompiler.ResolveCurrentArtifact(workspace, assetPath), initialArtifact),
                "Shader Apply did not publish the initial compiled artifact.");

            File.WriteAllText(sourcePath, Fragment("0.0, 1.0, 0.0, 1.0"));
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                var canceledFailure = Task.Run<Exception?>(() =>
                {
                    try
                    {
                        _ = ProjectShaderCompiler.CompileChangedInBackground(
                            workspace, [assetPath], cancellation.Token);
                        return null;
                    }
                    catch (Exception exception)
                    {
                        return exception;
                    }
                }).GetAwaiter().GetResult();
                TestAssert.Require(canceledFailure is OperationCanceledException &&
                                   PathsEqual(ProjectShaderCompiler.ResolveCurrentArtifact(
                                       workspace, assetPath), initialArtifact),
                    "A pre-canceled shader compilation changed the current artifact.");
            }

            var changed = Task.Run(() =>
                    ProjectShaderCompiler.CompileChangedInBackground(workspace, [assetPath]))
                .GetAwaiter().GetResult();
            var changedArtifact = changed.CompiledArtifacts[assetPath];
            TestAssert.Require(!PathsEqual(changedArtifact, initialArtifact) &&
                               PathsEqual(ProjectShaderCompiler.ResolveCurrentArtifact(
                                   workspace, assetPath), initialArtifact),
                "Background shader recompilation changed current before Apply.");
            ProjectShaderCompiler.ApplyCompilationResult(changed);
            TestAssert.Require(PathsEqual(
                    ProjectShaderCompiler.ResolveCurrentArtifact(workspace, assetPath), changedArtifact),
                "Shader Apply did not replace current with the changed artifact.");

            File.Delete(sourcePath);
            var deleted = Task.Run(() =>
                    ProjectShaderCompiler.CompileChangedInBackground(workspace, [assetPath]))
                .GetAwaiter().GetResult();
            TestAssert.Require(deleted.Succeeded && deleted.DeletedAssets.Contains(
                                   assetPath, StringComparer.OrdinalIgnoreCase) &&
                               PathsEqual(ProjectShaderCompiler.ResolveCurrentArtifact(
                                   workspace, assetPath), changedArtifact),
                "Background shader deletion removed current before Apply.");
            ProjectShaderCompiler.ApplyCompilationResult(deleted);
            TestAssert.Require(ProjectShaderCompiler.ResolveCurrentArtifact(workspace, assetPath) is null,
                "Shader deletion Apply did not remove the current pointer.");
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string Fragment(string color) =>
        "#version 450\nlayout(location = 0) out vec4 OutputColor;\n" +
        $"void main() {{ OutputColor = vec4({color}); }}\n";

    private static bool PathsEqual(string? left, string right) =>
        left is not null && Path.GetFullPath(left).Equals(
            Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
