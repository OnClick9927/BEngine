using BEngine.Serialization;


using System.Runtime.CompilerServices;

namespace BEngine.Editor;

internal static class SceneGizmoVisibility
{
    internal const string EnabledPreferenceKey = "BEngine.SceneView.Gizmos.Enabled";
    internal const string DisabledTypesPreferenceKey = "BEngine.SceneView.Gizmos.DisabledTypes";

    private static readonly Lock Gate = new();
    private static readonly ConditionalWeakTable<Type, TypeIdentity> TypeIds = new();
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
        var typeIds = ((((global::System.Collections.Generic.IEnumerable<string>)(componentTypes.Select(TypeId))), (global::System.Collections.Generic.IEqualityComparer<string>? )(StringComparer.Ordinal)) switch
        {
            (string[] { Length: 0 }, _) => global::System.Array.Empty<string>(),
            var (distinctItems1569, distinctComparer1569) => distinctItems1569.ToHashSet<string>(distinctComparer1569).ToArray()});
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
        return TypeIds.GetValue(componentType, static type =>
        {
            var assemblyName = type.Assembly.GetName().Name ?? type.Assembly.FullName ?? "Unknown";
            return new TypeIdentity($"{assemblyName}:{type.FullName ?? type.Name}");
        }).Value;
    }

    private sealed record TypeIdentity(string Value);

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
                var values = YamlUtility.Deserialize<string[]>(serialized) ?? [];
                return _disabledTypes = new HashSet<string>(
                    values.Where(value => !string.IsNullOrWhiteSpace(value)), StringComparer.Ordinal);
            }
            catch (InvalidDataException exception)
            {
                Debug.LogWarning($"Could not load Scene Gizmo visibility preferences: {exception.Message}");
                return _disabledTypes = new HashSet<string>(StringComparer.Ordinal);
            }
        }
    }

    private static void SaveDisabledTypes()
    {
        var serialized = YamlUtility.Serialize(
            (_disabledTypes ?? []).Order(StringComparer.Ordinal).ToArray());
        EditorPrefs.SetString(DisabledTypesPreferenceKey, serialized);
    }
}
