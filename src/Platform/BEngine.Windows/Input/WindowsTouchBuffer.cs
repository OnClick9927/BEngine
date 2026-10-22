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
        foreach (var key in _contacts.Where(pair => pair.Value.Phase is TouchPhase.Ended or TouchPhase.Canceled).Select(pair => pair.Key).ToArray())
            _contacts.Remove(key);
        foreach (var contact in _contacts.Values)
        { contact.Start = contact.Position; contact.Phase = TouchPhase.Stationary; }
        if (_cancel)
        {
            foreach (var contact in _contacts.Values) contact.Phase = TouchPhase.Canceled;
            _cancel = false;
        }
        else
        {
            var transitions = new Dictionary<WindowsTouchKey, TouchPhase>();
            while (_pending.TryPeek(out var frame))
            {
                // A contact gets at least one game frame for each begin/end transition.
                if (frame.Any(item => transitions.TryGetValue(item.Key, out var previous) &&
                    (item.Phase == TouchPhase.Began || previous == TouchPhase.Began &&
                        (item.Phase == TouchPhase.Ended || _contacts[item.Key].Position != item.Position)))) break;
                _pending.Dequeue();
                foreach (var item in frame)
                {
                    if (!_contacts.TryGetValue(item.Key, out var contact))
                    {
                        if (item.Phase != TouchPhase.Began || _contacts.Count >= 128) continue;
                        var occupied = _contacts.Values.Select(value => value.Id).ToHashSet();
                        var id = 0; while (occupied.Contains(id)) id++;
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
        return _contacts.Values.OrderBy(contact => contact.Id).Select(contact => new Touch(contact.Id,
            new(contact.Position.x * width, contact.Position.y * height),
            contact.Phase == TouchPhase.Began ? Vector2.zero : new((contact.Position.x - contact.Start.x) * width, (contact.Position.y - contact.Start.y) * height),
            contact.Phase == TouchPhase.Began ? 0 : deltaTime, 1, contact.Phase,
            contact.Phase is TouchPhase.Ended or TouchPhase.Canceled ? 0 : 1, 1,
            (contact.Radius.x * width + contact.Radius.y * height) / 2)).ToArray();
    }
    private sealed class Contact
    {
        internal int Id;
        internal Vector2 Start, Position, Radius;
        internal TouchPhase Phase;
    }
}
