using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace BEngine;

/// <summary>
/// Application-wide reflection index. Complete assembly indexes are reused;
/// incomplete scans are retried when registered. Gameplay and serialization query cached arrays and dictionaries.
/// </summary>
public static class RuntimeTypeCache
{
    // Asset import workers query this cache while the main thread activates domains.
    // Index publication and preference changes must complete before another reader proceeds.
    private static readonly Lock IndexGate = new();
    private static readonly Dictionary<Assembly, AssemblyIndex> Assemblies = [];
    private static readonly ConditionalWeakTable<Assembly, HashSet<string>> InitializerOwners = new();
    private static readonly Dictionary<string, Type> TypesByName = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Type> SerializedTypes = new(StringComparer.Ordinal);
    private static readonly Dictionary<Type, Type[]> DerivedTypes = [];
    private static readonly Dictionary<Type, Func<object>> Factories = [];
    private static readonly Dictionary<Type, ComponentReflectionInfo> Components = [];
    private static readonly Dictionary<Type, MethodInfo[]> MethodsByType = [];
    private static readonly Dictionary<Type, MemberInfo[]> MembersByType = [];
    private static readonly ConditionalWeakTable<Type, MemberInfo[]> DetachedMembers = new();
    private static readonly Dictionary<MemberLookupKey, MemberInfo> MembersByName = [];
    private static readonly Dictionary<MemberInfo, RuntimeMemberAccessor> MemberAccessors = [];
    private static readonly Dictionary<RuntimeMessageKey, RuntimeMessageHandler[]> MessageHandlers = [];
    private static readonly HashSet<Type> MessageTypes = [];
    private static readonly List<RuntimeInitializationEntry> RuntimeInitializers = [];
    private static readonly Dictionary<RuntimeInitializeLoadType, RuntimeInitializationEntry[]> InitializersByPhase = [];
    private static readonly List<AssemblyPreference> AssemblyPreferences = [];
    private static readonly Queue<Assembly> PendingAssemblies = new();
    private static readonly object PendingAssemblyGate = new();
    private static Type[] _runtimeSystemTypes = [];
    private static Type[] _allTypes = [];
    private static volatile bool _initialized;
    private static int _generation;
    private static long _assembliesScanned;
    private static long _typesScanned;
    private static bool _rebuildingIndexes;
    private static bool _rebuildRequested;
    private static AssemblyIndex[]? _indexedAssemblyOrder;

    static RuntimeTypeCache() => RuntimeServiceAccess.PlatformService.AssemblyLoaded += (_, args) =>
    {
        if (!_initialized) return;
        // Mono invokes this handler from its native assembly-load hook. Reflection here can load a
        // dependency recursively; if that scan fails, Mono treats the managed exception as a fatal
        // load-hook error. Defer all work until the next ordinary cache access instead.
        lock (PendingAssemblyGate) PendingAssemblies.Enqueue(args.LoadedAssembly);
    };

    public static ReflectionCacheStats stats
    {
        get
        {
            lock (IndexGate)
                return new(_generation, Assemblies.Count, _allTypes.Length, _assembliesScanned, _typesScanned);
        }
    }

    public static void Warmup() =>
        RegisterAssemblies(RuntimeServiceAccess.PlatformService.GetLoadedAssemblies());

    /// <summary>
    /// Registers a build-generated complete list of types declaring runtime initialization
    /// methods for this exact Host assembly. An empty list is meaningful. Assemblies without
    /// an index, including downloaded modules, retain ordinary reflection discovery.
    /// </summary>
    public static void RegisterRuntimeInitializerOwners(Assembly assembly, IEnumerable<string> ownerTypeNames)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(ownerTypeNames);
        var owners = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in ownerTypeNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            owners.Add(name);
        }
        lock (IndexGate)
        {
            if (InitializerOwners.TryGetValue(assembly, out var previous) && previous.SetEquals(owners)) return;
            InitializerOwners.Remove(assembly);
            InitializerOwners.Add(assembly, owners);
            if (!Assemblies.ContainsKey(assembly)) return;
            // Late registration must not leave the previously reflected initializer list
            // active. Generated hosts normally register before their first Warmup.
            var index = Scan(assembly);
            Assemblies[assembly] = index;
            _assembliesScanned++;
            _typesScanned += index.Types.Length;
            RebuildIndexes();
        }
    }

    public static void RegisterAssemblies(IEnumerable<Assembly> assemblies)
    {
        lock (IndexGate)
        {
            ArgumentNullException.ThrowIfNull(assemblies);
            _initialized = true;
            var changed = false;
            var candidates = new Queue<Assembly>(assemblies.Distinct());
            var inspected = new HashSet<Assembly>();
            while (true)
            {
                while (candidates.TryDequeue(out var assembly))
                {
                    if (!inspected.Add(assembly) || !ReferencesCore(assembly)) continue;
                    // Dependency loads can leave an early scan incomplete. A later registration
                    // or domain preference must retry it while complete indexes remain reusable.
                    if (Assemblies.TryGetValue(assembly, out var existing) && existing.IsComplete) continue;
                    var index = Scan(assembly);
                    Assemblies[assembly] = index;
                    _assembliesScanned++;
                    _typesScanned += index.Types.Length;
                    changed = true;
                }
                lock (PendingAssemblyGate)
                {
                    if (PendingAssemblies.Count == 0) break;
                    while (PendingAssemblies.TryDequeue(out var pending)) candidates.Enqueue(pending);
                }
            }
            if (changed) RebuildIndexes();
        }
    }

    public static void UnregisterAssemblies(IEnumerable<Assembly> assemblies)
    {
        lock (IndexGate)
        {
            ArgumentNullException.ThrowIfNull(assemblies);
            var removed = false;
            foreach (var assembly in assemblies)
            {
                InitializerOwners.Remove(assembly);
                removed |= Assemblies.Remove(assembly);
            }
            if (removed) RebuildIndexes();
        }
    }

    /// <summary>
    /// Temporarily makes the supplied assemblies win full-name collisions in reflection and serialization lookups.
    /// This is used by an isolated gameplay domain while edit-time assemblies remain loaded for the editor UI.
    /// </summary>
    public static IDisposable PreferAssemblies(IEnumerable<Assembly> assemblies)
    {
        lock (IndexGate)
        {
            ArgumentNullException.ThrowIfNull(assemblies);
            var preferred = assemblies.Distinct().ToArray();
            if (preferred.Length == 0) return EmptyPreference.Instance;
            RegisterAssemblies(preferred);
            var preference = new AssemblyPreference(preferred);
            AssemblyPreferences.Add(preference);
            RebuildIndexes();
            return preference;
        }
    }

    public static Type? FindType(string fullName)
    {
        lock (IndexGate)
        {
            if (string.IsNullOrWhiteSpace(fullName)) return null;
            EnsureInitialized();
            return TypesByName.GetValueOrDefault(fullName);
        }
    }

    /// <summary>Resolve persisted names against the active domain, including generic arguments and arrays.</summary>
    internal static Type? ResolveSerializedType(string name)
    {
        lock (IndexGate)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            EnsureInitialized();
            if (SerializedTypes.TryGetValue(name, out var cached)) return cached;
            var depth = 0; var end = name.Length;
            for (var index = 0; index < name.Length; index++)
            {
                if (name[index] == '[') depth++;
                else if (name[index] == ']') depth--;
                else if (name[index] == ',' && depth == 0) { end = index; break; }
            }
            // Preserve full-name migration for ordinary types moved to another assembly.
            if (TypesByName.TryGetValue(name[..end].Trim(), out var preferred))
            { SerializedTypes[name] = preferred; return preferred; }
            // Type.GetType's parser understands nested assembly-qualified generic arguments. Splitting
            // at the first comma truncates List<T> and the default binder cannot see collectible domains.
            var assemblies = RuntimeServiceAccess.PlatformService.GetLoadedAssemblies().ToArray();
            Type? Lookup(Assembly? assembly, string typeName, bool ignoreCase)
            {
                if (TypesByName.TryGetValue(typeName, out var active)) return active;
                if (assembly is not null) return assembly.GetType(typeName, false, ignoreCase);
                foreach (var candidate in assemblies)
                    if (candidate.GetType(typeName, false, ignoreCase) is { } found) return found;
                return null;
            }
            Type? type;
            try
            {
                type = Type.GetType(name,
                    requested => assemblies.FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, requested.Name, StringComparison.OrdinalIgnoreCase)),
                    Lookup, throwOnError: false);
            }
            catch (Exception error) when (error is ArgumentException or TypeLoadException or FileNotFoundException or FileLoadException or NotSupportedException)
            { return null; }
            if (type is not null) SerializedTypes[name] = type;
            return type;
        }
    }

    public static Type[] GetTypesDerivedFrom<T>() => GetTypesDerivedFrom(typeof(T));

    public static Type[] GetTypesDerivedFrom(Type parentType)
    {
        lock (IndexGate)
        {
            ArgumentNullException.ThrowIfNull(parentType);
            EnsureInitialized();
            if (DerivedTypes.TryGetValue(parentType, out var cached)) return cached;
            cached = _allTypes.Where(type => type != parentType && parentType.IsAssignableFrom(type)).ToArray();
            DerivedTypes[parentType] = cached;
            return cached;
        }
    }

    public static bool TryCreateInstance(Type type, out object instance)
    {
        ArgumentNullException.ThrowIfNull(type);
        var factory = GetFactory(type);
        if (factory is null) { instance = null!; return false; }
        // Constructors can perform resource work. Never run them under the index lock.
        instance = factory();
        return true;
    }

    public static Type[] GetAllTypes()
    {
        lock (IndexGate)
        {
            EnsureInitialized();
            return _allTypes;
        }
    }
    public static Type[] GetTypes(Assembly assembly)
    {
        lock (IndexGate)
        {
            EnsureInitialized();
            return Assemblies.TryGetValue(assembly, out var index) ? index.Types : [];
        }
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

    internal static bool HasMessage(Type targetType, string methodName, bool hasArgument)
    {
        lock (IndexGate)
        {
            EnsureInitialized();
            EnsureMessageHandlers(targetType);
            if (MessageHandlers.TryGetValue(new RuntimeMessageKey(targetType, methodName), out var handlers))
                foreach (var handler in handlers) if (handler.HasArgument == hasArgument) return true;
            return false;
        }
    }

    /// <summary>Checks for a compatible cached message before constructing its argument.
    /// A null argument type selects a zero-argument message.</summary>
    public static bool CanInvokeMessage(Type targetType, string methodName, Type? argumentType = null)
    {
        lock (IndexGate)
        {
            ArgumentNullException.ThrowIfNull(targetType);
            ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
            EnsureInitialized();
            EnsureMessageHandlers(targetType);
            if (MessageHandlers.TryGetValue(new RuntimeMessageKey(targetType, methodName), out var handlers))
                foreach (var handler in handlers)
                    if (argumentType is null ? !handler.HasArgument :
                        handler.HasArgument && handler.ParameterType!.IsAssignableFrom(argumentType)) return true;
            return false;
        }
    }

    internal static ComponentReflectionInfo GetComponentInfo(Type type)
    {
        lock (IndexGate)
        {
            EnsureInitialized();
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
        lock (IndexGate)
        {
            EnsureInitialized();
            return InitializersByPhase.GetValueOrDefault(phase) ?? [];
        }
    }

    internal static MethodInfo[] GetMethods(Type type)
    {
        lock (IndexGate)
        {
            EnsureInitialized();
            if (MethodsByType.TryGetValue(type, out var cached)) return cached;
            if (!Assemblies.TryGetValue(type.Assembly, out var index) || !index.Types.Contains(type)) return [];
            cached = GetMethodsCore(type);
            index.MethodsByType[type] = cached;
            MethodsByType[type] = cached;
            return cached;
        }
    }

    internal static Func<object>? GetFactory(Type type)
    {
        lock (IndexGate)
        {
            EnsureInitialized();
            if (Factories.TryGetValue(type, out var factory)) return factory;
            factory = CreateFactory(type);
            if (factory is not null) Factories[type] = factory;
            return factory;
        }
    }

    internal static MemberInfo[] GetInstanceMembers(Type type)
    {
        lock (IndexGate)
        {
            EnsureInitialized();
            if (MembersByType.TryGetValue(type, out var cached)) return cached;
            // Inspectors and undo can still reference instances from a retired script assembly.
            if (!Assemblies.TryGetValue(type.Assembly, out var index) || !index.Types.Contains(type))
                return DetachedMembers.GetValue(type, GetInstanceMembersCore);
            cached = GetInstanceMembersCore(type);
            index.MembersByType[type] = cached;
            MembersByType[type] = cached;
            foreach (var member in cached) MembersByName.TryAdd(new MemberLookupKey(type, member.Name), member);
            return cached;
        }
    }

    internal static bool IsSerializableMember(MemberInfo member)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (member.IsDefined(typeof(YamlDotNet.Serialization.YamlIgnoreAttribute), inherit: true)) return false;
        return member switch
        {
            FieldInfo field => !field.IsStatic && !field.IsInitOnly &&
                               !IsPublicPropertyBackingField(field) &&
                               !field.IsDefined(typeof(NonSerializedAttribute), inherit: true) &&
                               (!field.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute),
                                    inherit: true) ||
                                field.IsDefined(typeof(SerializeFieldAttribute), inherit: true) ||
                                field.IsDefined(typeof(SerializeReferenceAttribute), inherit: true)) &&
                               !typeof(Delegate).IsAssignableFrom(field.FieldType) &&
                               (field.IsPublic || field.IsDefined(typeof(SerializeFieldAttribute), inherit: true) ||
                                field.IsDefined(typeof(SerializeReferenceAttribute), inherit: true)),
            PropertyInfo property => property.GetIndexParameters().Length == 0 &&
                                     property.GetMethod is { IsPublic: true } &&
                                     property.SetMethod is { IsPublic: true } &&
                                     !typeof(Delegate).IsAssignableFrom(property.PropertyType),
            _ => false
        };
    }

    private static bool IsPublicPropertyBackingField(FieldInfo field)
    {
        if (!field.Name.StartsWith('<') || !field.Name.EndsWith(">k__BackingField", StringComparison.Ordinal)) return false;
        var name = field.Name[1..field.Name.IndexOf('>')];
        var property = field.DeclaringType?.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return property is not null && IsSerializableMember(property);
    }

    public static bool TryFindInstanceMember(Type type, string name, out MemberInfo member)
    {
        lock (IndexGate)
        {
            EnsureInitialized();
            if (MembersByName.TryGetValue(new MemberLookupKey(type, name), out member!)) return true;
            member = GetInstanceMembers(type).FirstOrDefault(candidate => candidate.Name == name)!;
            return member is not null;
        }
    }

    public static RuntimeMemberAccessor GetMemberAccessor(MemberInfo member)
    {
        lock (IndexGate)
        {
            EnsureInitialized();
            if (MemberAccessors.TryGetValue(member, out var accessor)) return accessor;
            accessor = CreateMemberAccessor(member);
            MemberAccessors[member] = accessor;
            return accessor;
        }
    }

    internal static Type[] GetRuntimeSystemTypes()
    {
        lock (IndexGate)
        {
            EnsureInitialized();
            return _runtimeSystemTypes;
        }
    }

    private static void EnsureInitialized()
    {
        if (!_initialized) Warmup();
        else
        {
            // Ordinary cache reads need no discovery work unless an assembly-load
            // notification queued a new assembly. Drain outside the event-hook lock.
            lock (PendingAssemblyGate) if (PendingAssemblies.Count == 0) return;
            RegisterAssemblies([]);
        }
    }

    private static AssemblyIndex Scan(Assembly assembly)
    {
        var types = GetLoadableTypes(assembly, out var isComplete);
        var initializers = new List<RuntimeInitializationEntry>();
        var methodsByType = new Dictionary<Type, MethodInfo[]>(types.Length);
        var membersByType = new Dictionary<Type, MemberInfo[]>(types.Length);
        InitializerOwners.TryGetValue(assembly, out var owners);
        foreach (var type in types)
        {
            if (owners is not null && (type.FullName is not { } ownerName || !owners.Contains(ownerName))) continue;
            // Global discovery only needs declared static initialization methods.
            // Full member graphs, accessors and component metadata are built on
            // first use, instead of reflecting every unused type at startup.
            MethodInfo[] methods;
            try { methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public |
                                           BindingFlags.NonPublic | BindingFlags.DeclaredOnly); }
            catch { continue; }
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
        return new AssemblyIndex(types, initializers.ToArray(), methodsByType, membersByType, isComplete);
    }

    private static void RebuildIndexes()
    {
        if (_rebuildingIndexes)
        {
            _rebuildRequested = true;
            return;
        }

        _rebuildingIndexes = true;
        try
        {
            do
            {
                _rebuildRequested = false;
                RebuildIndexesCore(GetOrderedAssemblyIndexes());
            } while (_rebuildRequested);
        }
        finally
        {
            _rebuildingIndexes = false;
        }
    }

    private static AssemblyIndex[] GetOrderedAssemblyIndexes()
    {
        if (AssemblyPreferences.Count == 0) return Assemblies.Values.ToArray();
        var ordered = new List<AssemblyIndex>(Assemblies.Count);
        var visited = new HashSet<Assembly>();
        for (var index = AssemblyPreferences.Count - 1; index >= 0; index--)
        foreach (var assembly in AssemblyPreferences[index].Assemblies)
        {
            if (!visited.Add(assembly) || !Assemblies.TryGetValue(assembly, out var assemblyIndex)) continue;
            ordered.Add(assemblyIndex);
        }
        foreach (var (assembly, assemblyIndex) in Assemblies)
            if (visited.Add(assembly)) ordered.Add(assemblyIndex);
        return ordered.ToArray();
    }

    private static void RebuildIndexesCore(AssemblyIndex[] assemblyIndexes)
    {
        // Nested domains can prefer the same assemblies without changing lookup precedence.
        // Keep the published index and its lazy metadata in that case. Compare index identities,
        // rather than assembly identities, so a retried scan or late initializer hint still
        // invalidates metadata even when the effective assembly order remains the same.
        if (_indexedAssemblyOrder is { } previous && previous.Length == assemblyIndexes.Length)
        {
            var unchanged = true;
            for (var index = 0; index < previous.Length; index++)
                if (!ReferenceEquals(previous[index], assemblyIndexes[index]))
                {
                    unchanged = false;
                    break;
                }
            if (unchanged) return;
        }
        // Publish the identity snapshot only after a complete rebuild. An exceptional or
        // reentrant scan must not make a later request reuse a partially rebuilt index.
        _indexedAssemblyOrder = null;
        TypesByName.Clear();
        SerializedTypes.Clear();
        DerivedTypes.Clear();
        Factories.Clear();
        Components.Clear();
        MethodsByType.Clear();
        MembersByType.Clear();
        MembersByName.Clear();
        MemberAccessors.Clear();
        MessageHandlers.Clear();
        MessageTypes.Clear();
        RuntimeInitializers.Clear();
        InitializersByPhase.Clear();
        var scannedTypes = assemblyIndexes.SelectMany(index => index.Types).Distinct().ToArray();
        foreach (var index in assemblyIndexes)
        {
            RuntimeInitializers.AddRange(index.Initializers);
            foreach (var pair in index.MethodsByType) MethodsByType[pair.Key] = pair.Value;
            foreach (var pair in index.MembersByType)
            {
                MembersByType[pair.Key] = pair.Value;
                foreach (var member in pair.Value)
                {
                    MembersByName.TryAdd(new MemberLookupKey(pair.Key, member.Name), member);
                }
            }
            foreach (var type in index.Types)
            {
                if (type.FullName is { } fullName) TypesByName.TryAdd(fullName, type);
                if (type.AssemblyQualifiedName is { } qualified) TypesByName.TryAdd(qualified, type);
            }
        }
        _allTypes = scannedTypes.Where(type => type.FullName is not { } fullName ||
                                               ReferenceEquals(TypesByName.GetValueOrDefault(fullName), type))
            .ToArray();
        RuntimeInitializers.RemoveAll(initializer =>
        {
            var owner = initializer.Callback.Method.DeclaringType;
            return owner?.FullName is { } fullName &&
                   !ReferenceEquals(TypesByName.GetValueOrDefault(fullName), owner);
        });
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
        _indexedAssemblyOrder = assemblyIndexes;
    }

    private static bool TryInvokeMessageCore(
        MonoBehaviour target,
        string methodName,
        object? argument,
        bool hasArgument)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        RuntimeMessageHandler[]? handlers;
        lock (IndexGate)
        {
            EnsureInitialized();
            EnsureMessageHandlers(target.GetType());
            if (!MessageHandlers.TryGetValue(new RuntimeMessageKey(target.GetType(), methodName), out handlers))
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

    private static void EnsureMessageHandlers(Type behaviourType)
    {
        if (MessageTypes.Contains(behaviourType) || behaviourType.IsAbstract ||
            !typeof(MonoBehaviour).IsAssignableFrom(behaviourType) ||
            !Assemblies.TryGetValue(behaviourType.Assembly, out var index) || !index.Types.Contains(behaviourType)) return;
        CacheMessageHandlers(behaviourType);
        MessageTypes.Add(behaviourType);
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
    }

    private static IEnumerable<MethodInfo> EnumerateHierarchyMethods(Type type)
    {
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
            foreach (var method in GetMethods(current)) yield return method;
    }

    private static RuntimeMessageHandler CreateMessageHandler(MethodInfo method, ParameterInfo[] parameters)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported) return default;
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
        if (declaringType.IsValueType || !RuntimeFeature.IsDynamicCodeSupported)
            return CreateReflectedMemberAccessor(member, valueType);
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
            return CreateReflectedMemberAccessor(member, valueType);
        }
    }

    private static RuntimeMemberAccessor CreateReflectedMemberAccessor(MemberInfo member, Type valueType) => member switch
    {
        FieldInfo field => new RuntimeMemberAccessor(valueType, field.GetValue,
            field.IsInitOnly ? null : field.SetValue),
        PropertyInfo property => new RuntimeMemberAccessor(valueType, property.GetValue,
            property.SetMethod is null ? null : property.SetValue),
        _ => default
    };

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
        var fields = type.GetFields(flags).Where(field => IsSerializableMember(field));
        var properties = type.GetProperties(flags).Where(property => IsSerializableMember(property) &&
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
        if (!RuntimeFeature.IsDynamicCodeSupported) return () => constructor.Invoke(null);
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

    private static Type[] GetLoadableTypes(Assembly assembly, out bool isComplete)
    {
        try
        {
            var types = assembly.GetTypes();
            isComplete = true;
            return types;
        }
        catch (ReflectionTypeLoadException exception)
        {
            isComplete = false;
            return exception.Types.Where(type => type is not null).Cast<Type>().ToArray();
        }
        catch { isComplete = false; return []; }
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
        Dictionary<Type, MemberInfo[]> MembersByType,
        bool IsComplete);

    private sealed class AssemblyPreference(Assembly[] assemblies) : IDisposable
    {
        private bool _disposed;

        internal Assembly[] Assemblies { get; } = assemblies;

        public void Dispose()
        {
            lock (IndexGate)
            {
                if (_disposed) return;
                _disposed = true;
                if (AssemblyPreferences.Remove(this)) RebuildIndexes();
            }
        }
    }

    private sealed class EmptyPreference : IDisposable
    {
        internal static readonly EmptyPreference Instance = new();
        public void Dispose() { }
    }

    private readonly record struct RuntimeMessageKey(Type BehaviourType, string MethodName);
    private readonly record struct MemberLookupKey(Type OwnerType, string Name);
    private readonly record struct RuntimeMessageHandler(
        bool HasArgument,
        Type? ParameterType,
        Action<object, object?> Callback);
}
