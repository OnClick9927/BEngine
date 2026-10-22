using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BEngine.Editor;

internal readonly struct PropertyAccessor
{
    private readonly object _owner;
    private readonly MemberInfo? _member;
    private readonly RuntimeMemberAccessor _accessor;
    private readonly IList? _list;
    private readonly int _index;
    private readonly Action? _writeBack;
    private readonly bool _ownerWritable;
    private readonly Func<object?>? _valueGetter;
    private readonly Action<object?>? _valueSetter;

    public MemberInfo? MemberInfo => _member;
    public bool CanWrite => _ownerWritable && (_member switch
    {
        FieldInfo or PropertyInfo => _accessor.CanWrite,
        _ => _list is not null && !_list.IsReadOnly
    });

    public Type ValueType => _member switch
    {
        FieldInfo or PropertyInfo => _accessor.ValueType,
        _ when _list is not null => _list.GetType().IsArray
            ? _list.GetType().GetElementType()!
            : _list.GetType().IsGenericType ? _list.GetType().GetGenericArguments()[0] : typeof(object),
        _ => typeof(object)
    };

    public PropertyAccessor(object owner, MemberInfo member, Action? writeBack = null,
        bool ownerWritable = true)
    {
        _owner = owner;
        _member = member;
        _accessor = RuntimeTypeCache.GetMemberAccessor(member);
        _list = null;
        _index = -1;
        _writeBack = writeBack;
        _ownerWritable = ownerWritable;
        _valueGetter = null;
        _valueSetter = null;
    }

    public PropertyAccessor(IList list, int index, bool ownerWritable = true, Action? writeBack = null)
    {
        _owner = list;
        _member = null;
        _accessor = default;
        _list = list;
        _index = index;
        _writeBack = writeBack;
        _ownerWritable = ownerWritable;
        _valueGetter = null;
        _valueSetter = null;
    }

    private PropertyAccessor(PropertyAccessor source, Func<object?> getter, Action<object?> setter)
    {
        this = source;
        _valueGetter = getter;
        _valueSetter = setter;
    }

    internal PropertyAccessor WithValueAccess(Func<object?> getter, Action<object?> setter) =>
        new(this, getter, setter);

    public object? GetValue() => _valueGetter is not null ? _valueGetter() : _member switch
    {
        FieldInfo or PropertyInfo => _accessor.Getter(_owner),
        _ when _list is not null => _list[_index],
        _ => null
    };

    public void SetValue(object? value)
    {
        if (!CanWrite) throw new InvalidOperationException("Property is read-only.");
        if (_valueSetter is not null)
        {
            _valueSetter(value);
            return;
        }
        switch (_member)
        {
            case FieldInfo or PropertyInfo:
                if (_accessor.Setter is null) throw new InvalidOperationException("Property is read-only.");
                _accessor.Setter(_owner, value);
                break;
            default:
                _list![_index] = value;
                break;
        }
        _writeBack?.Invoke();
    }
}
