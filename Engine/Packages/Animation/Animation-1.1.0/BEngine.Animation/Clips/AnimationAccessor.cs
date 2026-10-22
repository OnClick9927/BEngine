using System.Runtime.CompilerServices;

namespace BEngine;

/// <summary>One binding implementation shared by sampling, pose mixing and editor recording.</summary>
internal sealed record AnimationAccessor(Component Component, string Property, Type ValueType,
    AnimationBindingKind Kind, Func<AnimationValue> Read, Action<AnimationValue> Write)
{
    private sealed class CachedBinding
    {
        internal AnimationAccessor? Accessor;
        internal string? Path, PropertySource;
        internal string? ComponentType;
        internal Scene? Scene;
        internal long Version;
        internal string[] Parts = [];
        internal string Property = string.Empty;
    }
    private static readonly ConditionalWeakTable<AnimationBinding, ConditionalWeakTable<GameObject, CachedBinding>> Cache = new();
    internal static AnimationAccessor? Resolve(GameObject root, AnimationBinding binding)
    {
        var cached = Cache.GetValue(binding, static _ => new()).GetValue(root, static _ => new());
        var scene = root.scene;
        if (scene is not null && ReferenceEquals(scene, cached.Scene) && cached.Version == scene.hierarchyLookupVersion &&
            cached.Path == binding.relativePath && cached.PropertySource == binding.propertyName && cached.ComponentType == binding.componentType &&
            cached.Accessor?.Component.isDestroyed != true) return cached.Accessor;
        cached.Scene = scene; cached.Version = scene?.hierarchyLookupVersion ?? -1;
        cached.ComponentType = binding.componentType;
        var previous = cached.Accessor; cached.Accessor = null;
        if (cached.Path != binding.relativePath)
        { cached.Path = binding.relativePath; cached.Parts = binding.relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries); }
        Transform? target = root.transform;
        foreach (var part in cached.Parts)
        {
            Transform? childTarget = null;
            var children = target.children;
            for (var i = 0; i < children.Count; i++)
                if (children[i].gameObject.name == part) { childTarget = children[i]; break; }
            if (childTarget is null) return null;
            target = childTarget;
        }
        Component? component = null;
        var components = target.gameObject.components;
        for (var i = 0; i < components.Count; i++)
        {
            var item = components[i]; var type = item.GetType();
            if (!item.isDestroyed && (type.FullName == binding.componentType || type.Name == binding.componentType))
            { component = item; break; }
        }
        if (component is null) return null;
        if (cached.PropertySource != binding.propertyName)
        { cached.PropertySource = binding.propertyName; cached.Property = binding.propertyName.Replace("m_Local", "local", StringComparison.OrdinalIgnoreCase); }
        var property = cached.Property;
        cached.Accessor = previous is { } accessor && ReferenceEquals(accessor.Component, component) && accessor.Property == property
            ? accessor : Create(component, property);
        return cached.Accessor;
    }

    internal static AnimationAccessor? Create(Component component, string property)
    {
        property = property.Replace("m_Local", "local", StringComparison.OrdinalIgnoreCase);
        var split = property.Split('.');
        // Common built-in Transform channels use typed delegates and avoid boxing Vector2/Fix64.
        // Derived components retain the generic accessor so hidden/custom members keep their semantics.
        if (component.GetType() == typeof(Transform))
        {
            var transform = (Transform)component;
            if (split.Length == 2 && split[1] is "x" or "y" && split[0] is "localPosition" or "localScale" or "position")
            {
                var x = split[1] == "x"; var memberName = split[0];
                Vector2 ReadVector() => memberName == "localPosition" ? transform.localPosition : memberName == "localScale" ? transform.localScale : transform.position;
                void WriteVector(Vector2 value)
                {
                    if (memberName == "localPosition") transform.localPosition = value;
                    else if (memberName == "localScale") transform.localScale = value;
                    else transform.position = value;
                }
                return Numeric(() => { var v = ReadVector(); return x ? v.x : v.y; },
                    value => { var v = ReadVector(); WriteVector(x ? new(value, v.y) : new(v.x, value)); });
            }
            if (property == "localRotation") return Numeric(() => transform.localRotation, value => transform.localRotation = value);
            if (property == "rotation") return Numeric(() => transform.rotation, value => transform.rotation = value);
        }
        if (split.Length > 2 || !RuntimeTypeCache.TryFindInstanceMember(component.GetType(), split[0], out var member)) return null;
        var accessor = RuntimeTypeCache.GetMemberAccessor(member);
        if (accessor.Getter is null || accessor.Setter is null) return null;
        object? Get() => accessor.Getter(component);
        void Set(object? value) => accessor.Setter(component, value);
        var type = accessor.ValueType;
        if (split.Length == 2)
        {
            var axis = split[1];
            if (type == typeof(Vector2) && axis is "x" or "y")
                return Numeric(() => axis == "x" ? ((Vector2)Get()!).x : ((Vector2)Get()!).y,
                    value => { var v = (Vector2)Get()!; Set(axis == "x" ? new Vector2(value, v.y) : new Vector2(v.x, value)); });
            if (type == typeof(Color) && axis is "r" or "g" or "b" or "a")
                return Numeric(() => { var c = (Color)Get()!; return axis switch { "r" => c.r, "g" => c.g, "b" => c.b, _ => c.a }; },
                    value => { var c = (Color)Get()!; Set(new Color(axis == "r" ? value : c.r, axis == "g" ? value : c.g, axis == "b" ? value : c.b, axis == "a" ? value : c.a)); });
            return null;
        }
        if (type == typeof(Fix64)) return Numeric(() => (Fix64)Get()!, value => Set(value));
        if (type == typeof(int)) return Numeric(() => (int)Get()!, value => Set(Mathf.RoundToInt(value)));
        if (type == typeof(bool)) return new(component, property, type, AnimationBindingKind.Boolean,
            () => new((bool)Get()! ? 1 : 0), value => Set(value.Number != 0));
        if (type == typeof(BObject) || typeof(BAsset).IsAssignableFrom(type) || type == typeof(Sprite))
            return new(component, property, type, AnimationBindingKind.ObjectReference, () => new(0, (BObject?)Get()),
                value => { if (value.Reference is null || type.IsInstanceOfType(value.Reference)) Set(value.Reference); });
        return null;

        AnimationAccessor Numeric(Func<Fix64> read, Action<Fix64> write) =>
            new(component, property, typeof(Fix64), AnimationBindingKind.Numeric, () => new(read()), value => write(value.Number));
    }
}
