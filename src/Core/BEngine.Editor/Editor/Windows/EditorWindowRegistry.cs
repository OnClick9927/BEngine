using System.Diagnostics.CodeAnalysis;

namespace BEngine.Editor;

/// <summary>
/// Process-wide access point for EditorWindow views, models and controllers. Entries are weak so
/// closed or abandoned windows are not kept alive by the registry.
/// </summary>
public static class EditorWindowRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, WeakReference<EditorWindow>> Windows =
        new(StringComparer.Ordinal);

    public static IReadOnlyList<EditorWindow> GetWindows(bool openOnly = false) =>
        GetWindows<EditorWindow>(openOnly);

    public static IReadOnlyList<TWindow> GetWindows<TWindow>(bool openOnly = false)
        where TWindow : EditorWindow
    {
        lock (Gate)
        {
            PruneLocked();
            List<TWindow>? result = null;
            foreach (var reference in Windows.Values)
            {
                if (reference.TryGetTarget(out var window) && window is TWindow typed &&
                    (!openOnly || typed.isOpen))
                    (result ??= []).Add(typed);
            }
            if (result is null) return [];
            result.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.windowId, right.windowId));
            return result.ToArray();
        }
    }

    public static EditorWindow GetWindow(string windowId) =>
        TryGetWindow(windowId, out var window)
            ? window
            : throw new KeyNotFoundException($"EditorWindow '{windowId}' is not available.");

    public static TWindow GetWindow<TWindow>(string windowId) where TWindow : EditorWindow =>
        GetWindow(windowId) as TWindow ?? throw new InvalidCastException(
            $"EditorWindow '{windowId}' is not '{typeof(TWindow).FullName}'.");

    public static bool TryGetWindow(string windowId, out EditorWindow window)
    {
        if (string.IsNullOrWhiteSpace(windowId))
        {
            window = null!;
            return false;
        }

        lock (Gate)
        {
            if (Windows.TryGetValue(windowId, out var reference) &&
                reference.TryGetTarget(out window!)) return true;
            Windows.Remove(windowId);
            window = null!;
            return false;
        }
    }

    public static bool TryGetWindow<TWindow>(
        string windowId,
        [NotNullWhen(true)] out TWindow? window)
        where TWindow : EditorWindow
    {
        window = TryGetWindow(windowId, out var candidate) ? candidate as TWindow : null;
        return window is not null;
    }

    public static IEditorWindowModel GetModel(string windowId) => GetWindow(windowId).model;

    public static TModel GetModel<TModel>(string windowId) where TModel : class, IEditorWindowModel =>
        GetModel(windowId) as TModel ?? throw new InvalidCastException(
            $"EditorWindow '{windowId}' does not expose model '{typeof(TModel).FullName}'.");

    public static bool TryGetModel<TModel>(string windowId, [NotNullWhen(true)] out TModel? model)
        where TModel : class, IEditorWindowModel
    {
        model = TryGetWindow(windowId, out var window) ? window.model as TModel : null;
        return model is not null;
    }

    public static IReadOnlyList<TModel> GetModels<TModel>(bool openOnly = false)
        where TModel : class, IEditorWindowModel => GetWindows(openOnly)
            .Select(window => window.model)
            .OfType<TModel>()
            .ToArray();

    public static IEditorWindowController GetController(string windowId) =>
        GetWindow(windowId).controller;

    public static TController GetController<TController>(string windowId)
        where TController : class, IEditorWindowController =>
        GetController(windowId) as TController ?? throw new InvalidCastException(
            $"EditorWindow '{windowId}' does not expose controller '{typeof(TController).FullName}'.");

    public static bool TryGetController<TController>(
        string windowId,
        [NotNullWhen(true)] out TController? controller)
        where TController : class, IEditorWindowController
    {
        controller = TryGetWindow(windowId, out var window)
            ? window.controller as TController
            : null;
        return controller is not null;
    }

    public static IReadOnlyList<TController> GetControllers<TController>(bool openOnly = false)
        where TController : class, IEditorWindowController => GetWindows(openOnly)
            .Select(window => window.controller)
            .OfType<TController>()
            .ToArray();

    public static void InvokeController<TController>(string windowId, Action<TController> command)
        where TController : class, IEditorWindowController
    {
        ArgumentNullException.ThrowIfNull(command);
        command(GetController<TController>(windowId));
    }

    public static TResult InvokeController<TController, TResult>(
        string windowId,
        Func<TController, TResult> command)
        where TController : class, IEditorWindowController
    {
        ArgumentNullException.ThrowIfNull(command);
        return command(GetController<TController>(windowId));
    }

    internal static void Register(EditorWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        lock (Gate)
        {
            PruneLocked();
            if (Windows.TryGetValue(window.windowId, out var existing) &&
                existing.TryGetTarget(out var target) && !ReferenceEquals(target, window))
                throw new InvalidOperationException($"Duplicate EditorWindow id '{window.windowId}'.");
            Windows[window.windowId] = new WeakReference<EditorWindow>(window);
        }
    }

    private static void PruneLocked()
    {
        List<string>? deadKeys = null;
        foreach (var (key, reference) in Windows)
            if (!reference.TryGetTarget(out _)) (deadKeys ??= []).Add(key);
        if (deadKeys is not null)
            foreach (var key in deadKeys) Windows.Remove(key);
    }
}
