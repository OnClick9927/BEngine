using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using BEngine.Editor;

namespace BEngine.ExampleTests.HierarchyMultiScene;

internal static class GenericMenuCapture
{
    private static readonly Type DispatcherType = typeof(EditorWindow).Assembly.GetType(
        "BEngine.Editor.GenericMenuDispatcher", throwOnError: true)!;
    private static readonly PropertyInfo HandlerProperty = DispatcherType.GetProperty(
        "Handler", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly PropertyInfo CurrentPresentationProperty = DispatcherType.GetProperty(
        "CurrentPresentation", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static object? _items;
    private static bool _isAdvanced;

    public static bool IsAdvanced => _isAdvanced;

    public static void Install()
    {
        _items = null;
        _isAdvanced = false;
        var handlerType = HandlerProperty.PropertyType;
        var parameterType = handlerType.GetMethod("Invoke")!.GetParameters()[0].ParameterType;
        var parameter = Expression.Parameter(parameterType, "items");
        var capture = typeof(GenericMenuCapture).GetMethod(nameof(Capture),
            BindingFlags.Static | BindingFlags.NonPublic)!;
        HandlerProperty.SetValue(null, Expression.Lambda(handlerType,
            Expression.Call(capture, Expression.Convert(parameter, typeof(object))), parameter).Compile());
    }

    public static void Clear()
    {
        HandlerProperty.SetValue(null, null);
        _items = null;
        _isAdvanced = false;
    }

    public static IReadOnlyList<CapturedMenuItem> CapturedItems => Items().Select(item => new CapturedMenuItem(
        Read<string>(item, "Path"),
        Read<bool>(item, "Enabled"),
        Read<bool>(item, "On"),
        Read<bool>(item, "Separator"),
        Read<Action?>(item, "Action"))).ToArray();

    public static IReadOnlyList<string> Paths => CapturedItems
        .Where(item => !item.Separator)
        .Select(item => item.Path)
        .ToArray();

    public static void Invoke(string path)
    {
        var item = Items().Single(candidate => string.Equals(
            candidate.GetType().GetProperty("Path")!.GetValue(candidate) as string,
            path, StringComparison.Ordinal));
        var action = item.GetType().GetProperty("Action")!.GetValue(item) as Action ??
                     throw new InvalidOperationException($"Menu item '{path}' has no action.");
        action();
    }

    private static object[] Items() => _items is IEnumerable items
        ? items.Cast<object>().ToArray()
        : throw new InvalidOperationException("No GenericMenu was captured.");

    private static T Read<T>(object item, string propertyName) =>
        (T)item.GetType().GetProperty(propertyName)!.GetValue(item)!;

    private static void Capture(object items)
    {
        _items = items;
        var presentation = CurrentPresentationProperty.GetValue(null) ??
                           throw new InvalidOperationException("GenericMenu presentation was unavailable.");
        _isAdvanced = (bool)(presentation.GetType().GetProperty(
            "IsAdvanced", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(presentation) ?? false);
    }

    internal sealed record CapturedMenuItem(
        string Path,
        bool Enabled,
        bool Checked,
        bool Separator,
        Action? Action);
}
