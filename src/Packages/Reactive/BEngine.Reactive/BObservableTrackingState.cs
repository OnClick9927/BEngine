namespace BEngine.Reactive;

public readonly record struct BObservableTrackingState(
    int TrackingId,
    string ObservableType,
    TimeSpan AddedAt,
    string StackTrace);
