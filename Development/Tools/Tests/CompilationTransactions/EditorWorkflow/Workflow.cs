using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using BEngine.Editor;

// Copied only into the temporary Editor verification project.
public static class CompilationTransactionWorkflow
{
    static string Root => Environment.GetEnvironmentVariable("BENGINE_COMPILATION_WORKFLOW_RESULT") ?? "";
    static string Stage => Path.Combine(Root, "Stage");
    static string Events => Path.Combine(Root, "Events");
    static int Thread;
    static bool Completed;

    [InitializeOnLoadMethod]
    public static void Bind()
    {
        if (Root.Length == 0) return;
        Thread = Environment.CurrentManagedThreadId;
        EditorApplication.update += Tick;
        CompilationPipeline.compilationStarted += Started;
        CompilationPipeline.compilationFinished += Finished;
    }

    static void Started(object context) => Record("start");
    static void Finished(object context)
    {
        Record("finish");
        File.WriteAllText(Stage, "finished");
    }
    static void Record(string kind) => File.AppendAllText(Events,
        kind + ":" + (CompilationPipeline.isCompiling && EditorApplication.isCompiling &&
                       Environment.CurrentManagedThreadId == Thread) + "\n");

    static void Tick()
    {
        if (Completed) return;
        try
        {
            if (!File.Exists(Stage))
            {
                if (CompilationPipeline.isCompiling || EditorApplication.isCompiling)
                    throw new Exception("Initial Editor compilation left active state behind.");
                File.WriteAllText(Stage, "requested");
                // Superseding a pending request must retain one continuous transaction.
                CompilationPipeline.RequestScriptCompilation();
                CompilationPipeline.RequestScriptCompilation();
                if (!CompilationPipeline.isCompiling || !EditorApplication.isCompiling)
                    throw new Exception("Background work did not retain its transaction.");
                return;
            }
            if (File.ReadAllText(Stage) != "finished") return;
            var events = File.ReadAllLines(Events);
            var passed = events.SequenceEqual(new[] { "start:True", "finish:True" }) &&
                         !CompilationPipeline.isCompiling && !EditorApplication.isCompiling;
            Complete(passed, passed ? "" : string.Join(",", events));
        }
        catch (Exception failure) { Complete(false, failure.ToString()); }
    }
    static void Complete(bool passed, string error)
    {
        Completed = true;
        File.WriteAllText(Path.Combine(Root, "Result.json"), JsonSerializer.Serialize(new
        {
            Passed = passed, Checks = 6, Error = error,
            Details = new[] { "Initial compilation ends before the first update", "First request begins a transaction",
                "Superseded request shares that transaction", "Start and finish run once on the editor thread",
                "Reloaded callbacks observe Core and facade state", "Background apply returns to idle" }
        }));
        EditorApplication.Exit(passed ? 0 : 1);
    }
}
