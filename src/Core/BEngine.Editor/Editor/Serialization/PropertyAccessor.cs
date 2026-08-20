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

    public MemberInfo? MemberInfo => _member;
    public bool CanWrite => _member switch
    {
        FieldInfo or PropertyInfo => _accessor.CanWrite,
        _ => _list is not null && !_list.IsReadOnly
    };

    public Type ValueType => _member switch
    {
        FieldInfo or PropertyInfo => _accessor.ValueType,
        _ when _list is not null => _list.GetType().IsArray
            ? _list.GetType().GetElementType()!
            : _list.GetType().IsGenericType ? _list.GetType().GetGenericArguments()[0] : typeof(object),
        _ => typeof(object)
    };

    public PropertyAccessor(object owner, MemberInfo member)
    {
        _owner = owner;
        _member = member;
        _accessor = RuntimeTypeCache.GetMemberAccessor(member);
        _list = null;
        _index = -1;
    }

    public PropertyAccessor(IList list, int index)
    {
        _owner = list;
        _member = null;
        _accessor = default;
        _list = list;
        _index = index;
    }

    public object? GetValue() => _member switch
    {
        FieldInfo or PropertyInfo => _accessor.Getter(_owner),
        _ when _list is not null => _list[_index],
        _ => null
    };

    public void SetValue(object? value)
    {
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
    }
}
