namespace BEngine;

internal static class DelayedDestroy
{
    private static readonly List<(BObject Target, Fix64 Time)> Pending = [];

    static DelayedDestroy() => RuntimeLifecycle.frameEnded += (_, _) => Flush();

    internal static void Schedule(BObject target, Fix64 delay) =>
        Pending.Add((target, Time.time + delay));

    private static void Flush()
    {
        foreach (var item in Pending.Where(item => item.Time <= Time.time).ToArray())
        {
            BObject.Destroy(item.Target);
            Pending.Remove(item);
        }
    }
}
