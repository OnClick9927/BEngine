using System.Reflection;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;

namespace BEngine.Testing.Editor;

public sealed class TestCommandLine : IEditorCommandLineTool
{
    public string Option => "--run-tests";
    public int Run(IReadOnlyList<string> arguments) => RunAsync(arguments).GetAwaiter().GetResult();
    public static async Task<int> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < arguments.Count; index++)
        {
            var key = arguments[index];
            if (key == "--coverage") { options[key] = "true"; continue; }
            if (key is not ("--project" or "--mode" or "--filter" or "--category" or "--results" or "--timeout" or "--target"))
                throw new ArgumentException($"Unknown test option '{key}'.");
            if (++index >= arguments.Count || arguments[index].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Option '{key}' requires a value.");
            if (!options.TryAdd(key, arguments[index])) throw new ArgumentException($"Duplicate test option '{key}'.");
        }
        if (!options.TryGetValue("--project", out var project)) throw new ArgumentException("Usage: --run-tests --project <directory> [--mode Edit|Play|Player|All] [--filter text] [--category name] [--coverage] [--results directory] [--timeout seconds] [--target id]");
        var mode = options.GetValueOrDefault("--mode", "Edit");
        if (!mode.Equals("All", StringComparison.OrdinalIgnoreCase) && !Enum.TryParse<TestMode>(mode, true, out _)) throw new ArgumentException("Invalid test mode.");
        var timeout = int.Parse(options.GetValueOrDefault("--timeout", "600"), System.Globalization.CultureInfo.InvariantCulture);
        if (timeout <= 0) throw new ArgumentOutOfRangeException("--timeout");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromSeconds(timeout));
        using var instance = EditorInstanceContext.Create(project); var workspace = ProjectWorkspace.Open(project);
        using var packages = new BPackageManager(workspace, null, loadAssemblies: false);
        var compiler = EditorServiceAccess.GetRequiredService<IProjectScriptCompiler>();
        var host = DispatchProxy.Create<IEditorHost, CommandHost>(); ((CommandHost)(object)host).Root = workspace.RootPath; EditorBridge.Attach(host);
        try
        {
            deadline.Token.ThrowIfCancellationRequested();
            var runtime = compiler.CompileRuntimeAndLoad(workspace); compiler.CompileEditorAndLoad(workspace, runtime, typeof(EditorApplication).Assembly.Location);
            var assemblyRoot = Path.GetFullPath(instance.scriptAssembliesPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var projectAssemblies = AppDomain.CurrentDomain.GetAssemblies().Where(assembly => !assembly.IsDynamic && assembly.Location.Length > 0 &&
                Path.GetFullPath(assembly.Location).StartsWith(assemblyRoot, StringComparison.OrdinalIgnoreCase));
            var discovered = TestDiscovery.Discover(projectAssemblies); TestSourceLocator.Populate(discovered);
            var selected = discovered.Where(test =>
                (mode.Equals("All", StringComparison.OrdinalIgnoreCase) || test.Mode.ToString().Equals(mode, StringComparison.OrdinalIgnoreCase)) &&
                (!options.TryGetValue("--filter", out var filter) || test.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)) &&
                (!options.TryGetValue("--category", out var category) || test.Category.Equals(category, StringComparison.OrdinalIgnoreCase))).ToArray();
            var started = DateTimeOffset.UtcNow; var results = new List<TestResult>(); var coverage = options.ContainsKey("--coverage");
            if (coverage) CodeCoverage.BeginSession();
            var local = selected.Where(test => test.Mode != TestMode.Player).ToArray();
            if (local.Length > 0) results.AddRange((await TestExecutor.RunAsync(local, new() { CollectCoverage = coverage }, deadline.Token)).Tests);
            if (selected.Any(test => test.Mode == TestMode.Player))
            {
                var player = await PlayerTestSession.RunProjectAsync(workspace.RootPath, coverage, options.GetValueOrDefault("--target"),
                    selected.Where(test => test.Mode == TestMode.Player).Select(test => test.Id).ToArray(), cancellationToken: deadline.Token);
                results.AddRange(player.Tests);
            }
            var result = new TestRunResult(started, DateTimeOffset.UtcNow, results, coverage ? CodeCoverage.Snapshot() : null);
            var output = Path.GetFullPath(options.GetValueOrDefault("--results", Path.Combine(workspace.RootPath, "TestResults")));
            TestReportWriter.WriteJson(Path.Combine(output, "results.json"), result); TestReportWriter.WriteJUnit(Path.Combine(output, "junit.xml"), result);
            if (result.Coverage is { } snapshot) { CodeCoverage.WriteJson(Path.Combine(output, "coverage.json"), snapshot); CodeCoverage.WriteLcov(Path.Combine(output, "coverage.lcov"), snapshot); }
            Console.WriteLine($"BENGINE_TEST_RESULTS|passed={result.Passed}|failed={result.Failed}|skipped={result.Skipped}|selected={selected.Length}|path={output}");
            return selected.Length == 0 ? 3 : result.Succeeded ? 0 : 2;
        }
        finally { EditorBridge.Detach(host); compiler.ReleaseLoadContexts(); }
    }
    public class CommandHost : DispatchProxy
    {
        public string Root { get; set; } = "";
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            "get_ProjectRootPath" => Root, "get_AssetsRootPath" => Path.Combine(Root, "Assets"),
            _ => method?.ReturnType.IsValueType == true && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null
        };
    }
}
