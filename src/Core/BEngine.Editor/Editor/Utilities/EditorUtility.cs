using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Serialization;

namespace BEngine.Editor;

public static class EditorUtility
{
    private static readonly Dictionary<int, int> DirtyObjects = [];
    private static readonly object ProgressGate = new();
    private static EditorProgressInfo _progressInfo = EditorProgressInfo.None;

    public static event Action<EditorProgressInfo>? progressChanged;
    public static EditorProgressInfo progressInfo
    {
        get { lock (ProgressGate) return _progressInfo; }
    }

    public static void SetDirty(BObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var instanceId = target.GetInstanceID();
        DirtyObjects[instanceId] = DirtyObjects.GetValueOrDefault(instanceId) + 1;
        var scene = target is GameObject gameObject ? gameObject.scene : null;
        if (target is Component component)
        {
            try { scene = component.gameObject.scene; }
            catch (InvalidOperationException) { }
        }
        if (scene is not null) EditorSceneManager.MarkSceneDirty(scene);
    }

    public static bool IsDirty(BObject target) => target is not null && DirtyObjects.ContainsKey(target.GetInstanceID());
    public static int GetDirtyCount(BObject target) =>
        target is not null ? DirtyObjects.GetValueOrDefault(target.GetInstanceID()) : 0;
    public static void ClearDirty(BObject target)
    {
        if (target is not null) DirtyObjects.Remove(target.GetInstanceID());
    }

    internal static DirtyState CaptureDirtyState() => new(DirtyObjects);

    internal static void RestoreDirtyState(DirtyState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        DirtyObjects.Clear();
        foreach (var pair in state.Counts) DirtyObjects.Add(pair.Key, pair.Value);
    }

    public static BObject? InstanceIDToObject(int instanceId) => BObject.FindObjectFromInstanceID(instanceId);
    public static bool IsPersistent(BObject target) => AssetDatabase.Contains(target);
    public static void CopySerialized(BObject source, BObject destination) => ObjectState.CopyValues(source, destination);
    public static void CopySerializedManagedFieldsOnly(BObject source, BObject destination) =>
        CopySerialized(source, destination);

    public static bool DisplayDialog(string title, string message, string ok)
    {
        Debug.Log($"{title}: {message}");
        return true;
    }

    public static bool DisplayDialog(string title, string message, string ok, string cancel) =>
        DisplayDialog(title, message, ok);

    public static int DisplayDialogComplex(string title, string message, string ok, string cancel, string alt)
    {
        DisplayDialog(title, message, ok);
        return 0;
    }

    public static void DisplayProgressBar(string title, string info, float progress) =>
        ReportProgress(title, info, progress, cancelable: false);

    public static bool DisplayCancelableProgressBar(string title, string info, float progress)
    {
        ReportProgress(title, info, progress, cancelable: true);
        return progressInfo.IsCancellationRequested;
    }

    public static void ClearProgressBar()
    {
        EditorProgressInfo state;
        lock (ProgressGate)
        {
            if (!_progressInfo.IsVisible) return;
            _progressInfo = EditorProgressInfo.None;
            state = _progressInfo;
        }
        RaiseProgressChanged(state);
    }

    internal static void RequestProgressCancellation()
    {
        EditorProgressInfo state;
        lock (ProgressGate)
        {
            if (!_progressInfo.IsVisible || !_progressInfo.IsCancelable ||
                _progressInfo.IsCancellationRequested) return;
            _progressInfo = _progressInfo with { IsCancellationRequested = true };
            state = _progressInfo;
        }
        RaiseProgressChanged(state);
    }

    private static void ReportProgress(string title, string info, float progress, bool cancelable)
    {
        title = string.IsNullOrWhiteSpace(title) ? "BEngine" : title.Trim();
        info = info?.Trim() ?? string.Empty;
        EditorProgressInfo state;
        lock (ProgressGate)
        {
            var preserveCancellation = cancelable && _progressInfo.IsVisible &&
                string.Equals(_progressInfo.Title, title, StringComparison.Ordinal);
            _progressInfo = new EditorProgressInfo(title, info, Math.Clamp(progress, 0f, 1f),
                true, cancelable, preserveCancellation && _progressInfo.IsCancellationRequested);
            state = _progressInfo;
        }
        RaiseProgressChanged(state);
    }

    private static void RaiseProgressChanged(EditorProgressInfo state)
        => EditorCallbackDispatcher.Invoke(progressChanged, state, nameof(progressChanged));

    internal sealed class DirtyState(IReadOnlyDictionary<int, int> counts)
    {
        internal IReadOnlyDictionary<int, int> Counts { get; } =
            new Dictionary<int, int>(counts);
    }
    public static void FocusProjectWindow() => EditorApplication.RepaintProjectWindow();
    public static void PingObject(BObject target) => Selection.activeObject = target;
    public static void PingObject(int instanceId)
    {
        if (InstanceIDToObject(instanceId) is { } target) PingObject(target);
    }

    public static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var suffix = 0;
        while (value >= 1024 && suffix < suffixes.Length - 1)
        {
            value /= 1024;
            suffix++;
        }
        return $"{value:0.##} {suffixes[suffix]}";
    }

    public static int NaturalCompare(string? left, string? right) =>
        StringComparer.CurrentCultureIgnoreCase.Compare(left, right);
}
