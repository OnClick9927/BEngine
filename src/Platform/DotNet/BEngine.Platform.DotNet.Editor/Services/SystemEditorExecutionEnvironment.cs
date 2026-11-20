using System.Diagnostics;

namespace BEngine.Editor;

public sealed class SystemEditorExecutionEnvironment : IEditorExecutionEnvironment
{
    public int CurrentManagedThreadId => Environment.CurrentManagedThreadId;
    public string? CurrentManagedThreadName => Thread.CurrentThread.Name;
    public int ProcessorCount => Environment.ProcessorCount;
    public object? GetRuntimeData(string name) => AppContext.GetData(name);

    public EditorProcessMemory ReadCurrentProcessMemory()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            return new EditorProcessMemory(
                Math.Max(0, process.WorkingSet64),
                Math.Max(0, process.PrivateMemorySize64));
        }
        catch (InvalidOperationException) { }
        catch (NotSupportedException) { }
        catch (System.ComponentModel.Win32Exception) { }
        return default;
    }
}
