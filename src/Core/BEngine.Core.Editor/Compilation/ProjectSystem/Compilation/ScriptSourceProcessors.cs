using System.Reflection;

namespace BEngine.ProjectSystem;

/// <summary>An optional editor extension that transforms project compiler inputs.</summary>
public interface IScriptSourceProcessor
{
    string Revision { get; }
    string Transform(string source, string displayPath, IReadOnlyList<string> preprocessorSymbols);
}

/// <summary>Owns compiler extensions independently of any particular package.</summary>
public static class ScriptSourceProcessors
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<Type, IScriptSourceProcessor> Processors = [];

    public static void Register(IScriptSourceProcessor processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentException.ThrowIfNullOrWhiteSpace(processor.Revision);
        lock (Gate) Processors[processor.GetType()] = processor;
    }

    public static void UnregisterAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        lock (Gate)
            foreach (var type in Processors.Keys.Where(type => type.Assembly == assembly).ToArray())
                Processors.Remove(type);
    }

    internal static IScriptSourceProcessor[] Snapshot()
    {
        lock (Gate) return Processors.OrderBy(item => item.Key.FullName, StringComparer.Ordinal)
            .ThenBy(item => item.Key.Assembly.GetName().Name, StringComparer.Ordinal)
            .Select(item => item.Value).ToArray();
    }
}
