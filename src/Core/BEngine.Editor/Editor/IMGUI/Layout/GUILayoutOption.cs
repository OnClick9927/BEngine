namespace BEngine.Editor;

public sealed class GUILayoutOption
{
    internal GUILayoutOption(GUILayoutOptionType type, Fix64 value) { Type = type; Value = value; }
    internal GUILayoutOptionType Type { get; }
    internal Fix64 Value { get; }
}
