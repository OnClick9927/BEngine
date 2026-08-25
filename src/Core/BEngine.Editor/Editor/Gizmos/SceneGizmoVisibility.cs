using System.Text.Json;

namespace BEngine.Editor;

internal static class SceneGizmoVisibility
{
    internal const string EnabledPreferenceKey = "BEngine.SceneView.Gizmos.Enabled";
    internal const string DisabledTypesPreferenceKey = "BEngine.SceneView.Gizmos.DisabledTypes";

    private static readonly Lock Gate = new();
    private static HashSet<string>? _disabledTypes;

    internal static bool Enabled
    {
        get => EditorPrefs.GetBool(EnabledPreferenceKey, true);
        set
        {
            if (value == Enabled) return;
            EditorPrefs.SetBool(EnabledPreferenceKey, value);
            SceneView.RepaintAll();
        }
    }

    internal static bool IsVisible(Type componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        lock (Gate) return !DisabledTypes.Contains(TypeId(componentType));
    }

    internal static void SetVisible(Type componentType, bool visible)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        lock (Gate)
        {
            var changed = visible
                ? DisabledTypes.Remove(TypeId(componentType))
                : DisabledTypes.Add(TypeId(componentType));
            if (!changed) return;
            SaveDisabledTypes();
        }
        SceneView.RepaintAll();
    }

    internal static void SetAll(IEnumerable<Type> componentTypes, bool visible)
    {
        ArgumentNullException.ThrowIfNull(componentTypes);
        var typeIds = componentTypes.Select(TypeId).Distinct(StringComparer.Ordinal).ToArray();
        lock (Gate)
        {
            var changed = false;
            foreach (var typeId in typeIds)
                changed |= visible ? DisabledTypes.Remove(typeId) : DisabledTypes.Add(typeId);
            if (!changed) return;
            SaveDisabledTypes();
        }
        SceneView.RepaintAll();
    }

    internal static string TypeId(Type componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        var assemblyName = componentType.Assembly.GetName().Name ?? componentType.Assembly.FullName ?? "Unknown";
        return $"{assemblyName}:{componentType.FullName ?? componentType.Name}";
    }

    private static HashSet<string> DisabledTypes
    {
        get
        {
            if (_disabledTypes is not null) return _disabledTypes;
            var serialized = EditorPrefs.GetString(DisabledTypesPreferenceKey);
            if (string.IsNullOrWhiteSpace(serialized))
                return _disabledTypes = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                var values = JsonSerializer.Deserialize<string[]>(serialized) ?? [];
                return _disabledTypes = new HashSet<string>(
                    values.Where(value => !string.IsNullOrWhiteSpace(value)), StringComparer.Ordinal);
            }
            catch (JsonException exception)
            {
                Debug.LogWarning($"Could not load Scene Gizmo visibility preferences: {exception.Message}");
                return _disabledTypes = new HashSet<string>(StringComparer.Ordinal);
            }
        }
    }

    private static void SaveDisabledTypes()
    {
        var serialized = JsonSerializer.Serialize(
            (_disabledTypes ?? []).Order(StringComparer.Ordinal).ToArray());
        EditorPrefs.SetString(DisabledTypesPreferenceKey, serialized);
    }
}
