namespace BEngine;

public sealed class BCancellationSource : IDisposable
{
    private bool _isCancellationRequested;
    private bool _disposed;
    private RegistrationNode? _first;
    private RegistrationNode? _last;

    public BCancellationToken Token => new(this);
    public bool IsCancellationRequested => _isCancellationRequested;

    public bool Cancel()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_isCancellationRequested) return false;
        _isCancellationRequested = true;
        // Callbacks can remove any remaining registration, including the next node.
        var pending = new List<RegistrationNode>();
        var current = _first;
        while (current is not null)
        {
            pending.Add(current);
            current = current.Next;
        }
        _first = null;
        _last = null;
        foreach (var registration in pending)
        {
            registration.DetachOwner();
            registration.Invoke();
        }
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var current = _first;
        _first = null;
        _last = null;
        while (current is not null)
        {
            var next = current.Next;
            current.Detach();
            current = next;
        }
    }

    internal BCancellationRegistration Register(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_isCancellationRequested)
        {
            callback();
            return default;
        }
        var node = new RegistrationNode(this, callback);
        if (_last is null) _first = node;
        else
        {
            _last.Next = node;
            node.Previous = _last;
        }
        _last = node;
        return new BCancellationRegistration(node);
    }

    private void Remove(RegistrationNode node)
    {
        if (node.Previous is { } previous) previous.Next = node.Next;
        else if (ReferenceEquals(_first, node)) _first = node.Next;
        if (node.Next is { } next) next.Previous = node.Previous;
        else if (ReferenceEquals(_last, node)) _last = node.Previous;
        node.Detach();
    }

    internal sealed class RegistrationNode(BCancellationSource owner, Action callback) : IDisposable
    {
        private BCancellationSource? _owner = owner;
        private Action? _callback = callback;
        internal RegistrationNode? Previous { get; set; }
        internal RegistrationNode? Next { get; set; }

        public void Dispose() => _owner?.Remove(this);

        internal void Invoke()
        {
            var action = _callback;
            _callback = null;
            if (action is null) return;
            try { action(); }
            catch (Exception exception)
            {
                RuntimeServiceAccess.PlatformService.WriteDiagnostic(
                    $"Unhandled BTask cancellation callback exception: {exception}");
            }
        }

        internal void Detach()
        {
            DetachOwner();
            _callback = null;
        }

        internal void DetachOwner()
        {
            _owner = null;
            Previous = null;
            Next = null;
        }
    }
}
