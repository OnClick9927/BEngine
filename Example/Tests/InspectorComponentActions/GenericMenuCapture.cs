using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using BEngine.Editor;

namespace BEngine.ExampleTests.InspectorComponentActions;

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

    internal static bool HasMenu => _items is IEnumerable;
    internal static bool IsAdvanced => _isAdvanced;

    internal static IReadOnlyList<MenuItemSnapshot> Items => RawItems().Select(item => new MenuItemSnapshot(
        (string)(item.GetType().GetProperty("Path")!.GetValue(item) ?? string.Empty),
        (bool)(item.GetType().GetProperty("On")!.GetValue(item) ?? false),
        (bool)(item.GetType().GetProperty("Enabled")!.GetValue(item) ?? false),
        item.GetType().GetProperty("Action")!.GetValue(item) is Action)).ToArray();

    internal static void Install()
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

    internal static void Reset()
    {
        _items = null;
        _isAdvanced = false;
    }

    internal static void Clear()
    {
        HandlerProperty.SetValue(null, null);
        _items = null;
        _isAdvanced = false;
    }

    internal static void Invoke(string path)
    {
        var item = RawItems().Single(candidate => string.Equals(
            candidate.GetType().GetProperty("Path")!.GetValue(candidate) as string,
            path, StringComparison.Ordinal));
        var enabled = (bool)(item.GetType().GetProperty("Enabled")!.GetValue(item) ?? false);
        var action = item.GetType().GetProperty("Action")!.GetValue(item) as Action;
        if (!enabled || action is null)
            throw new InvalidOperationException($"Menu item '{path}' is disabled.");
        action();
    }

    private static object[] RawItems() => _items is IEnumerable items
        ? items.Cast<object>().ToArray()
        : throw new InvalidOperationException("No GenericMenu was captured.");

    private static void Capture(object items)
    {
        _items = items;
        var presentation = CurrentPresentationProperty.GetValue(null) ??
                           throw new InvalidOperationException("GenericMenu presentation was unavailable.");
        _isAdvanced = (bool)(presentation.GetType().GetProperty(
            "IsAdvanced", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(presentation) ?? false);
    }
}
