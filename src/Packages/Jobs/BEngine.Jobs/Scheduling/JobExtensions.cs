namespace BEngine.Jobs;

public static class JobExtensions
{
    public static JobHandle Schedule<TJob>(this TJob job, JobHandle dependency = default)
        where TJob : struct, IJob => JobSystem.Schedule(job, dependency);

    public static JobHandle Schedule<TJob>(this TJob job, int arrayLength, int batchSize = 64,
        JobHandle dependency = default) where TJob : struct, IJobParallelFor =>
        JobSystem.ScheduleParallel(job, arrayLength, batchSize, dependency);
}
