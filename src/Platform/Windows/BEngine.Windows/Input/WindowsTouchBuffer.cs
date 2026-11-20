namespace BEngine.Platform;

internal readonly record struct WindowsTouchKey(IntPtr Device, uint Id);
internal readonly record struct WindowsTouchEvent(WindowsTouchKey Key, Vector2 Position, TouchPhase Phase, Vector2 Radius = default);

/// <summary>Queues native reports without losing down/up pairs between game frames.</summary>
internal sealed class WindowsTouchBuffer
{
    private readonly Queue<WindowsTouchEvent[]> _pending = [];
    private readonly Dictionary<WindowsTouchKey, Contact> _contacts = [];
    private bool _cancel;
    internal void Enqueue(WindowsTouchEvent[] frame)
    {
        if (frame.Length == 0) return;
        if (frame.Length > 128 || _pending.Count >= 256) { Cancel(); return; }
        // The native callback supplies an owned array; later reports cannot change this frame.
        _pending.Enqueue(frame);
    }
    internal void Cancel() { _pending.Clear(); _cancel = true; }
    internal void Clear() { _pending.Clear(); _contacts.Clear(); _cancel = false; }
    internal Touch[] Poll(int width, int height, Fix64 deltaTime)
    {
        if (width <= 0 || height <= 0 || deltaTime < 0) throw new ArgumentOutOfRangeException(nameof(width));
        // Still publish a terminal contact for one frame; only a genuinely idle buffer can skip work.
        if (_contacts.Count == 0 && _pending.Count == 0)
        {
            _cancel = false;
            return Array.Empty<Touch>();
        }
        return PollActive(width, height, deltaTime);
    }

    private Touch[] PollActive(int width, int height, Fix64 deltaTime)
    {
        // Dictionary.Remove does not invalidate enumeration on the supported .NET runtime.
        foreach (var pair in _contacts)
            if (pair.Value.Phase is TouchPhase.Ended or TouchPhase.Canceled) _contacts.Remove(pair.Key);
        foreach (var contact in _contacts.Values)
        { contact.Start = contact.Position; contact.Phase = TouchPhase.Stationary; }
        if (_cancel)
        {
            foreach (var contact in _contacts.Values) contact.Phase = TouchPhase.Canceled;
            _cancel = false;
        }
        else if (_pending.Count != 0)
        {
            var transitions = new Dictionary<WindowsTouchKey, TouchPhase>();
            while (_pending.TryPeek(out var frame))
            {
                // A contact gets at least one game frame for each begin/end transition.
                if (Query2323())
                    break;
                [global::BEngine.SkipLoopGuard]
                bool Query2323()
                {
                    var querySource2323 = frame;
                    ArgumentNullException.ThrowIfNull(querySource2323, "source");
                    if (querySource2323.GetType() == typeof(global::BEngine.Platform.WindowsTouchEvent[]) || querySource2323.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Platform.WindowsTouchEvent>))
                    {
                        global::System.ReadOnlySpan<global::BEngine.Platform.WindowsTouchEvent> queryValues2323 = querySource2323.GetType() == typeof(global::BEngine.Platform.WindowsTouchEvent[]) ? (global::BEngine.Platform.WindowsTouchEvent[])(object)querySource2323 : global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Platform.WindowsTouchEvent>)(object)querySource2323);
                        foreach (var item in queryValues2323)
                        {
                            if (transitions.TryGetValue(item.Key, out var previous) && (item.Phase == TouchPhase.Began || previous == TouchPhase.Began && (item.Phase == TouchPhase.Ended || _contacts[item.Key].Position != item.Position)))
                                return true;
                        }
                    }
                    else
                    {
                        foreach (var item in querySource2323)
                        {
                            if (transitions.TryGetValue(item.Key, out var previous) && (item.Phase == TouchPhase.Began || previous == TouchPhase.Began && (item.Phase == TouchPhase.Ended || _contacts[item.Key].Position != item.Position)))
                                return true;
                        }
                    }
                
                    return false;
                }
                _pending.Dequeue();
                foreach (var item in frame)
                {
                    if (!_contacts.TryGetValue(item.Key, out var contact))
                    {
                        if (item.Phase != TouchPhase.Began || _contacts.Count >= 128) continue;
                        var id = FindAvailableId();
                        contact = new() { Id = id, Start = item.Position };
                        _contacts.Add(item.Key, contact);
                    }
                    if (contact.Phase is TouchPhase.Canceled or TouchPhase.Ended) continue;
                    contact.Position = item.Position; contact.Radius = item.Radius;
                    if (item.Phase != TouchPhase.Moved || contact.Phase != TouchPhase.Began) contact.Phase = item.Phase;
                    if (item.Phase is TouchPhase.Began or TouchPhase.Ended) transitions[item.Key] = item.Phase;
                }
            }
        }
        if (_contacts.Count == 0) return Array.Empty<Touch>();
        var touches = new Touch[_contacts.Count];
        var output = 0;
        foreach (var contact in _contacts.Values)
            touches[output++] = new Touch(contact.Id,
                new(contact.Position.x * width, contact.Position.y * height),
                contact.Phase == TouchPhase.Began ? Vector2.zero : new((contact.Position.x - contact.Start.x) * width, (contact.Position.y - contact.Start.y) * height),
                contact.Phase == TouchPhase.Began ? 0 : deltaTime, 1, contact.Phase,
                contact.Phase is TouchPhase.Ended or TouchPhase.Canceled ? 0 : 1, 1,
                (contact.Radius.x * width + contact.Radius.y * height) / 2);
        // Finger IDs are unique. Sorting the owned result needs no key/index arrays.
        Array.Sort(touches, static (left, right) => left.fingerId.CompareTo(right.fingerId));
        return touches;

        int FindAvailableId()
        {
            Span<bool> occupied = stackalloc bool[128];
            occupied.Clear();
            foreach (var contact in _contacts.Values) occupied[contact.Id] = true;
            var id = 0;
            while (occupied[id]) id++;
            return id;
        }
    }
    private sealed class Contact
    {
        internal int Id;
        internal Vector2 Start, Position, Radius;
        internal TouchPhase Phase;
    }
}
