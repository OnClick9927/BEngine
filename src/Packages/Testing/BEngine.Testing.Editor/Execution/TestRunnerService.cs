using System.Reflection;

namespace BEngine.Testing.Editor;

public static class TestRunnerService
{
    public static IReadOnlyList<TestCase> Discover()
    {
        var tests = TestDiscovery.Discover(AppDomain.CurrentDomain.GetAssemblies()
            .Where(ReferencesTesting));
        TestSourceLocator.Populate(tests);
        return tests;
    }

    public static Task<TestRunResult> RunLocalAsync(
        IEnumerable<TestCase> tests,
        bool coverage,
        Action<TestResult>? report = null,
        CancellationToken cancellationToken = default) =>
        TestExecutor.RunAsync(tests.Where(test => test.Mode != TestMode.Player),
            new TestRunOptions { CollectCoverage = coverage, ResultReported = report }, cancellationToken);

    public static bool OpenSource(TestCase test)
    {
        if (string.IsNullOrWhiteSpace(test.SourceFile)) return false;
        var path = test.SourceFile;
        if (!Path.IsPathRooted(path)) path = Path.Combine(EditorApplication.projectPath, path);
        return File.Exists(path) && AssetDatabase.OpenAsset(path, test.SourceLine);
    }

    private static bool ReferencesTesting(Assembly assembly)
    {
        if (assembly.IsDynamic) return false;
        if (ReferenceEquals(assembly, typeof(TestAttribute).Assembly)) return true;
        try
        {
            var testingName = typeof(TestAttribute).Assembly.GetName().Name;
            return assembly.GetReferencedAssemblies().Any(reference => reference.Name == testingName);
        }
        catch { return false; }
    }
}
