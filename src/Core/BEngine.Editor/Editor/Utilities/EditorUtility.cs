using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Serialization;

namespace BEngine.Editor;

public static class EditorUtility
{
    private static readonly Dictionary<int, int> DirtyObjects = [];
    private static readonly object ProgressGate = new();
    private static EditorProgressInfo _progressInfo = EditorProgressInfo.None;
    private static int _platformProgressSuppressionCount;
    internal static IEditorUtilityPlatform Platform { get; set; } = new WindowsEditorUtilityPlatform();

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

    internal static BObject[] GetDirtyObjects() => DirtyObjects.Keys
        .Select(InstanceIDToObject).Where(target => target is not null).Cast<BObject>().ToArray();

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
        => DisplayDialogCore(title, message, [ok]) == 0;

    public static bool DisplayDialog(string title, string message, string ok, string cancel) =>
        DisplayDialogCore(title, message, [ok, cancel]) == 0;

    public static int DisplayDialogComplex(string title, string message, string ok, string cancel, string alt)
        => DisplayDialogCore(title, message, [ok, cancel, alt]);

    public static void DisplayPopupMenu(Rect position, string menuItemPath, MenuCommand command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(menuItemPath);
        ArgumentNullException.ThrowIfNull(command);
        InvokeOnEditorMainThread(() =>
        {
            var menu = new GenericMenu();
            MenuItemRegistry.Discover().PopulatePath(menu, menuItemPath, command.context);
            menu.DropDown(position);
        }, nameof(DisplayPopupMenu));
    }

    public static string OpenFilePanel(string title, string directory, string extension)
    {
        var filter = BuildExtensionFilter(extension);
        return InvokeOnEditorMainThread(
            () => Platform.OpenFilePanel(NormalizeTitle(title), directory ?? string.Empty, filter),
            nameof(OpenFilePanel));
    }

    public static string OpenFilePanelWithFilters(string title, string directory, string[] filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        if (filters.Length == 0 || filters.Length % 2 != 0)
            throw new ArgumentException("Filters must contain alternating display names and extensions.",
                nameof(filters));
        var filter = string.Join('|', Enumerable.Range(0, filters.Length / 2)
            .Select(index => BuildFilterPair(filters[index * 2], filters[index * 2 + 1])));
        return InvokeOnEditorMainThread(
            () => Platform.OpenFilePanel(NormalizeTitle(title), directory ?? string.Empty, filter),
            nameof(OpenFilePanelWithFilters));
    }

    public static string OpenFolderPanel(string title, string folder, string defaultName) =>
        InvokeOnEditorMainThread(
            () => Platform.OpenFolderPanel(NormalizeTitle(title), folder ?? string.Empty,
                defaultName ?? string.Empty), nameof(OpenFolderPanel));

    public static void OpenWithDefaultApp(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        InvokeOnEditorMainThread(() => Platform.OpenWithDefaultApp(fileName.Trim()),
            nameof(OpenWithDefaultApp));
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
        var changed = false;
        lock (ProgressGate)
        {
            if (_progressInfo.IsVisible)
            {
                _progressInfo = EditorProgressInfo.None;
                changed = true;
            }
            state = _progressInfo;
        }
        if (changed) RaiseProgressChanged(state);
        if (Volatile.Read(ref _platformProgressSuppressionCount) == 0)
            InvokeOnEditorMainThread(Platform.ClearProgress, nameof(ClearProgressBar));
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
        if (Volatile.Read(ref _platformProgressSuppressionCount) == 0)
            InvokeOnEditorMainThread(() => Platform.ShowProgress(state,
                state.IsCancelable ? RequestProgressCancellation : null), nameof(DisplayProgressBar));
    }

    private static void RaiseProgressChanged(EditorProgressInfo state)
        => EditorCallbackDispatcher.Invoke(progressChanged, state, nameof(progressChanged));

    internal static IDisposable SuppressPlatformProgress()
    {
        Interlocked.Increment(ref _platformProgressSuppressionCount);
        return new PlatformProgressSuppression();
    }

    private static int DisplayDialogCore(string title, string message, string[] buttons)
    {
        if (buttons.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Dialog button labels cannot be empty.", nameof(buttons));
        return InvokeOnEditorMainThread(
            () => Platform.DisplayDialog(NormalizeTitle(title), message ?? string.Empty, buttons),
            nameof(DisplayDialog));
    }

    private static string BuildExtensionFilter(string? extension)
    {
        var value = extension?.Trim() ?? string.Empty;
        if (value.Length == 0 || value is "*" or ".*") return "All files (*.*)|*.*";
        var patterns = NormalizePatterns(value);
        var label = value.TrimStart('.', '*').ToUpperInvariant();
        return $"{label} files ({patterns})|{patterns}|All files (*.*)|*.*";
    }

    private static string BuildFilterPair(string name, string extensions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(extensions);
        var patterns = NormalizePatterns(extensions);
        return $"{name.Trim()} ({patterns})|{patterns}";
    }

    private static string NormalizePatterns(string extensions)
    {
        var patterns = extensions.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries).Select(extension =>
        {
            var value = extension.Trim();
            if (value is "*" or ".*" or "*.*") return "*.*";
            if (value.StartsWith("*.", StringComparison.Ordinal)) return value;
            return $"*.{value.TrimStart('.')}";
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (patterns.Length == 0)
            throw new ArgumentException("At least one file extension is required.", nameof(extensions));
        return string.Join(';', patterns);
    }

    private static string NormalizeTitle(string? title) =>
        string.IsNullOrWhiteSpace(title) ? "BEngine" : title.Trim();

    private static void InvokeOnEditorMainThread(Action callback, string name) =>
        InvokeOnEditorMainThread(() =>
        {
            callback();
            return true;
        }, name);

    private static T InvokeOnEditorMainThread<T>(Func<T> callback, string name)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var scheduler = EditorApplication.TaskScheduler;
        if (scheduler is null || scheduler.IsMainThread) return callback();

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.Post(() =>
        {
            try { completion.SetResult(callback()); }
            catch (Exception exception) { completion.SetException(exception); }
        }, $"EditorUtility.{name}");
        return completion.Task.GetAwaiter().GetResult();
    }

    internal sealed class DirtyState(IReadOnlyDictionary<int, int> counts)
    {
        internal IReadOnlyDictionary<int, int> Counts { get; } =
            new Dictionary<int, int>(counts);
    }

    private sealed class PlatformProgressSuppression : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Interlocked.Decrement(ref _platformProgressSuppressionCount);
        }
    }
    public static void FocusProjectWindow() => EditorApplication.RepaintProjectWindow();
    public static void PingObject(BObject target) => EditorObjectPing.Ping(target);
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
