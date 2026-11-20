namespace BEngine.Editor;

/// <summary>UI requests emitted by editor mechanisms and handled by the active workbench.</summary>
public static class EditorPresentationRequests
{
    public static event Action<Rect, string, MenuCommand>? PopupMenuRequested;
    public static event Action<BObject>? PingRequested;
    public static event Action? PrefabsRefreshed;
    public static void ShowPopup(Rect position, string path, MenuCommand command) => PopupMenuRequested?.Invoke(position,path,command);
    public static void Ping(BObject value) => PingRequested?.Invoke(value);
    public static void NotifyPrefabsRefreshed() => PrefabsRefreshed?.Invoke();
}

public class EditorCallbackExitException : Exception;

/// <summary>Allocation-free entry/exit contract for optional UI state isolation.</summary>
public interface IEditorFeatureIsolation
{
    int Begin();
    void Restore(int token, bool succeeded);
}

public static class EditorFeatureIsolation
{
    public static IEditorFeatureIsolation? Current { get; set; }
}
