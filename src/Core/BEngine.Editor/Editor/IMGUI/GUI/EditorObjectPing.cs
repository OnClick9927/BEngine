namespace BEngine.Editor;

/// <summary>
/// Owns the short-lived visual state created by EditorUtility.PingObject. Ping deliberately does
/// not participate in Selection: views may reveal and emphasize the object without changing the
/// object currently inspected by unlocked windows.
/// </summary>
internal static class EditorObjectPing
{
    internal const double DurationSeconds = 1.25;
    private static readonly Color HighlightColor = new(1, Fix64.FromDecimal(.78m),
        Fix64.FromDecimal(.08m));
    private static EditorObjectPingSnapshot _snapshot;

    internal static event Action<BObject>? pinged;

    internal static EditorObjectPingSnapshot snapshot => _snapshot;

    internal static BObject? currentTarget => _snapshot.InstanceId == 0 ||
                                               !Evaluate(EditorApplication.timeSinceStartup -
                                                   _snapshot.StartedAt).IsActive
        ? null
        : BObject.FindObjectFromInstanceID(_snapshot.InstanceId);

    internal static void Ping(BObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var path = NormalizePath(AssetDatabase.GetAssetPath(target));
        _snapshot = new EditorObjectPingSnapshot(target.GetInstanceID(), path,
            EditorApplication.timeSinceStartup);
        EditorCallbackDispatcher.Invoke(pinged, target, nameof(pinged));
        EditorApplication.QueuePlayerLoopUpdate();
    }

    internal static bool Draw(BObject? target, Rect rect) => target is not null &&
        DrawCore(target.GetInstanceID() == _snapshot.InstanceId, rect, EditorApplication.timeSinceStartup);

    internal static bool DrawPath(string? assetPath, Rect rect) => DrawCore(
        !string.IsNullOrEmpty(_snapshot.AssetPath) && NormalizePath(assetPath).Equals(
            _snapshot.AssetPath, StringComparison.OrdinalIgnoreCase), rect,
        EditorApplication.timeSinceStartup);

    internal static bool DrawHierarchy(BObject? target, Rect rect) => DrawCore(
        HierarchyObjectId(target) is { } targetId &&
        HierarchyObjectId(BObject.FindObjectFromInstanceID(_snapshot.InstanceId)) == targetId,
        rect, EditorApplication.timeSinceStartup);

    internal static GameObject? HierarchyGameObject(BObject? target) => target switch
    {
        GameObject gameObject => gameObject,
        Component component => component.gameObject,
        _ => null
    };

    internal static bool IsActive(BObject? target, double now, out EditorObjectPingFrame frame)
    {
        var matches = target is not null && target.GetInstanceID() == _snapshot.InstanceId;
        frame = matches ? Evaluate(now - _snapshot.StartedAt) : default;
        return matches && frame.IsActive;
    }

    internal static bool IsActivePath(string? assetPath, double now, out EditorObjectPingFrame frame)
    {
        var matches = !string.IsNullOrEmpty(_snapshot.AssetPath) && NormalizePath(assetPath).Equals(
            _snapshot.AssetPath, StringComparison.OrdinalIgnoreCase);
        frame = matches ? Evaluate(now - _snapshot.StartedAt) : default;
        return matches && frame.IsActive;
    }

    internal static EditorObjectPingFrame Evaluate(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0 || elapsedSeconds >= DurationSeconds)
            return default;

        var progress = (Fix64)(elapsedSeconds / DurationSeconds);
        // Two complete pulses make the locate feedback legible without keeping the editor busy.
        var wave = (Fix64.One - Fix64.Cos(progress * Fix64.TwoPi * 2)) / 2;
        var expansion = Fix64.One + wave * 4;
        var alpha = (Fix64.One - progress) *
                    (Fix64.One - wave * Fix64.FromDecimal(.18m));
        return new EditorObjectPingFrame(true, progress, expansion, alpha);
    }

    internal static Rect Expand(Rect rect, Fix64 amount) => new(
        rect.x - amount,
        rect.y - amount,
        Fix64.Max(0, rect.width + amount * 2),
        Fix64.Max(0, rect.height + amount * 2));

    internal static void ResetForTests()
    {
        _snapshot = default;
    }

    private static bool DrawCore(bool matches, Rect rect, double now)
    {
        var frame = Evaluate(now - _snapshot.StartedAt);
        if (!matches || !frame.IsActive) return false;
        DrawFrame(frame, rect);
        EditorWindow.currentDrawingWindow?.Repaint();
        return true;
    }

    private static void DrawFrame(EditorObjectPingFrame frame, Rect rect)
    {
        if (Event.current.type != EventType.Repaint || rect.width <= 0 || rect.height <= 0) return;
        var expanded = Expand(rect, frame.Expansion);
        var color = new Color(HighlightColor.r, HighlightColor.g, HighlightColor.b, frame.Alpha);
        DrawBorder(expanded, color, 2);
    }

    private static void DrawBorder(Rect rect, Color color, Fix64 width)
    {
        width = Fix64.Min(width, Fix64.Min(rect.width / 2, rect.height / 2));
        if (width <= 0 || color.a <= 0) return;
        GUI.DrawRect(new Rect(rect.x, rect.y, rect.width, width), color);
        GUI.DrawRect(new Rect(rect.x, rect.yMax - width, rect.width, width), color);
        GUI.DrawRect(new Rect(rect.x, rect.y + width, width,
            Fix64.Max(0, rect.height - width * 2)), color);
        GUI.DrawRect(new Rect(rect.xMax - width, rect.y + width, width,
            Fix64.Max(0, rect.height - width * 2)), color);
    }

    private static string NormalizePath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().Replace('\\', '/');

    private static int? HierarchyObjectId(BObject? target) => target switch
    {
        Scene scene => scene.GetInstanceID(),
        _ => HierarchyGameObject(target)?.GetInstanceID()
    };
}

internal readonly record struct EditorObjectPingSnapshot
{
    internal EditorObjectPingSnapshot(int instanceId, string? assetPath, double startedAt)
    {
        InstanceId = instanceId;
        AssetPath = assetPath ?? string.Empty;
        StartedAt = startedAt;
    }

    internal int InstanceId { get; }
    internal string AssetPath { get; }
    internal double StartedAt { get; }
}

internal readonly record struct EditorObjectPingFrame(
    bool IsActive,
    Fix64 Progress,
    Fix64 Expansion,
    Fix64 Alpha);
