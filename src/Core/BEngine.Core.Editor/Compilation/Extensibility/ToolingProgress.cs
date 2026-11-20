namespace BEngine.Editor;

/// <summary>Tooling progress can be presented by the active editor or ignored by a headless host.</summary>
public static class ToolingProgress
{
    public static event Action<string, string, float>? ReportRequested;
    public static event Action? ClearRequested;
    public static event Action<EditorProgressInfo>? Changed;
    public static void Notify(EditorProgressInfo progress) => Changed?.Invoke(progress);

    public static void Report(string title, string info, float progress)
    {
        if (ReportRequested is { } report) report(title, info, progress);
        else
        {
            var state = new EditorProgressInfo(title, info, Math.Clamp(progress, 0, 1), true, false, false);
            Notify(state);
            EditorServiceAccess.GetService<IEditorUtilityPlatform>()?.ShowProgress(state, null);
        }
    }

    public static void Clear()
    {
        if (ClearRequested is { } clear) clear();
        else EditorServiceAccess.GetService<IEditorUtilityPlatform>()?.ClearProgress();
    }
}
