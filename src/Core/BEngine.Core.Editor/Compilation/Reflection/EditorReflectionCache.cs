using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BEngine.Editor;

/// <summary>
/// Cached editor-side member access for packages. Accessors are prepared when a member is first used;
/// package drawers and inspectors should use it instead of scanning members during OnGUI.
/// </summary>
public static class EditorReflectionCache
{
    private static readonly ConditionalWeakTable<Type, CachedType> Types = new();

    public static bool TryGetMemberValue(object target, string memberName, out object? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
        if (For(target.GetType()).Getters.TryGetValue(memberName, out var getters))
            foreach (var getter in getters)
                if (getter.Value is { } callback) { value = callback(target); return true; }
        value = null;
        return false;
    }

    public static bool TryInvokePredicate(object target, string methodName, object? argument, out bool result)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        var invokers = For(target.GetType()).Predicates.GetValueOrDefault(methodName) ?? [];
        foreach (var invoker in invokers)
        {
            if (invoker.HasArgument && argument is not null && !invoker.ParameterType!.IsInstanceOfType(argument))
                continue;
            if (invoker.HasArgument && argument is null && invoker.ParameterType!.IsValueType) continue;
            if (invoker.Callback.Value is not { } callback) continue;
            result = callback(target, argument);
            return true;
        }
        result = false;
        return false;
    }

    public static bool TryInvokeAction(object target, string methodName, object? argument = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        var invokers = For(target.GetType()).Actions.GetValueOrDefault(methodName) ?? [];
        foreach (var invoker in invokers)
        {
            if (invoker.HasArgument && argument is not null && !invoker.ParameterType!.IsInstanceOfType(argument))
                continue;
            if (invoker.HasArgument && argument is null && invoker.ParameterType!.IsValueType) continue;
            if (invoker.Callback.Value is not { } callback) continue;
            callback(target, argument);
            return true;
        }
        return false;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MemberInfo, Attribute[]> AttributeCache = new();

    public static IReadOnlyList<Attribute> GetAttributes(MemberInfo? member) =>
        member is null ? [] : AttributeCache.GetValue(member, static key => key.GetCustomAttributes().ToArray());

    // A Type's members are immutable. Reloaded assemblies have new Type identities;
    // weak keys retain no collectible domain after its targets have gone away.
    private static CachedType For(Type type) => Types.GetValue(type, BuildType);

    private static CachedType BuildType(Type type)
    {
        var cached = new CachedType();
        var getters = new Dictionary<string, List<Lazy<Func<object, object?>?>>>(StringComparer.Ordinal);
        foreach (var member in RuntimeTypeCache.GetInstanceMembers(type))
        {
            if (member is not (FieldInfo or PropertyInfo)) continue;
            getters.TryAdd(member.Name, [Prepare(() => RuntimeTypeCache.GetMemberAccessor(member).Getter)]);
        }
        var memberNames = getters.Keys.ToHashSet(StringComparer.Ordinal);

        var predicateGroups = new Dictionary<string, List<PredicateInvoker>>(StringComparer.Ordinal);
        var actionGroups = new Dictionary<string, List<ActionInvoker>>(StringComparer.Ordinal);
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            var __filteredSource4007 = RuntimeTypeCache.GetMethods(current);
            ArgumentNullException.ThrowIfNull(__filteredSource4007, "source");
            foreach (var method in __filteredSource4007)
            {
                if (!(!method.IsStatic))
                    continue;
                var parameters = method.GetParameters();
                if (parameters.Length > 1)
                    continue;
                if (parameters.Length == 0 && method.ReturnType != typeof(void))
                {
                    if (!memberNames.Contains(method.Name))
                        getters.GetOrAdd(method.Name).Add(Prepare(() => CompileGetter(method)));
                }
        
                if (method.ReturnType == typeof(bool))
                {
                    predicateGroups.GetOrAdd(method.Name).Add(new PredicateInvoker(parameters.Length == 1, parameters.Length == 1 ? parameters[0].ParameterType : null, Prepare(() => CompilePredicate(method))));
                }
        
                if (method.ReturnType == typeof(void))
                {
                    actionGroups.GetOrAdd(method.Name).Add(new ActionInvoker(parameters.Length == 1, parameters.Length == 1 ? parameters[0].ParameterType : null, Prepare(() => CompileAction(method))));
                }
            }
        }
        foreach (var pair in getters) cached.Getters[pair.Key] = pair.Value.ToArray();
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

    // Unsupported signatures remain unavailable, as in eager discovery. Exceptions
    // from invoking a successfully compiled user callback still reach its caller.
    private static Lazy<T?> Prepare<T>(Func<T> build) where T : class => new(() =>
    {
        try { return build(); }
        catch { return null; }
    });

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

    private sealed class CachedType
    {
        internal readonly Dictionary<string, Lazy<Func<object, object?>?>[]> Getters = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, PredicateInvoker[]> Predicates = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, ActionInvoker[]> Actions = new(StringComparer.Ordinal);
    }
    private readonly record struct PredicateInvoker(
        bool HasArgument,
        Type? ParameterType,
        Lazy<Func<object, object?, bool>?> Callback);
    private readonly record struct ActionInvoker(
        bool HasArgument,
        Type? ParameterType,
        Lazy<Action<object, object?>?> Callback);
}
