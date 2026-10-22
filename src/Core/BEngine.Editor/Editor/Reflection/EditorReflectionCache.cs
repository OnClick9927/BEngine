using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BEngine.Editor;

/// <summary>
/// Cached editor-side member access for packages. The editor warms this index before any window is shown;
/// package drawers and inspectors should use it instead of scanning members during OnGUI.
/// </summary>
public static class EditorReflectionCache
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<MemberKey, Func<object, object?>> Getters = [];
    private static readonly Dictionary<MemberKey, PredicateInvoker[]> Predicates = [];
    private static readonly Dictionary<MemberKey, ActionInvoker[]> Actions = [];
    private static readonly ConditionalWeakTable<Type, CachedType> Types = new();
    private static int _generation = -1;

    public static bool TryGetMemberValue(object target, string memberName, out object? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
        EnsureFresh();
        Func<object, object?>? getter;
        lock (Gate) Getters.TryGetValue(new MemberKey(target.GetType(), memberName), out getter);
        if (getter is null) { value = null; return false; }
        value = getter(target);
        return true;
    }

    public static bool TryInvokePredicate(object target, string methodName, object? argument, out bool result)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        EnsureFresh();
        PredicateInvoker[] invokers;
        lock (Gate)
            invokers = Predicates.GetValueOrDefault(new MemberKey(target.GetType(), methodName)) ?? [];
        foreach (var invoker in invokers)
        {
            if (invoker.HasArgument && argument is not null && !invoker.ParameterType!.IsInstanceOfType(argument))
                continue;
            if (invoker.HasArgument && argument is null && invoker.ParameterType!.IsValueType) continue;
            result = invoker.Callback(target, argument);
            return true;
        }
        result = false;
        return false;
    }

    public static bool TryInvokeAction(object target, string methodName, object? argument = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        EnsureFresh();
        ActionInvoker[] invokers;
        lock (Gate) invokers = Actions.GetValueOrDefault(new MemberKey(target.GetType(), methodName)) ?? [];
        foreach (var invoker in invokers)
        {
            if (invoker.HasArgument && argument is not null && !invoker.ParameterType!.IsInstanceOfType(argument))
                continue;
            if (invoker.HasArgument && argument is null && invoker.ParameterType!.IsValueType) continue;
            invoker.Callback(target, argument);
            return true;
        }
        return false;
    }

    public static IReadOnlyList<Attribute> GetAttributes(MemberInfo? member) =>
        SerializedMemberMetadata.For(member).Attributes;

    internal static void Warmup() => EnsureFresh();

    private static void EnsureFresh()
    {
        var generation = RuntimeTypeCache.stats.Generation;
        lock (Gate)
        {
            if (_generation == generation) return;
            Getters.Clear(); Predicates.Clear(); Actions.Clear();
            foreach (var type in RuntimeTypeCache.GetAllTypes().Where(type => typeof(BObject).IsAssignableFrom(type)))
                CacheType(type);
            _generation = generation;
        }
    }

    private static void CacheType(Type type)
    {
        var cached = Types.GetValue(type, BuildType);
        foreach (var pair in cached.Getters) Getters[new(type, pair.Key)] = pair.Value;
        foreach (var pair in cached.Predicates) Predicates[new(type, pair.Key)] = pair.Value;
        foreach (var pair in cached.Actions) Actions[new(type, pair.Key)] = pair.Value;
    }

    private static CachedType BuildType(Type type)
    {
        var cached = new CachedType();
        foreach (var member in RuntimeTypeCache.GetInstanceMembers(type))
        {
            if (member is not (FieldInfo or PropertyInfo)) continue;
            var accessor = RuntimeTypeCache.GetMemberAccessor(member);
            cached.Getters.TryAdd(member.Name, accessor.Getter);
        }

        var predicateGroups = new Dictionary<string, List<PredicateInvoker>>(StringComparer.Ordinal);
        var actionGroups = new Dictionary<string, List<ActionInvoker>>(StringComparer.Ordinal);
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        foreach (var method in RuntimeTypeCache.GetMethods(current).Where(method => !method.IsStatic))
        {
            var parameters = method.GetParameters();
            if (parameters.Length > 1) continue;
            if (parameters.Length == 0 && method.ReturnType != typeof(void))
            {
                try { if (!cached.Getters.ContainsKey(method.Name)) cached.Getters.Add(method.Name, CompileGetter(method)); }
                catch { }
            }
            if (method.ReturnType == typeof(bool))
            {
                try
                {
                    predicateGroups.GetOrAdd(method.Name).Add(new PredicateInvoker(parameters.Length == 1,
                        parameters.Length == 1 ? parameters[0].ParameterType : null, CompilePredicate(method)));
                }
                catch { }
            }
            if (method.ReturnType == typeof(void))
            {
                try
                {
                    actionGroups.GetOrAdd(method.Name).Add(new ActionInvoker(parameters.Length == 1,
                        parameters.Length == 1 ? parameters[0].ParameterType : null, CompileAction(method)));
                }
                catch { }
            }
        }
        foreach (var pair in predicateGroups) cached.Predicates[pair.Key] = pair.Value.ToArray();
        foreach (var pair in actionGroups) cached.Actions[pair.Key] = pair.Value.ToArray();
        return cached;
    }

    private static Func<object, object?> CompileGetter(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var call = Expression.Call(Expression.Convert(target, method.DeclaringType!), method);
        return Expression.Lambda<Func<object, object?>>(Expression.Convert(call, typeof(object)), target).Compile();
    }

    private static Func<object, object?, bool> CompilePredicate(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var argument = Expression.Parameter(typeof(object), "argument");
        var parameters = method.GetParameters();
        var call = parameters.Length == 0
            ? Expression.Call(Expression.Convert(target, method.DeclaringType!), method)
            : Expression.Call(Expression.Convert(target, method.DeclaringType!), method,
                Expression.Convert(argument, parameters[0].ParameterType));
        return Expression.Lambda<Func<object, object?, bool>>(call, target, argument).Compile();
    }

    private static Action<object, object?> CompileAction(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var argument = Expression.Parameter(typeof(object), "argument");
        var parameters = method.GetParameters();
        var call = parameters.Length == 0
            ? Expression.Call(Expression.Convert(target, method.DeclaringType!), method)
            : Expression.Call(Expression.Convert(target, method.DeclaringType!), method,
                Expression.Convert(argument, parameters[0].ParameterType));
        return Expression.Lambda<Action<object, object?>>(call, target, argument).Compile();
    }

    private readonly record struct MemberKey(Type TargetType, string Name);
    private sealed class CachedType
    {
        internal readonly Dictionary<string, Func<object, object?>> Getters = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, PredicateInvoker[]> Predicates = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, ActionInvoker[]> Actions = new(StringComparer.Ordinal);
    }
    private readonly record struct PredicateInvoker(
        bool HasArgument,
        Type? ParameterType,
        Func<object, object?, bool> Callback);
    private readonly record struct ActionInvoker(
        bool HasArgument,
        Type? ParameterType,
        Action<object, object?> Callback);
}
