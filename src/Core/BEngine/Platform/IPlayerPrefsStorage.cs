namespace BEngine.Platform;

/// <summary>Optional durable storage for PlayerPrefs on platforms without a persistent filesystem.</summary>
public interface IPlayerPrefsStorage
{
    /// <summary>Returns the serialized preferences, or null when none have been saved.</summary>
    string? Read(string companyName, string productName);

    /// <summary>Atomically commits the complete serialized preferences before returning.</summary>
    void Write(string companyName, string productName, string contents);
}
