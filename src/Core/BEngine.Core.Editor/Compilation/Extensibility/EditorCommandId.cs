namespace BEngine.Editor;

/// <summary>A stable, provider-owned command identity. Core does not enumerate workbench actions.</summary>
public readonly record struct EditorCommandId(string Value)
{
    public static EditorCommandId From<T>(T value) where T : struct, Enum => Ids<T>.Values[value];
    private static class Ids<T> where T : struct, Enum
    {
        internal static readonly Dictionary<T, EditorCommandId> Values = Enum.GetValues<T>()
            .Distinct().ToDictionary(value => value, value => new EditorCommandId(typeof(T).FullName + "." + value));
    }
}
