using System.Globalization;
using System.Reflection;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace BEngine.Serialization;

public static class ComponentFieldSerializer
{
    private const string AssetTypeSuffix = ".$type";
    // Views borrow the immutable metadata arrays. Weak keys let retired domain metadata unload.
    private static readonly ConditionalWeakTable<MemberInfo[], ReadOnlyCollection<MemberInfo>> MemberViews = new();


    public static Dictionary<string, string> Serialize(BEngine.Component component)
    {
        if (component is BEngine.MissingComponent missing)
        {
            return new Dictionary<string, string>(missing.serializedFields, StringComparer.Ordinal);
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var member in GetSerializableMembers(component.GetType()))
        {
            var value = GetMemberValue(member, component);
            if (TryFormat(value, GetMemberType(member), out var text))
            {
                result[member.Name] = text;
                if (value is BEngine.BObject && RuntimeObjectCodecs.IsReferenceType(value.GetType()) && value.GetType() != GetMemberType(member))
                    result[member.Name + AssetTypeSuffix] = value.GetType().AssemblyQualifiedName ??
                                                            value.GetType().FullName ?? value.GetType().Name;
            }
        }

        return result;
    }

    public static void Deserialize(BEngine.Component component, IReadOnlyDictionary<string, string> fields)
    {
        if (component is BEngine.MissingComponent missing)
        {
            missing.serializedFields = new Dictionary<string, string>(fields, StringComparer.Ordinal);
            return;
        }
        foreach (var member in GetSerializableMembers(component.GetType()))
        {
            if (!TryGetSerializedValue(member, fields, out var text))
            {
                continue;
            }

            var memberType = GetMemberType(member);
            try
            {
                var assetType = IsAssetReferenceType(memberType)
                    ? ResolveAssetType(member, fields, memberType)
                    : memberType;
                SetMemberValue(member, component, RuntimeObjectCodecs.Find(assetType) is { } codec ? codec.ReadReference(text, fields) : Parse(text, assetType));
            }
            catch (Exception exception) when (IsRecoverableReadException(exception))
            {
                BEngine.Debug.LogWarning($"Could not restore {component.GetType().Name}.{member.Name}: {exception.Message}");
            }
        }
    }

    private static bool TryGetSerializedValue(
        MemberInfo member, IReadOnlyDictionary<string, string> fields, out string text)
    {
        if (fields.TryGetValue(member.Name, out text!)) return true;
        foreach (var alias in member.GetCustomAttributes<BEngine.FormerlySerializedAsAttribute>(inherit: true))
            if (fields.TryGetValue(alias.oldName, out text!))
                return true;
        text = string.Empty;
        return false;
    }

    public static IReadOnlyList<MemberInfo> GetSerializableMembers(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return MemberViews.GetValue(BEngine.RuntimeTypeCache.GetSerializableMembers(type, inspectorOnly: false),
            static members => Array.AsReadOnly(members));
    }

    public static IReadOnlyList<MemberInfo> GetInspectorMembers(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return MemberViews.GetValue(BEngine.RuntimeTypeCache.GetSerializableMembers(type, inspectorOnly: true),
            static members => Array.AsReadOnly(members));
    }

    public static Type GetMemberType(MemberInfo member) =>
        BEngine.RuntimeTypeCache.GetMemberAccessor(member).ValueType;

    public static object? GetMemberValue(MemberInfo member, object target) =>
        BEngine.RuntimeTypeCache.GetMemberAccessor(member).Getter(target);

    public static void SetMemberValue(MemberInfo member, object target, object? value)
    {
        var accessor = BEngine.RuntimeTypeCache.GetMemberAccessor(member);
        if (accessor.Setter is null) throw new InvalidOperationException($"{member.Name} is read-only.");
        accessor.Setter(target, value);
    }

    private static bool TryFormat(object? value, Type type, out string text)
    {
        text = string.Empty;
        if (value is null)
        {
            return type == typeof(string);
        }

        if (type == typeof(string)) text = (string)value;
        else if (typeof(BEngine.BAsset).IsAssignableFrom(type))
        {
            var asset = (BEngine.BAsset)value;
            text = asset.parentAssetGuid.HasValue && asset.localIdentifier > 0
                ? $"guid:{asset.parentAssetGuid.Value:N}#subasset={asset.localIdentifier.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
                : asset.assetPath;
            if (string.IsNullOrWhiteSpace(text)) return false;
        }
        else if (value is BObject obj && RuntimeObjectCodecs.Find(type) is { } codec) text = codec.WriteReference(obj);
        else if (type == typeof(bool)) text = ((bool)value) ? "true" : "false";
        else if (type == typeof(int)) text = ((int)value).ToString(CultureInfo.InvariantCulture);
        else if (type == typeof(long)) text = ((long)value).ToString(CultureInfo.InvariantCulture);
        else if (type == typeof(ulong)) text = ((ulong)value).ToString(CultureInfo.InvariantCulture);
        else if (type == typeof(Guid)) text = ((Guid)value).ToString("D");
        else if (type == typeof(BEngine.Fix64)) text = ((BEngine.Fix64)value).ToString();
        else if (type == typeof(BEngine.LayerMask)) text = ((BEngine.LayerMask)value).value.ToString(CultureInfo.InvariantCulture);
        else if (type == typeof(BEngine.Vector2)) text = Format((BEngine.Vector2)value);
        else if (type == typeof(BEngine.Vector4)) text = Format((BEngine.Vector4)value);
        else if (type == typeof(BEngine.Color)) text = Format((BEngine.Color)value);
        else if (type == typeof(BEngine.Rect)) text = Format((BEngine.Rect)value);
        else if (type.IsEnum) text = value.ToString() ?? string.Empty;
        else return false;
        return true;
    }

    private static object? Parse(string value, Type type)
    {
        if (type == typeof(string)) return value;
        if (typeof(BEngine.BAsset).IsAssignableFrom(type))
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return BEngine.BAsset.Load(value, type) ??
                   throw new InvalidDataException($"Asset reference '{value}' could not be loaded.");
        }
        if (RuntimeObjectCodecs.Find(type) is { } codec) return codec.ReadReference(value);
        if (type == typeof(bool)) return bool.Parse(value);
        if (type == typeof(int)) return int.Parse(value, CultureInfo.InvariantCulture);
        if (type == typeof(long)) return long.Parse(value, CultureInfo.InvariantCulture);
        if (type == typeof(ulong)) return ulong.Parse(value, CultureInfo.InvariantCulture);
        if (type == typeof(Guid)) return Guid.Parse(value);
        if (type == typeof(BEngine.Fix64)) return BEngine.Fix64.Parse(value);
        if (type == typeof(BEngine.LayerMask)) return new BEngine.LayerMask(ulong.Parse(value, CultureInfo.InvariantCulture));
        if (type == typeof(BEngine.Vector2)) return ParseVector2(value);
        if (type == typeof(BEngine.Vector4)) return ParseVector4(value);
        if (type == typeof(BEngine.Color)) return ParseColor(value);
        if (type == typeof(BEngine.Rect)) return ParseRect(value);
        if (type.IsEnum) return Enum.Parse(type, value, ignoreCase: false);
        throw new NotSupportedException($"Serialized component field type {type.FullName} is not supported.");
    }

    private static Type ResolveAssetType(
        MemberInfo member, IReadOnlyDictionary<string, string> fields, Type declaredType)
    {
        string? typeName = null;
        if (!fields.TryGetValue(member.Name + AssetTypeSuffix, out typeName))
        {
            foreach (var alias in member.GetCustomAttributes<BEngine.FormerlySerializedAsAttribute>(inherit: true))
                if (fields.TryGetValue(alias.oldName + AssetTypeSuffix, out typeName))
                    break;
        }
        if (string.IsNullOrWhiteSpace(typeName)) return declaredType;
        var resolved = BEngine.BAssetReferenceLoader.ResolveType(typeName) ??
                       throw new InvalidDataException($"Asset reference type '{typeName}' is not loaded.");
        if (!declaredType.IsAssignableFrom(resolved) || !IsAssetReferenceType(resolved) ||
            resolved.IsAbstract)
            throw new InvalidDataException(
                $"Asset reference type '{resolved.FullName}' is not assignable to {declaredType.FullName}.");
        return resolved;
    }

    private static bool IsAssetReferenceType(Type type) =>
        RuntimeObjectCodecs.IsReferenceType(type);

    internal static IEnumerable<string> EnumerateAssetReferences(Type type, IReadOnlyDictionary<string, string> fields)
    {
        foreach (var member in GetSerializableMembers(type))
            if (IsAssetReferenceType(GetMemberType(member)) && TryGetSerializedValue(member, fields, out var reference) &&
                !string.IsNullOrWhiteSpace(reference))
                yield return reference;
    }

    private static bool IsRecoverableReadException(Exception exception) => exception is IOException or
        UnauthorizedAccessException or InvalidDataException or FormatException or OverflowException or
        ArgumentException;

    private static string Format(BEngine.Vector2 value) => $"{value.x},{value.y}";
    private static string Format(BEngine.Vector4 value) => $"{value.x},{value.y},{value.z},{value.w}";
    private static string Format(BEngine.Color value) => $"{value.r},{value.g},{value.b},{value.a}";
    private static string Format(BEngine.Rect value) => $"{value.x},{value.y},{value.width},{value.height}";

    private static BEngine.Vector2 ParseVector2(string value)
    {
        Span<Fix64> parts = stackalloc Fix64[2];
        FixedScalarText.Parse(value, parts);
        return new(parts[0], parts[1]);
    }

    private static BEngine.Vector4 ParseVector4(string value)
    {
        Span<Fix64> parts = stackalloc Fix64[4];
        FixedScalarText.Parse(value, parts);
        return new(parts[0], parts[1],
            parts[2], parts[3]);
    }

    private static BEngine.Color ParseColor(string value)
    {
        Span<Fix64> parts = stackalloc Fix64[4];
        FixedScalarText.Parse(value, parts);
        return new(parts[0], parts[1],
            parts[2], parts[3]);
    }

    private static BEngine.Rect ParseRect(string value)
    {
        Span<Fix64> parts = stackalloc Fix64[4];
        FixedScalarText.Parse(value, parts);
        return new(parts[0], parts[1],
            parts[2], parts[3]);
    }

}
