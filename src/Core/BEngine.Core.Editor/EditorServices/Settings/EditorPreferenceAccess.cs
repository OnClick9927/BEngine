namespace BEngine.Editor;
public static class EditorPreferenceAccess
{
    public static Func<string>? ExternalCodeEditorProvider { get; set; }
    public static string ExternalCodeEditor => ExternalCodeEditorProvider?.Invoke() ?? string.Empty;
}
