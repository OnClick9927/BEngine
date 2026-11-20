namespace BEngine.Editor;

/// <summary>Owns a native docking target overlay. All operations run on its window thread.</summary>
public interface IEditorDockPreview : IDisposable
{
    void ShowPreview(Rect screenRect);
    void HidePreview();
}

/// <summary>Owns a native menu attached to an editor window.</summary>
public interface IEditorNativeMainMenu : IDisposable
{
    bool SynchronizeRoots(IReadOnlyList<string> roots);
}
