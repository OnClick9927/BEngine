using System.Linq.Expressions;
using System.Reflection;

namespace BEngine.Editor;

internal static class GizmoDrawerRegistry
{
    private const GizmoType SelectionOptions = GizmoType.NotInSelectionHierarchy |
                                               GizmoType.NonSelected |
                                               GizmoType.Selected |
                                               GizmoType.Active |
                                               GizmoType.InSelectionHierarchy;
    private static readonly Lock Gate = new();
    private static readonly Dictionary<Type, GizmoDrawerCommand[]> Commands = [];
    private static GizmoDrawerRegistration[] _registrations = [];
    private static GizmoDrawableType[] _drawableTypes = [];
    private static int _generation = -1;

    internal static IReadOnlyList<GizmoDrawableType> DrawableTypes
    {
        get
        {
            lock (Gate)
            {
                EnsureFresh();
                return _drawableTypes;
            }
        }
    }

    internal static void Warmup()
    {
        lock (Gate) EnsureFresh();
    }

    internal static GizmoDrawerCommand[] GetDrawers(Type componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        if (!typeof(Component).IsAssignableFrom(componentType))
            throw new ArgumentException($"{componentType.FullName} is not a Component type.",
                nameof(componentType));
        lock (Gate)
        {
            EnsureFresh();
            return GetDrawersCore(componentType);
        }
    }

    internal static bool ShouldInvoke(GizmoDrawerCommand command, GizmoType state)
    {
        var conditions = command.DrawOptions & SelectionOptions;
        return conditions == 0 || (conditions & state) != 0;
    }

    private static void EnsureFresh()
    {
        var generation = RuntimeTypeCache.stats.Generation;
        if (_generation == generation) return;

        Commands.Clear();
        var registrations = new List<GizmoDrawerRegistration>();
        foreach (var method in TypeCache.GetMethodsWithAttribute<DrawGizmoAttribute>())
        {
            if (!TryReadRegistration(method, out var registration)) continue;
            registrations.Add(registration);
        }
        _registrations = registrations
            .OrderBy(item => item.TargetType.FullName, StringComparer.Ordinal)
            .ThenBy(item => Describe(item.Method), StringComparer.Ordinal)
            .ToArray();
        _drawableTypes = BuildDrawableTypes();
        _generation = generation;
    }

    private static bool TryReadRegistration(
        MethodInfo method,
        out GizmoDrawerRegistration registration)
    {
        registration = default;
        if (!IsValid(method, out var componentType))
        {
            Debug.LogWarning("DrawGizmo method must be static void with parameters " +
                             $"(TComponent, GizmoType): {Describe(method)}");
            return false;
        }

        var feature = $"DrawGizmo attribute {Describe(method)}";
        if (!EditorFeatureGuard.TryInvoke(feature,
                () => method.GetCustomAttribute<DrawGizmoAttribute>(inherit: false), null,
                out var attribute) || attribute is null)
            return false;

        var compileFeature = $"DrawGizmo callback {Describe(method)}";
        if (!EditorFeatureGuard.TryInvoke(compileFeature, () => Compile(method, componentType), null,
                out var callback) || callback is null)
            return false;

        registration = new GizmoDrawerRegistration(
            method, componentType, attribute.drawOptions, callback, compileFeature);
        return true;
    }

    private static GizmoDrawerCommand[] GetDrawersCore(Type componentType)
    {
        if (Commands.TryGetValue(componentType, out var cached)) return cached;
        cached = _registrations
            .Where(item => item.TargetType.IsAssignableFrom(componentType))
            .Select(item => new GizmoDrawerCommand(
                item.DrawOptions, item.Callback, item.Feature, item.Method))
            .ToArray();
        Commands[componentType] = cached;
        return cached;
    }

    private static GizmoDrawableType[] BuildDrawableTypes()
    {
        var types = TypeCache.GetTypesDerivedFrom<Component>()
            .Where(type => type is { IsAbstract: false, ContainsGenericParameters: false } &&
                           (OverridesGizmoCallback(type) || GetDrawersCore(type).Length > 0))
            .Distinct()
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        var paths = types.Select(type => (Type: type, Path: MenuPath(type))).ToArray();
        var duplicates = paths.GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return paths.Select(item => new GizmoDrawableType(item.Type,
                duplicates.Contains(item.Path) ? DisambiguatedPath(item.Type, item.Path) : item.Path))
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ComponentType.FullName, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool OverridesGizmoCallback(Type type) =>
        Overrides(type, nameof(Component.OnDrawGizmos)) ||
        Overrides(type, nameof(Component.OnDrawGizmosSelected));

    private static bool Overrides(Type type, string methodName)
    {
        try
        {
            var method = type.GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.Public,
                binder: null, Type.EmptyTypes, modifiers: null);
            return method is not null && method.DeclaringType != typeof(Component) &&
                   method.GetBaseDefinition().DeclaringType == typeof(Component);
        }
        catch { return false; }
    }

    private static string MenuPath(Type type)
    {
        try
        {
            var menu = type.GetCustomAttribute<AddComponentMenuAttribute>(inherit: false)?.componentMenu;
            if (!string.IsNullOrWhiteSpace(menu))
                return menu.Replace('\\', '/').Trim('/');
        }
        catch { }
        return ObjectNames.NicifyVariableName(type.Name);
    }

    private static string DisambiguatedPath(Type type, string path)
    {
        var assembly = type.Assembly.GetName().Name ?? "Assembly";
        var owner = string.IsNullOrWhiteSpace(type.Namespace)
            ? assembly
            : $"{assembly}/{type.Namespace.Replace('.', '/')}";
        return $"{owner}/{path}";
    }

    private static bool IsValid(MethodInfo method, out Type componentType)
    {
        componentType = null!;
        if (!method.IsStatic || method.ContainsGenericParameters || method.ReturnType != typeof(void))
            return false;
        var parameters = method.GetParameters();
        if (parameters.Length != 2 || parameters[1].ParameterType != typeof(GizmoType) ||
            !typeof(Component).IsAssignableFrom(parameters[0].ParameterType))
            return false;
        componentType = parameters[0].ParameterType;
        return true;
    }

    private static Action<Component, GizmoType> Compile(MethodInfo method, Type componentType)
    {
        var component = Expression.Parameter(typeof(Component), "component");
        var state = Expression.Parameter(typeof(GizmoType), "state");
        return Expression.Lambda<Action<Component, GizmoType>>(
            Expression.Call(method, Expression.Convert(component, componentType), state),
            component, state).Compile();
    }

    private static string Describe(MethodInfo method) =>
        $"{method.DeclaringType?.FullName}.{method.Name}";

    private readonly record struct GizmoDrawerRegistration(
        MethodInfo Method,
        Type TargetType,
        GizmoType DrawOptions,
        Action<Component, GizmoType> Callback,
        string Feature);
}
