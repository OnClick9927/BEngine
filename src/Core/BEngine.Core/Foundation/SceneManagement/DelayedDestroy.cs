namespace BEngine;

internal static class DelayedDestroy
{
    private static readonly List<(BObject Target, Fix64 Time)> Pending = [];

    static DelayedDestroy() => RuntimeLifecycle.frameEnded += (_, _) => Flush();

    internal static void Schedule(BObject target, Fix64 delay) =>
        Pending.Add((target, Time.time + delay));

    private static void Flush()
    {
        var initialCount = Pending.Count;
        var writeIndex = 0;
        for (var readIndex = 0; readIndex < initialCount; readIndex++)
        {
            var item = Pending[readIndex];
            if (item.Time > Time.time)
            {
                if (writeIndex != readIndex) Pending[writeIndex] = item;
                writeIndex++;
                continue;
            }

            BObject.Destroy(item.Target);
        }
        if (writeIndex < initialCount) Pending.RemoveRange(writeIndex, initialCount - writeIndex);
    }
}
