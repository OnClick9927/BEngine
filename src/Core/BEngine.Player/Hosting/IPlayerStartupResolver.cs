namespace BEngine.Player;

public interface IPlayerStartupResolver
{
    string BaseDirectory { get; }
    string? PersistentDataPath { get; }
    PlayerStartupDescriptor Resolve(IReadOnlyList<string> arguments);
    string ResolveDefaultProjectPath(string? playerDirectory = null);
}
