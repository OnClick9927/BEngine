using System.Reflection;
using System.Reflection.Emit;
using System.Collections;

namespace BEngine;

/// <summary>
/// Application-wide reflection index. Assemblies are inspected once during startup or immediately when loaded;
/// gameplay and serialization paths only query immutable arrays and dictionaries.
/// </summary>
public static class RuntimeTypeCache
{
    private static readonly object Gate = new();
    private static readonly Dictionary<Assembly, AssemblyIndex> Assemblies = [];
    private static readonly Dictionary<string, Type> TypesByName = new(StringComparer.Ordinal);
    private static readonly Dictionary<Type, Type[]> DerivedTypes = [];
    private static readonly Dictionary<Type, Func<object>> Factories = [];
    private static readonly Dictionary<Type, ComponentReflectionInfo> Components = [];
    private static readonly Dictionary<Type, MethodInfo[]> MethodsByType = [];
    private static readonly Dictionary<Type, MemberInfo[]> MembersByType = [];
    private static readonly Dictionary<MemberLookupKey, MemberInfo> MembersByName = [];
    private static readonly Dictionary<MemberInfo, RuntimeMemberAccessor> MemberAccessors = [];
    private static readonly Dictionary<RuntimeMessageKey, RuntimeMessageHandler[]> MessageHandlers = [];
    private static readonly Dictionary<RuntimeMessageKey, Func<object, IEnumerator>> CoroutineFactories = [];
    private static readonly List<RuntimeInitializationEntry> RuntimeInitializers = [];
    private static readonly Dictionary<RuntimeInitializeLoadType, RuntimeInitializationEntry[]> InitializersByPhase = [];
    private static Type[] _runtimeSystemTypes = [];
    private static Type[] _allTypes = [];
    private static bool _initialized;
    private static int _generation;
    private static long _assembliesScanned;
    private static long _typesScanned;

    static RuntimeTypeCache() => AppDomain.CurrentDomain.AssemblyLoad += (_, args) =>
    {
        lock (Gate)
        {
            if (!_initialized) return;
        }
        RegisterAssemblies([args.LoadedAssembly]);
    };

    public static ReflectionCacheStats stats
    {
        get
        {
            lock (Gate)
                return new ReflectionCacheStats(_generation, Assemblies.Count, _allTypes.Length,
                    _assembliesScanned, _typesScanned);
        }
    }

    public static void Warmup() => RegisterAssemblies(AppDomain.CurrentDomain.GetAssemblies());

    public static void RegisterAssemblies(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        lock (Gate)
        {
            _initialized = true;
            var changed = false;
            foreach (var assembly in assemblies.Distinct())
            {
                if (Assemblies.ContainsKey(assembly) || !ReferencesCore(assembly)) continue;
                var index = Scan(assembly);
                Assemblies.Add(assembly, index);
                _assembliesScanned++;
                _typesScanned += index.Types.Length;
                changed = true;
            }
            if (changed) RebuildIndexes();
        }
    }

    public static void UnregisterAssemblies(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        lock (Gate)
        {
            var removed = false;
            foreach (var assembly in assemblies) removed |= Assemblies.Remove(assembly);
            if (removed) RebuildIndexes();
        }
    }

    public static Type? FindType(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return null;
        EnsureInitialized();
        lock (Gate) return TypesByName.GetValueOrDefault(fullName);
    }

    public static Type[] GetTypesDerivedFrom<T>() => GetTypesDerivedFrom(typeof(T));

    public static Type[] GetTypesDerivedFrom(Type parentType)
    {
        ArgumentNullException.ThrowIfNull(parentType);
        EnsureInitialized();
        lock (Gate)
        {
            if (DerivedTypes.TryGetValue(parentType, out var cached)) return cached;
            cached = _allTypes.Where(type => type != parentType && parentType.IsAssignableFrom(type)).ToArray();
            DerivedTypes[parentType] = cached;
            return cached;
        }
    }

    public static bool TryCreateInstance(Type type, out object instance)
    {
        ArgumentNullException.ThrowIfNull(type);
        EnsureInitialized();
        Func<object>? factory;
        lock (Gate)
        {
            if (!Factories.TryGetValue(type, out factory))
            {
                factory = CreateFactory(type);
                if (factory is null) { instance = null!; return false; }
                Factories[type] = factory;
            }
        }
        instance = factory();
        return true;
    }

    public static Type[] GetAllTypes() { EnsureInitialized(); lock (Gate) return _allTypes; }
    public static Type[] GetTypes(Assembly assembly)
    {
        EnsureInitialized();
        lock (Gate) return Assemblies.TryGetValue(assembly, out var index) ? index.Types : [];
    }

    /// <summary>Invokes a cached zero-argument Unity-style message without runtime reflection.</summary>
    public static bool TryInvokeMessage(MonoBehaviour target, string methodName) =>
        TryInvokeMessageCore(target, methodName, null, hasArgument: false);

    /// <summary>Invokes a cached one-argument Unity-style message without runtime reflection.</summary>
    public static bool TryInvokeMessage(MonoBehaviour target, string methodName, object argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        return TryInvokeMessageCore(target, methodName, argument, hasArgument: true);
    }

    internal static bool TryInvokeCoroutine(MonoBehaviour target, string methodName, out IEnumerator routine)
    {
        EnsureInitialized();
        Func<object, IEnumerator>? factory;
        lock (Gate) CoroutineFactories.TryGetValue(new RuntimeMessageKey(target.GetType(), methodName), out factory);
        if (factory is null) { routine = null!; return false; }
        routine = factory(target);
        return routine is not null;
    }

    internal static bool HasMessage(Type targetType, string methodName, bool hasArgument)
    {
        EnsureInitialized();
        lock (Gate) return MessageHandlers.TryGetValue(new RuntimeMessageKey(targetType, methodName), out var handlers) &&
                            handlers.Any(handler => handler.HasArgument == hasArgument);
    }

    internal static ComponentReflectionInfo GetComponentInfo(Type type)
    {
        EnsureInitialized();
        lock (Gate)
        {
            if (Components.TryGetValue(type, out var info)) return info;
            info = BuildComponentInfo(type);
            Components[type] = info;
            return info;
        }
    }

    internal static MemberInfo[] GetSerializableMembers(Type type, bool inspectorOnly)
    {
        var info = GetComponentInfo(type);
        return inspectorOnly ? info.InspectorMembers : info.SerializableMembers;
    }

    internal static RuntimeInitializationEntry[] GetRuntimeInitializers(RuntimeInitializeLoadType phase)
    {
        EnsureInitialized();
        lock (Gate) return InitializersByPhase.GetValueOrDefault(phase) ?? [];
    }

    internal static MethodInfo[] GetMethods(Type type)
    {
        EnsureInitialized();
        lock (Gate) return MethodsByType.GetValueOrDefault(type) ?? [];
    }

    internal static Func<object>? GetFactory(Type type)
    {
        EnsureInitialized();
        lock (Gate)
        {
            if (Factories.TryGetValue(type, out var factory)) return factory;
            factory = CreateFactory(type);
            if (factory is not null) Factories[type] = factory;
            return factory;
        }
    }

    internal static MemberInfo[] GetInstanceMembers(Type type)
    {
        EnsureInitialized();
        lock (Gate) return MembersByType.GetValueOrDefault(type) ?? [];
    }

    public static bool TryFindInstanceMember(Type type, string name, out MemberInfo member)
    {
        EnsureInitialized();
        lock (Gate) return MembersByName.TryGetValue(new MemberLookupKey(type, name), out member!);
    }

    public static RuntimeMemberAccessor GetMemberAccessor(MemberInfo member)
    {
        EnsureInitialized();
        lock (Gate)
        {
            if (MemberAccessors.TryGetValue(member, out var accessor)) return accessor;
            accessor = CreateMemberAccessor(member);
            MemberAccessors[member] = accessor;
            return accessor;
        }
    }

    internal static Type[] GetRuntimeSystemTypes()
    {
        EnsureInitialized();
        lock (Gate) return _runtimeSystemTypes;
    }

    private static void EnsureInitialized()
    {
        lock (Gate) { if (_initialized) return; }
        Warmup();
    }

    private static AssemblyIndex Scan(Assembly assembly)
    {
        var types = GetLoadableTypes(assembly);
        var initializers = new List<RuntimeInitializationEntry>();
        var methodsByType = new Dictionary<Type, MethodInfo[]>(types.Length);
        var membersByType = new Dictionary<Type, MemberInfo[]>(types.Length);
        foreach (var type in types)
        {
            var methods = GetMethodsCore(type);
            methodsByType[type] = methods;
            membersByType[type] = GetInstanceMembersCore(type);
            foreach (var method in methods)
            {
                if (!method.IsStatic) continue;
                RuntimeInitializeOnLoadMethodAttribute? attribute;
                try { attribute = method.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>(false); }
                catch { continue; }
                if (attribute is null) continue;
                if (method.ReturnType != typeof(void) || method.GetParameters().Length != 0)
                {
                    Debug.LogWarning("Runtime initialization method must be static void with no parameters: " +
                                     $"{method.DeclaringType?.FullName}.{method.Name}");
                    continue;
                }
                try
                {
                    initializers.Add(new RuntimeInitializationEntry(attribute.loadType,
                        (Action)method.CreateDelegate(typeof(Action)),
                        $"{method.DeclaringType?.FullName}.{method.Name}", method.MetadataToken));
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"Could not cache runtime initializer {method.DeclaringType?.FullName}." +
                                     $"{method.Name}: {exception.Message}");
                }
            }
        }
        return new AssemblyIndex(types, initializers.ToArray(), methodsByType, membersByType);
    }

    private static void RebuildIndexes()
    {
        TypesByName.Clear();
        DerivedTypes.Clear();
        Factories.Clear();
        Components.Clear();
        MethodsByType.Clear();
        MembersByType.Clear();
        MembersByName.Clear();
        MemberAccessors.Clear();
        MessageHandlers.Clear();
        CoroutineFactories.Clear();
        RuntimeInitializers.Clear();
        InitializersByPhase.Clear();
        _allTypes = Assemblies.Values.SelectMany(index => index.Types).Distinct().ToArray();
        foreach (var index in Assemblies.Values)
        {
            RuntimeInitializers.AddRange(index.Initializers);
            foreach (var pair in index.MethodsByType) MethodsByType[pair.Key] = pair.Value;
            foreach (var pair in index.MembersByType)
            {
                MembersByType[pair.Key] = pair.Value;
                foreach (var member in pair.Value)
                {
                    MembersByName.TryAdd(new MemberLookupKey(pair.Key, member.Name), member);
                    if (typeof(BObject).IsAssignableFrom(pair.Key))
                        MemberAccessors.TryAdd(member, CreateMemberAccessor(member));
                }
            }
            foreach (var type in index.Types)
            {
                if (type.FullName is { } fullName) TypesByName.TryAdd(fullName, type);
                if (type.AssemblyQualifiedName is { } qualified) TypesByName.TryAdd(qualified, type);
                if (!type.IsAbstract && type.IsClass &&
                    (typeof(Component).IsAssignableFrom(type) || typeof(ScriptableObject).IsAssignableFrom(type) ||
                     typeof(ISceneRuntimeSystem).IsAssignableFrom(type)))
                {
                    var factory = CreateFactory(type);
                    if (factory is not null) Factories[type] = factory;
                }
                if (!type.IsAbstract && typeof(Component).IsAssignableFrom(type))
                    Components[type] = BuildComponentInfo(type);
                if (!type.IsAbstract && typeof(MonoBehaviour).IsAssignableFrom(type))
                    CacheMessageHandlers(type);
            }
        }
        RuntimeInitializers.Sort((left, right) =>
        {
            var phase = left.Phase.CompareTo(right.Phase);
            if (phase != 0) return phase;
            var name = string.Compare(left.Description, right.Description, StringComparison.Ordinal);
            return name != 0 ? name : left.MetadataToken.CompareTo(right.MetadataToken);
        });
        foreach (var group in RuntimeInitializers.GroupBy(item => item.Phase))
            InitializersByPhase[group.Key] = group.ToArray();
        _runtimeSystemTypes = _allTypes.Where(type => type != typeof(ScopedSceneRuntimeSystem) &&
                                               type.IsClass && !type.IsAbstract &&
                                               typeof(ISceneRuntimeSystem).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        _generation++;
    }

    private static bool TryInvokeMessageCore(
        MonoBehaviour target,
        string methodName,
        object? argument,
        bool hasArgument)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        EnsureInitialized();
        RuntimeMessageHandler[] handlers;
        lock (Gate)
        {
            if (!MessageHandlers.TryGetValue(new RuntimeMessageKey(target.GetType(), methodName), out handlers!))
                return false;
        }
        foreach (var handler in handlers)
        {
            if (handler.HasArgument != hasArgument) continue;
            if (hasArgument && !handler.ParameterType!.IsInstanceOfType(argument)) continue;
            handler.Callback(target, argument);
            return true;
        }
        return false;
    }

    private static void CacheMessageHandlers(Type behaviourType)
    {
        var methods = EnumerateHierarchyMethods(behaviourType).ToArray();
        foreach (var group in methods.Where(method => !method.IsStatic && method.ReturnType == typeof(void))
                     .Select(method => (Method: method, Parameters: method.GetParameters()))
                     .Where(item => item.Parameters.Length <= 1)
                     .GroupBy(item => item.Method.Name, StringComparer.Ordinal))
        {
            var handlers = group.Select(item => CreateMessageHandler(item.Method, item.Parameters))
                .Where(handler => handler.Callback is not null)
                .OrderBy(handler => handler.HasArgument)
                .ToArray();
            if (handlers.Length > 0)
                MessageHandlers[new RuntimeMessageKey(behaviourType, group.Key)] = handlers;
        }
        foreach (var method in methods.Where(method => !method.IsStatic &&
                     typeof(IEnumerator).IsAssignableFrom(method.ReturnType) && method.GetParameters().Length == 0))
        {
            var factory = CreateCoroutineFactory(method);
            if (factory is not null)
                CoroutineFactories.TryAdd(new RuntimeMessageKey(behaviourType, method.Name), factory);
        }
    }

    private static IEnumerable<MethodInfo> EnumerateHierarchyMethods(Type type)
    {
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
            if (MethodsByType.TryGetValue(current, out var methods))
                foreach (var method in methods) yield return method;
    }

    private static Func<object, IEnumerator>? CreateCoroutineFactory(MethodInfo method)
    {
        try
        {
            var dynamicMethod = new DynamicMethod($"Coroutine_{method.Name}_{method.MetadataToken}",
                typeof(IEnumerator), [typeof(object)], typeof(RuntimeTypeCache).Module, skipVisibility: true);
            var il = dynamicMethod.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, method.DeclaringType!);
            il.Emit(method.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, method);
            if (method.ReturnType != typeof(IEnumerator)) il.Emit(OpCodes.Castclass, typeof(IEnumerator));
            il.Emit(OpCodes.Ret);
            return (Func<object, IEnumerator>)dynamicMethod.CreateDelegate(typeof(Func<object, IEnumerator>));
        }
        catch { return null; }
    }

    private static RuntimeMessageHandler CreateMessageHandler(MethodInfo method, ParameterInfo[] parameters)
    {
        try
        {
            var dynamicMethod = new DynamicMethod($"Message_{method.Name}_{method.MetadataToken}", typeof(void),
                [typeof(object), typeof(object)], typeof(RuntimeTypeCache).Module, skipVisibility: true);
            var il = dynamicMethod.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, method.DeclaringType!);
            if (parameters.Length == 1)
            {
                il.Emit(OpCodes.Ldarg_1);
                var parameterType = parameters[0].ParameterType;
                il.Emit(parameterType.IsValueType ? OpCodes.Unbox_Any : OpCodes.Castclass, parameterType);
            }
            il.Emit(method.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, method);
            il.Emit(OpCodes.Ret);
            return new RuntimeMessageHandler(parameters.Length == 1,
                parameters.Length == 1 ? parameters[0].ParameterType : null,
                (Action<object, object?>)dynamicMethod.CreateDelegate(typeof(Action<object, object?>)));
        }
        catch
        {
            return default;
        }
    }

    private static RuntimeMemberAccessor CreateMemberAccessor(MemberInfo member)
    {
        var declaringType = member.DeclaringType ?? throw new ArgumentException("Member has no declaring type.");
        var valueType = member switch
        {
            FieldInfo field => field.FieldType,
            PropertyInfo property => property.PropertyType,
            _ => throw new ArgumentException("Only fields and properties are supported.", nameof(member))
        };
        if (declaringType.IsValueType)
        {
            return member switch
            {
                FieldInfo field => new RuntimeMemberAccessor(valueType, field.GetValue,
                    field.IsInitOnly ? null : field.SetValue),
                PropertyInfo property => new RuntimeMemberAccessor(valueType, property.GetValue,
                    property.SetMethod is null ? null : property.SetValue),
                _ => default
            };
        }
        try
        {
            var getterMethod = new DynamicMethod($"Get_{member.Name}_{member.MetadataToken}", typeof(object),
                [typeof(object)], typeof(RuntimeTypeCache).Module, skipVisibility: true);
            var getterIl = getterMethod.GetILGenerator();
            getterIl.Emit(OpCodes.Ldarg_0);
            getterIl.Emit(OpCodes.Castclass, declaringType);
            if (member is FieldInfo field) getterIl.Emit(OpCodes.Ldfld, field);
            else
            {
                var getter = ((PropertyInfo)member).GetMethod ??
                             throw new InvalidOperationException("Property has no getter.");
                getterIl.Emit(getter.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, getter);
            }
            if (valueType.IsValueType) getterIl.Emit(OpCodes.Box, valueType);
            getterIl.Emit(OpCodes.Ret);
            var getterDelegate = (Func<object, object?>)getterMethod.CreateDelegate(typeof(Func<object, object?>));

            Action<object, object?>? setterDelegate = null;
            var canWrite = member is FieldInfo writableField && !writableField.IsInitOnly ||
                           member is PropertyInfo { SetMethod: not null };
            if (canWrite)
            {
                var setterMethod = new DynamicMethod($"Set_{member.Name}_{member.MetadataToken}", typeof(void),
                    [typeof(object), typeof(object)], typeof(RuntimeTypeCache).Module, skipVisibility: true);
                var setterIl = setterMethod.GetILGenerator();
                setterIl.Emit(OpCodes.Ldarg_0);
                setterIl.Emit(OpCodes.Castclass, declaringType);
                setterIl.Emit(OpCodes.Ldarg_1);
                setterIl.Emit(valueType.IsValueType ? OpCodes.Unbox_Any : OpCodes.Castclass, valueType);
                if (member is FieldInfo setField) setterIl.Emit(OpCodes.Stfld, setField);
                else
                {
                    var setter = ((PropertyInfo)member).SetMethod!;
                    setterIl.Emit(setter.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, setter);
                }
                setterIl.Emit(OpCodes.Ret);
                setterDelegate = (Action<object, object?>)setterMethod.CreateDelegate(typeof(Action<object, object?>));
            }
            return new RuntimeMemberAccessor(valueType, getterDelegate, setterDelegate);
        }
        catch
        {
            return member switch
            {
                FieldInfo field => new RuntimeMemberAccessor(valueType, field.GetValue,
                    field.IsInitOnly ? null : field.SetValue),
                PropertyInfo property => new RuntimeMemberAccessor(valueType, property.GetValue,
                    property.SetMethod is null ? null : property.SetValue),
                _ => default
            };
        }
    }

    private static ComponentReflectionInfo BuildComponentInfo(Type type)
    {
        if (!typeof(Component).IsAssignableFrom(type) || type.IsAbstract)
            return new ComponentReflectionInfo(null, false, [], [], []);
        var objectFactory = Factories.GetValueOrDefault(type) ?? CreateFactory(type);
        Func<Component>? factory = objectFactory is null ? null : () => (Component)objectFactory();
        var disallow = type.IsDefined(typeof(DisallowMultipleComponentAttribute), inherit: true);
        Type[] required;
        try
        {
            required = type.GetCustomAttributes<RequireComponentAttribute>(true)
                .SelectMany(attribute => attribute.requiredComponents).Distinct().ToArray();
        }
        catch { required = []; }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var fields = type.GetFields(flags).Where(field => !field.IsStatic && !field.IsInitOnly &&
            (field.IsPublic || field.IsDefined(typeof(SerializeFieldAttribute), true) ||
             field.IsDefined(typeof(SerializeReferenceAttribute), true)));
        var properties = type.GetProperties(flags).Where(property => property.GetIndexParameters().Length == 0 &&
            property.GetMethod is { IsPublic: true } && property.SetMethod is { IsPublic: true } &&
            !IgnoredComponentProperties.Contains(property.Name));
        var serializable = fields.Cast<MemberInfo>().Concat(properties)
            .OrderBy(member => member.Name, StringComparer.Ordinal).ToArray();
        var inspector = serializable.Where(member => !member.IsDefined(typeof(HideInInspectorAttribute), true)).ToArray();
        return new ComponentReflectionInfo(factory, disallow, required, serializable, inspector);
    }

    private static Func<object>? CreateFactory(Type type)
    {
        if (type.IsAbstract || !type.IsClass) return null;
        var constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, Type.EmptyTypes, modifiers: null);
        if (constructor is null) return null;
        try
        {
            var method = new DynamicMethod($"Create_{type.Name}_{type.MetadataToken}", typeof(object), Type.EmptyTypes,
                typeof(RuntimeTypeCache).Module, skipVisibility: true);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Newobj, constructor);
            il.Emit(OpCodes.Ret);
            return (Func<object>)method.CreateDelegate(typeof(Func<object>));
        }
        catch { return () => constructor.Invoke(null); }
    }

    private static bool ReferencesCore(Assembly assembly)
    {
        if (assembly.IsDynamic) return false;
        if (ReferenceEquals(assembly, typeof(RuntimeTypeCache).Assembly)) return true;
        var coreName = typeof(RuntimeTypeCache).Assembly.GetName().Name;
        try
        {
            return assembly.GetReferencedAssemblies().Any(reference =>
                reference.Name == coreName || reference.Name == "BEngine.Editor");
        }
        catch { return false; }
    }

    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException exception)
        { return exception.Types.Where(type => type is not null).Cast<Type>().ToArray(); }
        catch { return []; }
    }

    private static MethodInfo[] GetMethodsCore(Type type)
    {
        try
        {
            return type.GetMethods(BindingFlags.Static | BindingFlags.Instance |
                                   BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        }
        catch { return []; }
    }

    private static MemberInfo[] GetInstanceMembersCore(Type type)
    {
        var members = new List<MemberInfo>();
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            try
            {
                members.AddRange(current.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
                members.AddRange(current.GetProperties(BindingFlags.Instance | BindingFlags.Public |
                                                       BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(property => property.GetIndexParameters().Length == 0 && property.GetMethod is not null));
            }
            catch { }
        }
        return members.ToArray();
    }

    private static readonly HashSet<string> IgnoredComponentProperties = new(StringComparer.Ordinal)
    {
        nameof(BObject.Id), nameof(BObject.name), nameof(BObject.hideFlags), nameof(Component.gameObject),
        nameof(Component.transform), nameof(Component.enabled)
    };

    private sealed record AssemblyIndex(
        Type[] Types,
        RuntimeInitializationEntry[] Initializers,
        Dictionary<Type, MethodInfo[]> MethodsByType,
        Dictionary<Type, MemberInfo[]> MembersByType);

    private readonly record struct RuntimeMessageKey(Type BehaviourType, string MethodName);
    private readonly record struct MemberLookupKey(Type OwnerType, string Name);
    private readonly record struct RuntimeMessageHandler(
        bool HasArgument,
        Type? ParameterType,
        Action<object, object?> Callback);
}
