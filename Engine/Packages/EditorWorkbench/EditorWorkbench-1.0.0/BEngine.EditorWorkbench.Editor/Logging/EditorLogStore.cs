namespace BEngine.Editor;

internal static class EditorLogStore
{
    private const int MaximumEntries = 20_000;
    private static readonly Lock Gate = new();
    private static readonly List<LogEntry> Entries = [];
    private static int _oldestEntry;
    private static long _version;
    private static long _clearVersion;

    static EditorLogStore() => Debug.MessageLogged += Add;

    internal static long version
    {
        get { lock (Gate) return _version; }
    }

    internal static long clearVersion
    {
        get { lock (Gate) return _clearVersion; }
    }

    internal static void Initialize() { }

    internal static LogEntry[] Snapshot()
    {
        lock (Gate) return CreateSnapshot();
    }

    internal static LogEntry[] SnapshotWithVersion(out long snapshotVersion)
    {
        lock (Gate)
        {
            snapshotVersion = _version;
            return CreateSnapshot();
        }
    }

    internal static void Clear()
    {
        lock (Gate)
        {
            Entries.Clear();
            _oldestEntry = 0;
            _version++;
            _clearVersion++;
        }
    }

    private static void Add(LogEntry entry)
    {
        lock (Gate)
        {
            if (Entries.Count < MaximumEntries)
            {
                // Keep the final backing-array growth within the retention limit.
                if (Entries.Count == Entries.Capacity && Entries.Capacity > MaximumEntries / 2)
                    Entries.Capacity = MaximumEntries;
                Entries.Add(entry);
            }
            else
            {
                Entries[_oldestEntry] = entry;
                if (++_oldestEntry == MaximumEntries) _oldestEntry = 0;
            }
            _version++;
        }
    }

    private static LogEntry[] CreateSnapshot()
    {
        if (_oldestEntry == 0) return Entries.ToArray();
        var snapshot = new LogEntry[Entries.Count];
        var tailCount = Entries.Count - _oldestEntry;
        Entries.CopyTo(_oldestEntry, snapshot, 0, tailCount);
        Entries.CopyTo(0, snapshot, tailCount, _oldestEntry);
        return snapshot;
    }
}
