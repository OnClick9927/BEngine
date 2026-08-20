namespace BEngine.Editor;

internal static class EditorLogStore
{
    private const int MaximumEntries = 20_000;
    private static readonly object Gate = new();
    private static readonly List<LogEntry> Entries = [];
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
        lock (Gate) return Entries.ToArray();
    }

    internal static LogEntry[] SnapshotWithVersion(out long snapshotVersion)
    {
        lock (Gate)
        {
            snapshotVersion = _version;
            return Entries.ToArray();
        }
    }

    internal static void Clear()
    {
        lock (Gate)
        {
            Entries.Clear();
            _version++;
            _clearVersion++;
        }
    }

    private static void Add(LogEntry entry)
    {
        lock (Gate)
        {
            Entries.Add(entry);
            if (Entries.Count > MaximumEntries)
                Entries.RemoveRange(0, Entries.Count - MaximumEntries);
            _version++;
        }
    }
}
