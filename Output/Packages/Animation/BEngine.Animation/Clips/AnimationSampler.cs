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
        else if (normalized.Equals("localRotation", StringComparison.OrdinalIgnoreCase))
            transform.localRotation = value;
        else if (normalized.StartsWith("localScale", StringComparison.OrdinalIgnoreCase))
            transform.localScale = SetAxis(transform.localScale, normalized, value);
    }

    private static Vector2 SetAxis(Vector2 vector, string property, Fix64 value) =>
        property.EndsWith(".y", StringComparison.OrdinalIgnoreCase)
            ? new Vector2(vector.x, value)
            : new Vector2(value, vector.y);

    private static void ApplyMember(Component component, string propertyName, Fix64 value)
    {
        if (!RuntimeTypeCache.TryFindInstanceMember(component.GetType(), propertyName, out var member)) return;
        var accessor = RuntimeTypeCache.GetMemberAccessor(member);
        if (accessor.Setter is null) return;
        if (accessor.ValueType == typeof(Fix64)) accessor.Setter(component, value);
        else if (accessor.ValueType == typeof(int)) accessor.Setter(component, (int)value);
    }
}
