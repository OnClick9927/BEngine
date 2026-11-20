namespace BEngine.Editor;

/// <summary>Runs a host's UI pump while a synchronous editor operation waits for workers.</summary>
public static class EditorExecutionPump
{
    [ThreadStatic] private static Registration? _current;

    /// <summary>Registers an owner-thread pump until the returned scope is disposed.</summary>
    public static IDisposable Register(Action pump)
    {
        ArgumentNullException.ThrowIfNull(pump);
        var registration = new Registration(pump, Environment.CurrentManagedThreadId, _current);
        if (_current is not null) _current.Next = registration;
        _current = registration;
        return registration;
    }

    /// <summary>Pumps only the current thread's presentation, if one was registered.</summary>
    public static void Pump() => _current?.Callback();

    private sealed class Registration(Action callback, int ownerThread, Registration? previous) : IDisposable
    {
        internal Action Callback { get; } = callback;
        internal Registration? Previous { get; set; } = previous;
        internal Registration? Next { get; set; }
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            if (Environment.CurrentManagedThreadId != ownerThread)
                throw new InvalidOperationException("Editor pump registrations must be disposed on their owner thread.");
            _disposed = true;
            if (Previous is not null) Previous.Next = Next;
            if (Next is not null) Next.Previous = Previous;
            if (ReferenceEquals(_current, this)) _current = Previous;
            Previous = null;
            Next = null;
        }
    }
}
