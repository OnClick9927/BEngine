using System.Reflection;
using System.Runtime.CompilerServices;

namespace BEngine.Jobs;

public sealed record JobCompilationInfo(
    Type JobType,
    bool BurstRequested,
    bool MethodPrepared,
    string Runtime,
    string Message);

public static class JobCompiler
{
    public static JobCompilationInfo Prepare<TJob>() where TJob : struct => JobCompilationCache<TJob>.Info;

    private static JobCompilationInfo PrepareCore<TJob>() where TJob : struct
    {
        var type = typeof(TJob);
        var method = type.GetMethod(nameof(IJob.Execute), BindingFlags.Instance | BindingFlags.Public,
            binder: null, types: Type.EmptyTypes, modifiers: null) ??
            type.GetMethod(nameof(IJobParallelFor.Execute), BindingFlags.Instance | BindingFlags.Public,
                binder: null, types: [typeof(int)], modifiers: null);
        if (method is null)
            throw new InvalidOperationException($"Job type '{type.FullName}' has no supported Execute method.");
        var requested = type.GetCustomAttribute<BurstCompileAttribute>() is not null;
        try
        {
            RuntimeHelpers.PrepareMethod(method.MethodHandle);
            return new(type, requested, true, System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                "Execute method prepared for optimized generic dispatch.");
        }
        catch (Exception exception) when (exception is PlatformNotSupportedException or InvalidOperationException)
        {
            return new(type, requested, false, System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                "Ahead-of-use preparation is unavailable; optimized generic dispatch remains active: " + exception.Message);
        }
    }

    private static class JobCompilationCache<TJob> where TJob : struct
    {
        internal static readonly JobCompilationInfo Info = PrepareCore<TJob>();
    }
}

internal static class SingleJobDispatch<TJob> where TJob : struct, IJob
{
    internal static readonly JobCompilationInfo Compilation = JobCompiler.Prepare<TJob>();

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    internal static void Execute(ref TJob job) => job.Execute();
}

internal static class ParallelJobDispatch<TJob> where TJob : struct, IJobParallelFor
{
    internal static readonly JobCompilationInfo Compilation = JobCompiler.Prepare<TJob>();

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    internal static void Execute(ref TJob job, int index) => job.Execute(index);
}
