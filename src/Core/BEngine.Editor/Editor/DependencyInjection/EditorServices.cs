namespace BEngine.Editor;

/// <summary>
/// Provides package editor extensions with read-only access to the editor composition root.
/// Service-provider attachment and lifetime remain owned by the editor host.
/// </summary>
public static class EditorServices
{
    public static T GetRequiredService<T>() where T : notnull =>
        EditorServiceAccess.GetRequiredService<T>();
}
