namespace BEngine.Platform;

/// <summary>Locates optional installed host tools without starting or installing them.</summary>
public interface IEditorHostTools
{
    string? FindChromiumBrowser();
    string? FindAndroidDebugBridge();
}
