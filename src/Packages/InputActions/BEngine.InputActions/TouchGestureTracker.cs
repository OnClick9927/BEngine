namespace BEngine.InputActions;

public sealed class TouchGestureSettings
{
    public Fix64 tapDuration { get; set; } = Fix64.FromRaw(1073741824L) /* FromDecimal(.25m) */;
    public Fix64 longPressDuration { get; set; } = Fix64.Half;
    public Fix64 tapDistance { get; set; } = 12;
    public Fix64 swipeDistance { get; set; } = 50;
    public Fix64 swipeDuration { get; set; } = 1;
    public void Validate()
    {
        if (tapDuration <= 0 || longPressDuration <= 0 || tapDistance < 0 || swipeDistance <= tapDistance || swipeDuration <= 0)
            throw new InvalidDataException("Touch timing must be positive and swipe distance must exceed tap tolerance.");
    }
    internal TouchGestureSettings Copy() => (TouchGestureSettings)MemberwiseClone();
}

public enum TouchGestureKind { Tap, LongPress, Swipe }
public readonly record struct TouchGesture(TouchGestureKind kind, int fingerId, Vector2 position, Vector2 displacement, Fix64 duration);

/// <summary>Deterministic gesture state for one input consumer. Update once per input frame using unscaled time.</summary>
public sealed class TouchGestureTracker
{
    private readonly TouchGestureSettings _settings;
    private readonly Dictionary<int, Contact> _contacts = [];
    private readonly List<TouchGesture> _gestures = [];
    private readonly IReadOnlyList<TouchGesture> _gestureView;
    private readonly List<Touch> _touches = [];
    private readonly IReadOnlyList<Touch> _touchView;
    private ObjectPool<TouchWorkspace>? _workspaces;
    private (int A, int B)? _pair;
    private Vector2 _pairVector, _pairCenter;
    private int _primary = -1;
    public IReadOnlyList<TouchGesture> gestures => _gestureView;
    public IReadOnlyList<Touch> touches => _touchView;
    public int activeTouchCount => _contacts.Count;
    public int primaryFingerId => _primary;
    public Vector2 panDelta { get; private set; }
    public Fix64 pinchDelta { get; private set; }
    public Fix64 pinchScaleDelta { get; private set; }
    public Fix64 rotationDelta { get; private set; }

    public TouchGestureTracker(TouchGestureSettings? settings = null)
    {
        _settings = (settings ?? new()).Copy(); _settings.Validate();
        _gestureView = _gestures.AsReadOnly(); _touchView = _touches.AsReadOnly();
    }
    public void Reset()
    {
        _contacts.Clear(); _gestures.Clear(); _touches.Clear(); _primary = -1; _pair = null;
        panDelta = Vector2.zero; pinchDelta = pinchScaleDelta = rotationDelta = 0;
    }
    public Touch? GetTouch(int fingerId = -1)
    {
        var id = fingerId < 0 ? _primary : fingerId;
        foreach (var touch in _touches) if (touch.fingerId == id) return touch;
        return null;
    }

    public void Update(IEnumerable<Touch> touches, Fix64 deltaTime)
    {
        ArgumentNullException.ThrowIfNull(touches);
        if (deltaTime < 0) throw new ArgumentOutOfRangeException(nameof(deltaTime));
        var pool = _workspaces ??= new ObjectPool<TouchWorkspace>(static () => new(),
            onRelease: static workspace => workspace.Reset(), maxSize: 2);
        var workspace = pool.Get();
        try
        {
            if (touches is Touch[] array)
            {
                workspace.Count = Math.Min(array.Length, workspace.Snapshot.Length);
                Array.Copy(array, workspace.Snapshot, workspace.Count);
            }
            else
            {
                foreach (var touch in touches)
                {
                    workspace.Snapshot[workspace.Count++] = touch;
                    if (workspace.Count == workspace.Snapshot.Length) break;
                }
            }
            if (workspace.Count > 128) throw new ArgumentException("Invalid touch snapshot.", nameof(touches));
            var activeCount = 0;
            var ordered = true;
            for (var index = 0; index < workspace.Count; index++)
            {
                var touch = workspace.Snapshot[index];
                if (touch.fingerId < 0 || !Enum.IsDefined(touch.phase) || !workspace.Present.Add(touch.fingerId))
                    throw new ArgumentException("Invalid touch snapshot.", nameof(touches));
                if (touch.phase is not (TouchPhase.Ended or TouchPhase.Canceled)) activeCount++;
                if (index > 0 && workspace.Snapshot[index - 1].fingerId > touch.fingerId) ordered = false;
            }
            if (!ordered) Array.Sort(workspace.Snapshot, 0, workspace.Count, TouchIdComparer.Instance);
            ApplySnapshot(workspace, activeCount, deltaTime);
        }
        finally { pool.Release(workspace); }
    }

    private void ApplySnapshot(TouchWorkspace workspace, int activeCount, Fix64 deltaTime)
    {
        // Validate before changing the previous frame, so rejected input cannot cancel a live gesture.
        _gestures.Clear(); _touches.Clear(); panDelta = Vector2.zero; pinchDelta = pinchScaleDelta = rotationDelta = 0;
        foreach (var id in _contacts.Keys) if (!workspace.Present.Contains(id)) workspace.Removed.Add(id);
        foreach (var id in workspace.Removed) _contacts.Remove(id);
        for (var index = 0; index < workspace.Count; index++)
        {
            var touch = workspace.Snapshot[index];
            var existing = _contacts.TryGetValue(touch.fingerId, out var contact);
            if (!existing || touch.phase == TouchPhase.Began && contact!.LastPhase != TouchPhase.Began)
            {
                contact = new Contact { Origin = touch.position, Position = touch.position, Eligible = touch.phase == TouchPhase.Began };
                if (touch.phase is TouchPhase.Ended or TouchPhase.Canceled) { _touches.Add(touch with { deltaPosition = Vector2.zero }); continue; }
                _contacts[touch.fingerId] = contact;
            }
            else contact!.Age += deltaTime;
            var delta = touch.position - contact!.Position;
            contact.Position = touch.position; contact.LastPhase = touch.phase;
            contact.MaximumDistance = Fix64.Max(contact.MaximumDistance, Vector2.Distance(contact.Origin, touch.position));
            contact.WasMultiple |= activeCount > 1;
            _touches.Add(touch with { deltaPosition = delta });
            if (touch.phase is TouchPhase.Ended or TouchPhase.Canceled)
            {
                if (touch.phase == TouchPhase.Ended && contact.Eligible && !contact.WasMultiple && !contact.Held)
                {
                    if (contact.Age <= _settings.tapDuration && contact.MaximumDistance <= _settings.tapDistance)
                        _gestures.Add(new(TouchGestureKind.Tap, touch.fingerId, touch.position, touch.position - contact.Origin, contact.Age));
                    else if (contact.Age <= _settings.swipeDuration && Vector2.Distance(contact.Origin, touch.position) >= _settings.swipeDistance)
                        _gestures.Add(new(TouchGestureKind.Swipe, touch.fingerId, touch.position, touch.position - contact.Origin, contact.Age));
                }
                _contacts.Remove(touch.fingerId);
            }
            else if (contact.Eligible && !contact.WasMultiple && !contact.Held && contact.Age >= _settings.longPressDuration && contact.MaximumDistance <= _settings.tapDistance)
            {
                contact.Held = true;
                _gestures.Add(new(TouchGestureKind.LongPress, touch.fingerId, touch.position, touch.position - contact.Origin, contact.Age));
            }
        }
        var first = -1; var second = -1;
        foreach (var id in _contacts.Keys)
        {
            if (first < 0 || id < first) { second = first; first = id; }
            else if (second < 0 || id < second) second = id;
        }
        if (!_contacts.ContainsKey(_primary)) _primary = first;
        if (second >= 0)
        {
            var pair = (first, second);
            var a = _contacts[pair.Item1].Position; var b = _contacts[pair.Item2].Position;
            var vector = b - a; var center = (a + b) / 2;
            if (_pair == pair && vector.sqrMagnitude > 0 && _pairVector.sqrMagnitude > 0)
            {
                panDelta = center - _pairCenter;
                pinchDelta = vector.magnitude - _pairVector.magnitude;
                pinchScaleDelta = vector.magnitude / _pairVector.magnitude - 1;
                rotationDelta = Vector2.Angle(_pairVector, vector);
            }
            _pair = pair; _pairVector = vector; _pairCenter = center;
        }
        else
        {
            _pair = null;
            if (GetTouch() is { } primary) panDelta = primary.deltaPosition;
        }
    }

    // Each enumeration owns its scratch until commit, including reentrant input enumerators.
    private sealed class TouchWorkspace
    {
        internal readonly Touch[] Snapshot = new Touch[129];
        internal readonly HashSet<int> Present = [];
        internal readonly List<int> Removed = [];
        internal int Count;
        internal void Reset() { Count = 0; Present.Clear(); Removed.Clear(); }
    }

    private sealed class TouchIdComparer : IComparer<Touch>
    {
        internal static readonly TouchIdComparer Instance = new();
        public int Compare(Touch a, Touch b) => a.fingerId.CompareTo(b.fingerId);
    }

    private sealed class Contact
    {
        internal Vector2 Origin, Position;
        internal Fix64 Age, MaximumDistance;
        internal TouchPhase LastPhase;
        internal bool Eligible, WasMultiple, Held;
    }
}
