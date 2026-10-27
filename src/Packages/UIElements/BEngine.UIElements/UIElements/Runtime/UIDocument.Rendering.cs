namespace BEngine.UIElements;

public sealed partial class UIDocument
{
    /// <summary>
    /// Skips creation of this document's render submissions when the collecting
    /// camera excludes its sorting layer. Disabled by default.
    /// </summary>
    /// <remarks>
    /// Applies only to the exact built-in List&lt;RenderSubmission2D&gt; collection.
    /// Layout, styles, resource resolution and collection factories still run;
    /// excluded commands still consume their original submission-order numbers.
    /// Per-command material/atlas preparation, payload copies and Add calls are
    /// omitted. Later render contributors therefore do not observe the excluded
    /// submissions in the shared collection. Enable only when the application's
    /// contributor pipeline does not depend on those excluded submissions or on
    /// their material preparation. Unknown and derived collection types retain
    /// the complete submission path, including their Add callbacks.
    /// </remarks>
    public bool cullExcludedCameraSubmissions { get; set; }
}
