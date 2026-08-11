using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Serialization;
using BEngine.UIElements;

namespace BEngine.Editor;

public static class EditorUtility
{
    private static readonly Dictionary<Guid, int> DirtyObjects = [];

    public static void SetDirty(BObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        DirtyObjects[target.Id] = DirtyObjects.GetValueOrDefault(target.Id) + 1;
        if (target is GameObject or Component) EditorSceneManager.MarkSceneDirty();
    }

    public static bool IsDirty(BObject target) => target is not null && DirtyObjects.ContainsKey(target.Id);
    public static int GetDirtyCount(BObject target) => target is not null ? DirtyObjects.GetValueOrDefault(target.Id) : 0;
    public static void ClearDirty(BObject target)
    {
        if (target is not null) DirtyObjects.Remove(target.Id);
    }

    public static BObject? InstanceIDToObject(int instanceId) => BObject.FindObjectFromInstanceID(instanceId);
    public static bool IsPersistent(BObject target) => AssetDatabase.Contains(target);
    public static void CopySerialized(BObject source, BObject destination) => ObjectState.CopyValues(source, destination);
    public static void CopySerializedManagedFieldsOnly(BObject source, BObject destination) =>
        CopySerialized(source, destination);

    public static bool DisplayDialog(string title, string message, string ok)
    {
        Debug.Log($"{title}: {message}");
        return true;
    }

    public static bool DisplayDialog(string title, string message, string ok, string cancel) =>
        DisplayDialog(title, message, ok);

    public static int DisplayDialogComplex(string title, string message, string ok, string cancel, string alt)
    {
        DisplayDialog(title, message, ok);
        return 0;
    }

    public static void DisplayProgressBar(string title, string info, float progress) { }
    public static bool DisplayCancelableProgressBar(string title, string info, float progress) => false;
    public static void ClearProgressBar() { }
    public static void FocusProjectWindow() => EditorApplication.RepaintProjectWindow();
    public static void PingObject(BObject target) => Selection.activeObject = target;
    public static void PingObject(int instanceId)
    {
        if (InstanceIDToObject(instanceId) is { } target) PingObject(target);
    }

    public static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var suffix = 0;
        while (value >= 1024 && suffix < suffixes.Length - 1)
        {
            value /= 1024;
            suffix++;
        }
        return $"{value:0.##} {suffixes[suffix]}";
    }

    public static int NaturalCompare(string? left, string? right) =>
        StringComparer.CurrentCultureIgnoreCase.Compare(left, right);
}

public static class EditorPrefs
{
    private static readonly object Gate = new();
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BEngine", "EditorPrefs.yaml");
    private static Dictionary<string, string>? _values;

    public static bool HasKey(string key) => Values.ContainsKey(key);
    public static void DeleteKey(string key) { lock (Gate) { Values.Remove(key); Save(); } }
    public static void DeleteAll() { lock (Gate) { Values.Clear(); Save(); } }
    public static string GetString(string key, string defaultValue = "") => Values.GetValueOrDefault(key, defaultValue);
    public static int GetInt(string key, int defaultValue = 0) =>
        int.TryParse(GetString(key), out var value) ? value : defaultValue;
    public static float GetFloat(string key, float defaultValue = 0) =>
        float.TryParse(GetString(key), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : defaultValue;
    public static bool GetBool(string key, bool defaultValue = false) =>
        bool.TryParse(GetString(key), out var value) ? value : defaultValue;
    public static void SetString(string key, string value) { lock (Gate) { Values[key] = value; Save(); } }
    public static void SetInt(string key, int value) => SetString(key, value.ToString(
        System.Globalization.CultureInfo.InvariantCulture));
    public static void SetFloat(string key, float value) => SetString(key, value.ToString(
        System.Globalization.CultureInfo.InvariantCulture));
    public static void SetBool(string key, bool value) => SetString(key, value.ToString());

    private static Dictionary<string, string> Values
    {
        get
        {
            lock (Gate)
            {
                if (_values is not null) return _values;
                try
                {
                    _values = File.Exists(FilePath)
                        ? YamlUtility.Load<EditorPrefsDocument>(FilePath).Values
                        : new Dictionary<string, string>(StringComparer.Ordinal);
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException)
                {
                    Debug.LogWarning($"Could not load EditorPrefs: {exception.Message}");
                    _values = new Dictionary<string, string>(StringComparer.Ordinal);
                }
                return _values;
            }
        }
    }

    private static void Save() => YamlUtility.Save(new EditorPrefsDocument { Values = Values }, FilePath);

    private sealed class EditorPrefsDocument
    {
        public string Format { get; set; } = "BEngine.EditorPrefs";
        public int Version { get; set; } = 1;
        public Dictionary<string, string> Values { get; set; } = new(StringComparer.Ordinal);
    }
}

public static class SessionState
{
    private static readonly Dictionary<string, object> Values = new(StringComparer.Ordinal);

    public static void EraseString(string key) => Values.Remove(key);
    public static string GetString(string key, string defaultValue) => Values.TryGetValue(key, out var value)
        ? value?.ToString() ?? defaultValue : defaultValue;
    public static void SetString(string key, string value) => Values[key] = value;
    public static int GetInt(string key, int defaultValue) => Values.TryGetValue(key, out var value) && value is int typed
        ? typed : defaultValue;
    public static void SetInt(string key, int value) => Values[key] = value;
    public static float GetFloat(string key, float defaultValue) =>
        Values.TryGetValue(key, out var value) && value is float typed ? typed : defaultValue;
    public static void SetFloat(string key, float value) => Values[key] = value;
    public static bool GetBool(string key, bool defaultValue) =>
        Values.TryGetValue(key, out var value) && value is bool typed ? typed : defaultValue;
    public static void SetBool(string key, bool value) => Values[key] = value;
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class InitializeOnLoadAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method)]
public sealed class InitializeOnLoadMethodAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method)]
public sealed class DidReloadScriptsAttribute(int callbackOrder = 0) : Attribute
{
    public int callbackOrder { get; } = callbackOrder;
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CustomPropertyDrawerAttribute(Type type, bool useForChildren = false) : Attribute
{
    public Type type { get; } = type;
    public bool useForChildren { get; } = useForChildren;
}

public abstract class PropertyDrawer
{
    public PropertyAttribute? attribute { get; internal set; }
    public FieldInfo? fieldInfo { get; internal set; }
    public MemberInfo? memberInfo { get; internal set; }
    public abstract VisualElement CreatePropertyGUI(SerializedProperty property);
    public virtual bool CanCacheInspectorGUI(SerializedProperty property) => true;
}

[Flags]
public enum GizmoType
{
    Pickable = 1,
    NotInSelectionHierarchy = 2,
    NonSelected = 4,
    Selected = 8,
    Active = 16,
    InSelectionHierarchy = 32
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class DrawGizmoAttribute(GizmoType gizmo) : Attribute
{
    public GizmoType drawOptions { get; } = gizmo;
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class OnOpenAssetAttribute(int callbackOrder = 0) : Attribute
{
    public int callbackOrder { get; } = callbackOrder;
}

public static class TypeCache
{
    public static Type[] GetTypesDerivedFrom<T>() => GetTypesDerivedFrom(typeof(T));
    public static Type[] GetTypesDerivedFrom(Type parentType) => GetAllTypes()
        .Where(type => type != parentType && parentType.IsAssignableFrom(type))
        .ToArray();
    public static Type[] GetTypesWithAttribute<T>() where T : Attribute => GetAllTypes()
        .Where(type => type.GetCustomAttribute<T>() is not null)
        .ToArray();
    public static MethodInfo[] GetMethodsWithAttribute<T>() where T : Attribute => GetAllTypes()
        .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Instance |
                                            BindingFlags.Public | BindingFlags.NonPublic))
        .Where(method => method.GetCustomAttribute<T>() is not null)
        .ToArray();

    internal static Type[] GetAllTypes() => AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(GetLoadableTypes)
        .ToArray();

    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type is not null).Cast<Type>().ToArray();
        }
    }
}

internal static class EditorInitialization
{
    private static readonly HashSet<string> Invoked = new(StringComparer.Ordinal);

    internal static void Run()
    {
        foreach (var type in TypeCache.GetTypesWithAttribute<InitializeOnLoadAttribute>())
        {
            RuntimeHelpers.RunClassConstructor(type.TypeHandle);
        }

        var methods = TypeCache.GetMethodsWithAttribute<InitializeOnLoadMethodAttribute>()
            .Concat(TypeCache.GetMethodsWithAttribute<DidReloadScriptsAttribute>()
                .OrderBy(method => method.GetCustomAttribute<DidReloadScriptsAttribute>()!.callbackOrder));
        foreach (var method in methods)
        {
            var key = $"{method.Module.ModuleVersionId:N}:{method.MetadataToken}";
            if (!Invoked.Add(key)) continue;
            if (!method.IsStatic || method.ReturnType != typeof(void) || method.GetParameters().Length != 0)
            {
                Debug.LogWarning($"Editor initialization method must be static void with no parameters: " +
                                 $"{method.DeclaringType?.FullName}.{method.Name}");
                continue;
            }
            try { method.Invoke(null, null); }
            catch (Exception exception)
            {
                var cause = exception is TargetInvocationException { InnerException: not null }
                    ? exception.InnerException : exception;
                Debug.LogError($"Editor initialization failed: {cause.Message}");
            }
        }
    }
}
