namespace BEngine.VisualScripting;

/// <summary>Explicit registration is retained by AOT builds; no runtime code generation or reflection invocation.</summary>
public static class VisualScriptActions
{
    private static readonly Dictionary<string, Func<VisualScriptRunner, VisualScriptNode, string?>> Actions = new(StringComparer.Ordinal);
    private static readonly object Gate = new();
    public static void Register(string id, Func<VisualScriptRunner, VisualScriptNode, string?> action)
    { ArgumentException.ThrowIfNullOrWhiteSpace(id); ArgumentNullException.ThrowIfNull(action); lock (Gate) { if (!Actions.TryAdd(id, action)) throw new InvalidOperationException($"Visual action '{id}' is already registered."); } }
    public static bool Unregister(string id) { lock (Gate) return Actions.Remove(id); }
    public static IReadOnlyList<string> RegisteredIds { get { lock (Gate) return Actions.Keys.Order(StringComparer.Ordinal).ToArray(); } }
    internal static string? Execute(string id, VisualScriptRunner runner, VisualScriptNode node)
    {
        Func<VisualScriptRunner, VisualScriptNode, string?> action;
        lock (Gate) action = Actions.GetValueOrDefault(id) ?? throw new InvalidOperationException($"Visual action '{id}' is not registered.");
        return action(runner, node);
    }
}
