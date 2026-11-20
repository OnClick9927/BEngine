using System.Reflection;
using System.Text.Json;
using BEngine.Editor;

var checks = new List<string>();
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks.Add(name); }
Check(!EditorApplication.isCompiling && !CompilationPipeline.isCompiling, "Facade and Core begin idle");
Check(typeof(EditorApplication).GetProperty(nameof(EditorApplication.isCompiling))!.SetMethod is null, "Editor Services cannot mutate compiling state");
Check(typeof(CompilationPipeline).GetProperty(nameof(CompilationPipeline.isCompiling))!.SetMethod is null, "Core exposes read-only compiling state");
Check(typeof(CompilationPipeline).GetMethod("RaiseCompilationStarted", BindingFlags.NonPublic | BindingFlags.Static) is null && typeof(CompilationPipeline).GetMethod("RaiseCompilationFinished", BindingFlags.NonPublic | BindingFlags.Static) is null, "Unpaired transaction notification entry points are removed");
var seen = new List<bool>(); Action<object> callback = _ => seen.Add(EditorApplication.isCompiling);
CompilationPipeline.compilationStarted += callback; CompilationPipeline.compilationFinished += callback;
try
{
    using (CompilationPipeline.BeginCompilation(new object())) Check(EditorApplication.isCompiling && CompilationPipeline.isCompiling, "Facade reads the actual Core transaction without a host");
    Check(seen.SequenceEqual([true, true]), "Facade remains true during both transaction callbacks");
    Check(!EditorApplication.isCompiling && !CompilationPipeline.isCompiling, "Facade clears immediately with the Core transaction");
}
finally { CompilationPipeline.compilationStarted -= callback; CompilationPipeline.compilationFinished -= callback; }
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => new[]{"BEngine.EditorWorkbench.Host", "BEngine.EditorWorkbench.Editor", "BEngine.Application"}.Contains(a.GetName().Name)), "Facade does not require the default workbench or game framework");
File.WriteAllText(args[0], JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Details = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"COMPILATION_TRANSACTION_FACADE_OK {checks.Count}");
