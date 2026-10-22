namespace BEngine;

internal static class AnimationSampler
{
    public static void Sample(AnimationClip clip, GameObject root, Fix64 time) =>
        SampleWeighted(clip, root, time, Fix64.One);

    internal static void SampleWeighted(AnimationClip clip, GameObject root, Fix64 time, Fix64 weight,
        bool skipRootMotion = false)
    {
        foreach (var binding in clip.bindings)
        {
            if (skipRootMotion && IsRootMotion(binding)) continue;
            var accessor = AnimationAccessor.Resolve(root, binding);
            if (accessor is null) continue;
            accessor.Write(AnimationValue.Mix(accessor.Read(), binding.Evaluate(time, accessor.Kind), weight, accessor.Kind));
        }
    }

    internal static (Vector2 Position, Fix64 Rotation) RootMotion(
        AnimationClip clip, Fix64 previous, Fix64 current, bool loop)
    {
        var position = Vector2.zero;
        var rotation = Fix64.Zero;
        var length = clip.length;
        foreach (var binding in clip.bindings)
        {
            if (!IsRootMotion(binding)) continue;
            var delta = SampleRootMotion(binding, current, loop, length) - SampleRootMotion(binding, previous, loop, length);
            var property = binding.propertyName.Replace("m_Local", "local", StringComparison.OrdinalIgnoreCase);
            if (property.Equals("localRotation", StringComparison.OrdinalIgnoreCase)) rotation += delta;
            else if (property.EndsWith(".y", StringComparison.OrdinalIgnoreCase)) position += new Vector2(0, delta);
            else position += new Vector2(delta, 0);
        }
        return (position, rotation);
    }

    private static Fix64 SampleRootMotion(AnimationBinding binding, Fix64 time, bool loop, Fix64 length)
    {
        if (!loop || length <= 0) return binding.curve.Evaluate(Mathf.Clamp(time, 0, length));
        var cycles = Mathf.FloorToInt(time / length);
        return binding.curve.Evaluate(Mathf.Repeat(time, length)) +
            cycles * (binding.curve.Evaluate(length) - binding.curve.Evaluate(0));
    }

    private static bool IsRootMotion(AnimationBinding binding)
    {
        if (!string.IsNullOrEmpty(binding.relativePath) ||
            binding.componentType != typeof(Transform).FullName && binding.componentType != nameof(Transform)) return false;
        var property = binding.propertyName.Replace("m_Local", "local", StringComparison.OrdinalIgnoreCase);
        return property.StartsWith("localPosition", StringComparison.OrdinalIgnoreCase) ||
               property.Equals("localRotation", StringComparison.OrdinalIgnoreCase);
    }
}
