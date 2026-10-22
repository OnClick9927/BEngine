using System.Reflection;

namespace BEngine.Jobs;

internal interface INativeContainer
{
    INativeContainerState? ContainerState { get; }
}

internal interface INativeContainerState
{
    void Reserve(long jobId, JobAccess access, IReadOnlySet<long> dependencies);
    void Release(long jobId);
    void CheckRead();
    void CheckWrite();
    void DisposeChecked();
}

internal readonly record struct JobContainerAccess(INativeContainerState State, JobAccess Access);

internal static class JobContainerSafety
{
    internal static JobContainerAccess[] Collect<TJob>(TJob job) where TJob : struct
    {
        var result = new Dictionary<INativeContainerState, JobAccess>(ReferenceEqualityComparer.Instance);
        foreach (var field in typeof(TJob).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!typeof(INativeContainer).IsAssignableFrom(field.FieldType)) continue;
            if (field.GetCustomAttribute<ReadOnlyAttribute>() is not null &&
                field.GetCustomAttribute<WriteOnlyAttribute>() is not null)
                throw new InvalidOperationException($"Job field '{field.Name}' cannot be both ReadOnly and WriteOnly.");
            if (field.GetValue(job) is not INativeContainer { ContainerState: { } state }) continue;
            var access = field.GetCustomAttribute<ReadOnlyAttribute>() is not null ? JobAccess.Read :
                field.GetCustomAttribute<WriteOnlyAttribute>() is not null ? JobAccess.Write : JobAccess.ReadWrite;
            result[state] = result.GetValueOrDefault(state) | access;
        }
        return result.Select(pair => new JobContainerAccess(pair.Key, pair.Value)).ToArray();
    }

    internal static void Reserve(IEnumerable<JobContainerAccess> accesses, long jobId, IReadOnlySet<long> dependencies)
    {
        var acquired = new List<INativeContainerState>();
        try
        {
            foreach (var access in accesses)
            {
                access.State.Reserve(jobId, access.Access, dependencies);
                acquired.Add(access.State);
            }
        }
        catch
        {
            foreach (var state in acquired) state.Release(jobId);
            throw;
        }
    }

    internal static void Release(IEnumerable<JobContainerAccess> accesses, long jobId)
    {
        foreach (var access in accesses) access.State.Release(jobId);
    }
}
