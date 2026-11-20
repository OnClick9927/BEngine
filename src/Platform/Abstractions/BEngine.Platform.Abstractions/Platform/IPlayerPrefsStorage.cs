namespace BEngine.Platform;

public readonly record struct PlayerPrefsScope(string CompanyName, string ProductName, string PersistentDataPath);

/// <summary>Platform-owned durable storage. The scope contains identity, never a storage file convention.</summary>
public interface IPlayerPrefsStorage
{
    /// <summary>A stable identity for the current storage backend, including any replaced filesystem.</summary>
    object Identity { get; }
    /// <summary>Returns the serialized preferences, or null when none have been saved.</summary>
    string? Read(PlayerPrefsScope scope);

    /// <summary>Atomically commits the complete serialized preferences before returning.</summary>
    void Write(PlayerPrefsScope scope, string contents);
}
