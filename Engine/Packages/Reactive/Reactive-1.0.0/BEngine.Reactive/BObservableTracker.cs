namespace BEngine.Reactive;

public static class BObservableTracker
{
    private static readonly Dictionary<int, BObservableTrackingState> Active = [];
    private static int _nextId;

    public static bool EnableTracking { get; set; }
    public static bool EnableStackTrace { get; set; }
    public static int ActiveSubscriptionCount => Active.Count;

    public static IReadOnlyList<BObservableTrackingState> GetActiveSubscriptions() =>
        Active.Values.OrderBy(static value => value.TrackingId).ToArray();

    public static void ForEachActiveSubscription(Action<BObservableTrackingState> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        foreach (var state in Active.Values) visitor(state);
    }

    internal static int Add(Type observableType)
    {
        if (!EnableTracking) return 0;
        var id = ++_nextId;
        Active[id] = new BObservableTrackingState(
            id,
            observableType.FullName ?? observableType.Name,
            BTaskScheduler.Main.Elapsed,
            EnableStackTrace
                ? RuntimeServiceAccess.PlatformService.CaptureStackTrace(skipFrames: 2)
                : string.Empty);
        return id;
    }

    internal static void Remove(int id)
    {
        if (id != 0) Active.Remove(id);
    }
}
