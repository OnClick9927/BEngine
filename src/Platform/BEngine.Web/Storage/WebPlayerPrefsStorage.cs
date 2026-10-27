using System.Runtime.InteropServices.JavaScript;

namespace BEngine.Platform;

/// <summary>Commits PlayerPrefs synchronously to the current browser origin's localStorage.</summary>
internal sealed partial class WebPlayerPrefsStorage : IPlayerPrefsStorage
{
    internal static WebPlayerPrefsStorage Shared { get; } = new();

    public string? Read(string companyName, string productName)
    {
        if (!OperatingSystem.IsBrowser()) throw new PlatformNotSupportedException();
        try { return ReadPreferences(companyName, productName); }
        catch (JSException exception)
        {
            throw new IOException("Could not read Web PlayerPrefs from browser storage. " + exception.Message, exception);
        }
    }

    public void Write(string companyName, string productName, string contents)
    {
        if (!OperatingSystem.IsBrowser()) throw new PlatformNotSupportedException();
        try { WritePreferences(companyName, productName, contents); }
        catch (JSException exception)
        {
            // Never report a successful Save when storage is blocked or its quota is exhausted.
            throw new IOException("Could not save Web PlayerPrefs. Browser storage may be blocked or full. " +
                exception.Message, exception);
        }
    }

    [JSImport("globalThis.BEnginePlayerPrefs.read")]
    private static partial string? ReadPreferences(string companyName, string productName);

    [JSImport("globalThis.BEnginePlayerPrefs.write")]
    private static partial void WritePreferences(string companyName, string productName, string contents);
}
