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
            Console.WriteLine(
                "THREADING_ARCHITECTURE_OK|editor-concurrency,priority,cancellation,fault-isolation," +
                "main-thread-continuation,dispose-race," +
                "asset-prepare-apply,asset-cancel," +
                "shader-compile-apply,shader-delete,shader-cancel,script-build-apply," +
                "compilation-event-order");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"THREADING_ARCHITECTURE_FAILED|{exception}");
            return 1;
        }
    }
}
