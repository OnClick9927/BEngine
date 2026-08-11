using System.Reflection;

namespace BEngine.Animation;

internal static class AnimationSampler
{
    public static void Sample(AnimationClip clip, GameObject root, Fix64 time)
    {
        foreach (var binding in clip.bindings)
        {
            var target = Find(root, binding.relativePath);
            if (target is null) continue;
            var value = binding.curve.Evaluate(time);
            if (binding.componentType == typeof(Transform).FullName || binding.componentType == nameof(Transform))
            {
                ApplyTransform(target.transform, binding.propertyName, value);
                continue;
            }
            var component = target.components.FirstOrDefault(item => item.GetType().FullName == binding.componentType || item.GetType().Name == binding.componentType);
            if (component is not null) ApplyMember(component, binding.propertyName, value);
        }
    }

    private static GameObject? Find(GameObject root, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return root;
        var current = root.transform;
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = current.children.FirstOrDefault(item => item.gameObject.name == part);
            if (current is null) return null;
        }
        return current.gameObject;
    }

    private static void ApplyTransform(Transform transform, string property, Fix64 value)
    {
        var normalized = property.Replace("m_Local", "local", StringComparison.OrdinalIgnoreCase);
        if (normalized.StartsWith("localPosition", StringComparison.OrdinalIgnoreCase))
            transform.localPosition = SetAxis(transform.localPosition, normalized, value);
        else if (normalized.StartsWith("localEulerAngles", StringComparison.OrdinalIgnoreCase))
            transform.localEulerAngles = SetAxis(transform.localEulerAngles, normalized, value);
        else if (normalized.StartsWith("localScale", StringComparison.OrdinalIgnoreCase))
            transform.localScale = SetAxis(transform.localScale, normalized, value);
    }

    private static Vector3 SetAxis(Vector3 vector, string property, Fix64 value) => property.EndsWith(".x", StringComparison.OrdinalIgnoreCase)
        ? new Vector3(value, vector.y, vector.z) : property.EndsWith(".y", StringComparison.OrdinalIgnoreCase)
            ? new Vector3(vector.x, value, vector.z) : new Vector3(vector.x, vector.y, value);

    private static void ApplyMember(Component component, string propertyName, Fix64 value)
    {
        var property = component.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.CanWrite == true)
        {
            if (property.PropertyType == typeof(Fix64)) property.SetValue(component, value);
            else if (property.PropertyType == typeof(int)) property.SetValue(component, (int)value);
            return;
        }
        var field = component.GetType().GetField(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field?.FieldType == typeof(Fix64)) field.SetValue(component, value);
        else if (field?.FieldType == typeof(int)) field.SetValue(component, (int)value);
    }
}
