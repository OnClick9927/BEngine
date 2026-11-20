using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine.Editor;
using BEngine.Platform;

BEngine.Platform.DotNet.DotNetPlatformServices.Initialize(); DotNetEditorPlatform.Initialize();
var checks = new List<string>();
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks.Add(name); }
var type = Assembly.Load("BEngine.EditorWorkbench.Host").GetType("BEngine.Workbench.GpuEditorApplication", true)!;
// Fixture-only state injection targets the actual host's cancellation cleanup.
// No constructor, native window, GPU or worker is started by this isolated probe.
var host = RuntimeHelpers.GetUninitializedObject(type);
// Loading host types activates IMGUI's presentation isolation. This fixture has
// no UI frame or font backend; Core notification mechanics are tested headlessly.
RuntimeHelpers.RunModuleConstructor(Assembly.Load("BEngine.IMGUI.Editor").ManifestModule.ModuleHandle);
EditorFeatureIsolation.Current = null;
var flags = BindingFlags.Instance | BindingFlags.NonPublic;
var cancel = type.GetMethod("CancelScriptCompilation", flags)!;
var cancellationField = type.GetField("_scriptCompilationCancellation", flags)!;
var transactionField = type.GetField("_backgroundCompilation", flags)!;
var generationField = type.GetField("_scriptCompilationGeneration", flags)!;
var events = new List<string>();
Action<object> start = _ => events.Add("start"); Action<object> finish = _ => events.Add("finish");
CompilationPipeline.compilationStarted += start; CompilationPipeline.compilationFinished += finish;
try
{
    var source = new CancellationTokenSource();
    source.Token.Register(static () => throw new IOException("cancellation-callback"));
    cancellationField.SetValue(host, source);
    transactionField.SetValue(host, CompilationPipeline.BeginCompilation(new object()));
    var failed = false;
    try { cancel.Invoke(host, [true]); }
    catch (TargetInvocationException failure) when (failure.InnerException is AggregateException aggregate && aggregate.InnerExceptions.Any(e => e.Message == "cancellation-callback")) { failed = true; }
    Check(failed, "Cancellation callback errors remain observable");
    Check(!CompilationPipeline.isCompiling, "Throwing cancellation callbacks cannot strand Core transaction state");
    Check(cancellationField.GetValue(host) is null && transactionField.GetValue(host) is null, "Host releases cancellation and transaction ownership after failure");
    Check(events.SequenceEqual(["start", "finish"]), "Cancellation failure still emits exactly one matching finish");
    Check((int)generationField.GetValue(host)! == 1, "Cancellation invalidates queued completions before dispatching callbacks");
    var disposed = false; try { source.Cancel(); } catch (ObjectDisposedException) { disposed = true; }
    Check(disposed, "Faulting cancellation source is still disposed");
    cancel.Invoke(host, [true]); Check(events.Count == 2, "Repeated cancellation adds no duplicate finish");

    var replacement = new CancellationTokenSource();
    cancellationField.SetValue(host, replacement);
    transactionField.SetValue(host, CompilationPipeline.BeginCompilation(new object()));
    cancel.Invoke(host, [false]);
    Check(CompilationPipeline.isCompiling && events.Count == 3, "Superseded work preserves a continuous transaction");
    Check(cancellationField.GetValue(host) is null && transactionField.GetValue(host) is not null, "Superseding work releases only its cancellation source");
    cancel.Invoke(host, [true]); Check(!CompilationPipeline.isCompiling && events.Count == 4, "Later shutdown finishes the preserved transaction once");
}
finally
{
    cancel.Invoke(host, [true]);
    CompilationPipeline.compilationStarted -= start; CompilationPipeline.compilationFinished -= finish;
}
File.WriteAllText(args[0], JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Details = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"COMPILATION_TRANSACTION_WORKBENCH_OK {checks.Count}");
