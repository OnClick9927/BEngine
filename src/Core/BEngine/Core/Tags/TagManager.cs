namespace BEngine;

public static class TagManager
{
    private static readonly string[] BuiltInTags =
        ["Untagged", "Respawn", "Finish", "EditorOnly", "MainCamera", "Player", "GameController"];
    private static readonly object Gate = new();
    private static string[] _tags = CreateDefaultTags();
    private static IReadOnlyList<string> _readOnlyTags = Array.AsReadOnly(_tags);
    private static int _version;

    public static int version
    {
        get
        {
            lock (Gate) return _version;
        }
    }

    public static IReadOnlyList<string> tags
    {
        get
        {
            lock (Gate) return _readOnlyTags;
        }
    }

    public static bool IsDefined(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return false;
        lock (Gate) return _tags.Contains(tag.Trim(), StringComparer.Ordinal);
    }

    public static void Configure(IEnumerable<string>? tags)
    {
        var normalized = (tags ?? [])
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Select(static tag => tag.Trim())
            .Prepend("Untagged")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        lock (Gate)
        {
            _tags = normalized;
            _readOnlyTags = Array.AsReadOnly(_tags);
            _version++;
        }
    }

    internal static string[] CreateDefaultTags() => (string[])BuiltInTags.Clone();
}
