using System.Collections.Frozen;

namespace BEngine;

public static class TagManager
{
    private static readonly string[] BuiltInTags =
        ["Untagged", "Respawn", "Finish", "EditorOnly", "MainCamera", "Player", "GameController"];
    private static string[] _tags = CreateDefaultTags();
    private static FrozenSet<string> _tagSet = _tags.ToFrozenSet(StringComparer.Ordinal);
    private static IReadOnlyList<string> _readOnlyTags = Array.AsReadOnly(_tags);
    private static int _version;

    public static int version
    {
        get => _version;
    }

    public static IReadOnlyList<string> tags
    {
        get => _readOnlyTags;
    }

    public static bool IsDefined(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return false;
        return _tagSet.Contains(tag.Trim());
    }

    public static void Configure(IEnumerable<string>? tags)
    {
        var normalized = ((((global::System.Collections.Generic.IEnumerable<string>)((tags ?? []).Where(static tag => !string.IsNullOrWhiteSpace(tag)).Select(static tag => tag.Trim()).Prepend("Untagged"))), (global::System.Collections.Generic.IEqualityComparer<string>? )(StringComparer.Ordinal)) switch
        {
            (string[] { Length: 0 }, _) => global::System.Array.Empty<string>(),
            var (distinctItems908, distinctComparer908) => distinctItems908.ToHashSet<string>(distinctComparer908).ToArray()});
        _tags = normalized;
        _tagSet = normalized.ToFrozenSet(StringComparer.Ordinal);
        _readOnlyTags = Array.AsReadOnly(_tags);
        _version++;
    }

    internal static string[] CreateDefaultTags() => (string[])BuiltInTags.Clone();
}
