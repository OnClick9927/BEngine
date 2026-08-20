using BEngine.Editor;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;

namespace BEngine.ExampleTests.ThreadingArchitecture;

internal static class ScriptBuildApplyPipelineTests
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"BEngineScriptPipeline_{Guid.NewGuid():N}");
        var events = new List<CompilationEvent>();
        void Started(string path) => events.Add(new CompilationEvent(
            "Started", path, Environment.CurrentManagedThreadId, HasError: false));
        void Finished(string path, CompilerMessage[] messages) => events.Add(new CompilationEvent(
            "Finished", path, Environment.CurrentManagedThreadId,
            messages.Any(message => message.type == CompilerMessageType.Error)));

        CompilationPipeline.assemblyCompilationStarted += Started;
        CompilationPipeline.assemblyCompilationFinished += Finished;
        try
        {
            ProjectScriptCompiler.ReleaseLoadContexts();
            var workspace = ProjectWorkspaceFactory.Create(root, "Script Build Apply Threading");
            var preferred = ProjectScriptCompiler.PreferredAssemblyPaths(
                typeof(EditorWindow).Assembly.Location);
            using var packages = new BPackageManager(workspace, null, loadAssemblies: false);
            var runtimeReferences = RuntimePackageLoader.ResolveEnabledReferences(
                workspace, packages.definitions).AssemblyReferences;
            var editorReferences = EditorProjectScriptCompiler.PackageReferences(
                workspace, packages, preferred);

            using (var canceled = new CancellationTokenSource())
            {
                canceled.Cancel();
                var canceledFailure = Task.Run<Exception?>(() =>
                {
                    try
                    {
                        _ = ProjectScriptBuildPipeline.Build(
                            workspace, runtimeReferences, editorReferences, canceled.Token);
                        return null;
                    }
                    catch (Exception exception)
                    {
                        return exception;
                    }
                }).GetAwaiter().GetResult();
                TestAssert.Require(canceledFailure is OperationCanceledException && events.Count == 0,
                    "A canceled script Build produced compilation events or did not cancel.");
            }

            var ownerThreadId = Environment.CurrentManagedThreadId;
            var buildThreadId = 0;
            var build = Task.Run(() =>
            {
                buildThreadId = Environment.CurrentManagedThreadId;
                return ProjectScriptBuildPipeline.Build(
                    workspace, runtimeReferences, editorReferences, CancellationToken.None);
            }).GetAwaiter().GetResult();
            var artifacts = build.Runtime.CompiledAssemblies
                .Concat(build.Editor.CompiledAssemblies).ToArray();

            TestAssert.Require(buildThreadId != ownerThreadId,
                "ProjectScriptBuildPipeline.Build did not run on a background worker.");
            TestAssert.Require(artifacts.Length >= 2 &&
                               build.Runtime.CompiledAssemblies.Count > 0 &&
                               build.Editor.CompiledAssemblies.Count > 0,
                "The minimal script fixture did not build both runtime and editor assemblies.");
            TestAssert.Require(events.Count == 0,
                "The background script Build raised main-thread compilation events before Apply.");
            TestAssert.Require(artifacts.All(artifact =>
                    ScriptAssemblyStore.ResolveCurrentPath(workspace, artifact.Node.Name) is null),
                "The background script Build published a current assembly before Apply.");

            var failedBuild = WithMissingEditorArtifact(workspace, build);
            var failedArtifacts = failedBuild.Runtime.CompiledAssemblies
                .Concat(failedBuild.Editor.CompiledAssemblies).ToArray();
            var applyFailure = CaptureException(() =>
                ProjectScriptBuildPipeline.Apply(workspace, failedBuild));
            TestAssert.Require(applyFailure is not null,
                "Script Apply unexpectedly succeeded with a missing editor assembly.");
            var expectedFailureEvents = failedArtifacts.Select(artifact =>
                    new CompilationEvent("Started", artifact.AssemblyPath, ownerThreadId, HasError: false))
                .Concat(failedArtifacts.Select(artifact =>
                    new CompilationEvent("Finished", artifact.AssemblyPath, ownerThreadId, HasError: true)))
                .ToArray();
            TestAssert.Require(events.SequenceEqual(expectedFailureEvents),
                "A failed Script Apply did not finish every started artifact with an Error on the owner thread. " +
                $"Actual: {Describe(events)}");
            TestAssert.Require(artifacts.All(artifact =>
                    ScriptAssemblyStore.ResolveCurrentPath(workspace, artifact.Node.Name) is null),
                "A failed Script Apply published an assembly from an incomplete transaction.");

            events.Clear();
            ProjectScriptBuildPipeline.Apply(workspace, build);

            TestAssert.Require(artifacts.All(artifact =>
                PathsEqual(
                    ScriptAssemblyStore.ResolveCurrentPath(workspace, artifact.Node.Name),
                    artifact.AssemblyPath)),
                "Script Apply did not publish every compiled assembly as current.");

            var expected = artifacts.Select(artifact =>
                    new CompilationEvent("Started", artifact.AssemblyPath, ownerThreadId, HasError: false))
                .Concat(artifacts.Select(artifact =>
                    new CompilationEvent("Finished", artifact.AssemblyPath, ownerThreadId, HasError: false)))
                .ToArray();
            TestAssert.Require(events.SequenceEqual(expected),
                "Script Apply did not raise every Started event before every successful Finished event " +
                $"in artifact order on the owner thread. Actual: {Describe(events)}");

            var loadedNames = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetName().Name)
                .OfType<string>()
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            TestAssert.Require(artifacts.All(artifact => loadedNames.Contains(artifact.Node.Name)),
                "Script Apply emitted completion events without loading every compiled assembly.");
        }
        finally
        {
            CompilationPipeline.assemblyCompilationStarted -= Started;
            CompilationPipeline.assemblyCompilationFinished -= Finished;
            ProjectScriptCompiler.ReleaseLoadContexts();
            TryDelete(root);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static bool PathsEqual(string? left, string right) =>
        left is not null && Path.GetFullPath(left).Equals(
            Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static ProjectScriptBuildResult WithMissingEditorArtifact(
        ProjectWorkspace workspace,
        ProjectScriptBuildResult build)
    {
        var editorArtifacts = build.Editor.CompiledAssemblies.ToArray();
        var source = editorArtifacts[^1];
        editorArtifacts[^1] = new CompiledProjectAssembly
        {
            Node = source.Node,
            BuildId = source.BuildId,
            AssemblyPath = Path.Combine(workspace.TempPath, "Missing", Path.GetFileName(source.AssemblyPath))
        };
        return new ProjectScriptBuildResult(build.Runtime, new ProjectAssemblyBuildResult(
            editorArtifacts, build.Editor.BaseReferences, build.Editor.ExistingProjectReferences));
    }

    private static Exception? CaptureException(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static string Describe(IEnumerable<CompilationEvent> events) => string.Join(", ",
        events.Select(item =>
            $"{item.Kind}:{Path.GetFileName(item.Path)}@{item.ThreadId}:error={item.HasError}"));

    private sealed record CompilationEvent(string Kind, string Path, int ThreadId, bool HasError);
}
