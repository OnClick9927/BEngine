namespace BEngine.ExampleTests.ThreadingArchitecture;

internal static class Program
{
    private static async Task<int> Main()
    {
        try
        {
            await EditorTaskSchedulerTests.RunAsync().ConfigureAwait(false);
            AssetRefreshPipelineTests.Run();
            ShaderCompilationPipelineTests.Run();
            ScriptBuildApplyPipelineTests.Run();
            RuntimeThreadingTests.Run();
            Console.WriteLine(
                "THREADING_ARCHITECTURE_OK|editor-concurrency,priority,cancellation,fault-isolation," +
                "main-thread-continuation,dispose-race,worker-dispose-rejection," +
                "asset-prepare-apply,asset-cancel," +
                "shader-compile-apply,shader-delete,shader-cancel,script-build-apply," +
                "compilation-event-order,runtime-main-thread,bobject,scene,world,render,frame-isolation");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"THREADING_ARCHITECTURE_FAILED|{exception}");
            return 1;
        }
    }
}
