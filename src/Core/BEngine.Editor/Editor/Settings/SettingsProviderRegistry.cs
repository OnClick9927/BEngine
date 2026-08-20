using System.Reflection;
using System.Runtime.CompilerServices;

namespace BEngine.Editor;

public static class SettingsProviderRegistry
{
    private static int _version;
    private static readonly ConditionalWeakTable<Assembly, object> ExcludedAssemblies = new();
    private static SettingsProvider[] _userProviders = [];
    private static SettingsProvider[] _projectProviders = [];
    private static bool _dirty = true;
    public static int version => Volatile.Read(ref _version);
    public static event Action? changed;

    public static IReadOnlyList<SettingsProvider> GetProviders(SettingsScope scope)
    {
        lock (ExcludedAssemblies)
        {
            if (_dirty) Rebuild();
            return scope == SettingsScope.User ? _userProviders : _projectProviders;
        }
    }

    internal static void Warmup()
    {
        lock (ExcludedAssemblies)
        {
            Rebuild();
        }
    }

    private static SettingsProvider[] Discover(SettingsScope scope)
    {
        var providers = new Dictionary<string, SettingsProvider>(StringComparer.OrdinalIgnoreCase);
        var methods = TypeCache.GetMethodsWithAttribute<SettingsProviderAttribute>()
            .Concat(scope == SettingsScope.User ? TypeCache.GetMethodsWithAttribute<PreferenceItemAttribute>() : [])
            .Distinct().OrderBy(method => method.DeclaringType?.Assembly.GetName().Name,
                StringComparer.OrdinalIgnoreCase).ThenBy(method => method.MetadataToken);
        foreach (var method in methods)
        {
            var assembly = method.DeclaringType!.Assembly;
            if (ExcludedAssemblies.TryGetValue(assembly, out _)) continue;
            var feature = $"SettingsProvider attributes {method.DeclaringType?.FullName}.{method.Name}";
            EditorFeatureGuard.TryInvoke(feature,
                () => method.GetCustomAttribute<SettingsProviderAttribute>(), null, out var settingsAttribute);
            if (settingsAttribute is not null)
            {
                if (!TryCreateProvider(method, out var provider) || provider.scope != scope) continue;
                provider.sourceAssembly = assembly.GetName().Name ?? string.Empty;
                providers.TryAdd(provider.settingsPath, provider);
            }
            if (scope != SettingsScope.User) continue;
            EditorFeatureGuard.TryInvoke(feature,
                () => method.GetCustomAttribute<PreferenceItemAttribute>(), null, out var legacy);
            if (legacy is null) continue;
            if (!method.IsStatic || method.ReturnType != typeof(void) || method.GetParameters().Length != 0)
            {
                Debug.LogWarning($"Ignored invalid PreferenceItem method {method.DeclaringType?.FullName}.{method.Name}.");
                continue;
            }
            var legacyProvider = new SettingsProvider($"Preferences/{legacy.name}", SettingsScope.User)
            {
                label = legacy.name,
                sourceAssembly = assembly.GetName().Name ?? string.Empty,
                guiHandler = BindPreference(method)
            };
            providers.TryAdd(legacyProvider.settingsPath, legacyProvider);
        }
        return providers.Values.OrderBy(item => item.settingsPath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void Rebuild()
    {
        _userProviders = Discover(SettingsScope.User);
        _projectProviders = Discover(SettingsScope.Project);
        _dirty = false;
    }

    public static void Invalidate()
    {
        lock (ExcludedAssemblies) _dirty = true;
        Interlocked.Increment(ref _version);
        EditorCallbackDispatcher.Invoke(changed, nameof(changed));
    }

    internal static void ForgetAssemblies(IEnumerable<Assembly> assemblies)
    {
        foreach (var assembly in assemblies) ExcludedAssemblies.GetValue(assembly, _ => new object());
        Invalidate();
    }

    private static bool TryCreateProvider(MethodInfo method, out SettingsProvider provider)
    {
        provider = null!;
        if (!method.IsStatic || method.GetParameters().Length != 0 ||
            !typeof(SettingsProvider).IsAssignableFrom(method.ReturnType))
        {
            Debug.LogWarning($"Ignored invalid SettingsProvider factory {method.DeclaringType?.FullName}.{method.Name}.");
            return false;
        }
        var feature = $"SettingsProvider factory {method.DeclaringType?.FullName}.{method.Name}";
        if (!EditorFeatureGuard.TryInvoke(feature, () =>
            {
                var factory = (Func<SettingsProvider>)method.CreateDelegate(typeof(Func<SettingsProvider>));
                return factory() ?? throw new InvalidOperationException("Factory returned null.");
            }, null!, out provider)) return false;
        return true;
    }

    private static Action<string> BindPreference(MethodInfo method)
    {
        var callback = (Action)method.CreateDelegate(typeof(Action));
        return _ => callback();
    }
}
