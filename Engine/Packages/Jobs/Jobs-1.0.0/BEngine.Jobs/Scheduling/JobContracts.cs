namespace BEngine.Jobs;

public interface IJob
{
    void Execute();
}

public interface IJobParallelFor
{
    void Execute(int index);
}

[AttributeUsage(AttributeTargets.Field)]
public sealed class ReadOnlyAttribute : Attribute;

[AttributeUsage(AttributeTargets.Field)]
public sealed class WriteOnlyAttribute : Attribute;

[AttributeUsage(AttributeTargets.Struct)]
public sealed class BurstCompileAttribute : Attribute;

[Flags]
internal enum JobAccess { None = 0, Read = 1, Write = 2, ReadWrite = Read | Write }

internal static class JobExecutionContext
{
    [ThreadStatic] internal static long CurrentJobId;
}
