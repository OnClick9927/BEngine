using BEngine.Rendering;

namespace BEngine.Editor;

public static class GUIUtility
{
    private static int _controlCount;
    private static readonly Stack<int> IdScopes = new();
    public static int hotControl { get; set; }
    public static int keyboardControl { get; set; }
    public static bool textFieldInput { get; internal set; }
    public static string systemCopyBuffer { get; set; } = string.Empty;
    public static Fix64 pixelsPerPoint { get; set; } = Fix64.One;
    [ThreadStatic] internal static Fix64 devicePixelsPerPoint;
    public static string fontFamily { get; set; } = "BEngine Built-in";
    public static Fix64 currentViewWidth { get; internal set; }
    public static Fix64 currentViewHeight { get; internal set; }
    public static int GetControlID(FocusType focusType) => GetControlID(0, focusType, default);
    public static int GetControlID(int hint, FocusType focusType) => GetControlID(hint, focusType, default);
    public static int GetControlID(int hint, FocusType focusType, Rect position)
    {
        var scope = IdScopes.Count == 0 ? 17 : IdScopes.Peek();
        return HashCode.Combine(scope, hint, (int)focusType, _controlCount++);
    }
    public static void ExitGUI() => throw new ExitGUIException();
    public static void GUIToScreenPoint(ref Vector2 point) => point = GUIToScreenPoint(point);
    public static Vector2 GUIToScreenPoint(Vector2 point) =>
        GUI.GUIToRootPoint(point) * pixelsPerPoint;
    public static Vector2 ScreenToGUIPoint(Vector2 point) => GUI.RootToGUIPoint(
        point / Fix64.Max(Fix64.FromDecimal(0.01m), pixelsPerPoint));
    internal static void BeginContainer(int id) { IdScopes.Push(id); _controlCount = 0; }
    internal static void EndContainer() { if (IdScopes.Count > 0) IdScopes.Pop(); }
    internal static int[] CaptureContainerScopes() => IdScopes.Reverse().ToArray();
    internal static int CaptureControlCount() => _controlCount;
    internal static void RestoreContainerScopes(IReadOnlyList<int> scopes, int controlCount)
    {
        IdScopes.Clear();
        foreach (var scope in scopes) IdScopes.Push(scope);
        _controlCount = Math.Max(0, controlCount);
    }
    internal static void BeginEvent()
    {
        _controlCount = 0;
        IdScopes.Clear();
    }

    internal static void ReleaseInputFocus()
    {
        hotControl = 0;
        keyboardControl = 0;
        textFieldInput = false;
        GUI.FocusControl(string.Empty);
    }
}
