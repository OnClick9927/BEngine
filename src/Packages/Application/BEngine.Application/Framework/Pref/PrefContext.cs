using System;
using System.Diagnostics.CodeAnalysis;

namespace BEngine.App.Pref;

/// <summary>A typed, instance-owned preference. Save explicitly after changing Value or its mutable members.</summary>
public sealed class PrefContext<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T> : IPrefContext where T : notnull
{
    private PrefService? _owner;
    private T _value;
    internal PrefContext(PrefService owner, string key, string storageKey, T value)
    {
        _owner = owner; Key = key; StorageKey = storageKey; _value = value;
    }
    public string Key { get; }
    public string StorageKey { get; }
    public T Value
    {
        get { EnsureAlive(); return _value; }
        set { EnsureAlive(); ArgumentNullException.ThrowIfNull(value); _value = value; }
    }
    public void Save() => EnsureAlive().Save<T>(Key);
    public bool Delete() => EnsureAlive().Delete<T>(Key);
    private PrefService EnsureAlive() => _owner ?? throw new ObjectDisposedException(nameof(PrefContext<T>), "The preference was deleted or its owning service was disposed.");
    string IPrefContext.Serialize(IPrefConverter converter) => converter.Serialize(_value, typeof(T));
    void IPrefContext.Invalidate() { _value = default!; _owner = null; }
}

internal interface IPrefContext
{
    string Key { get; }
    string StorageKey { get; }
    string Serialize(IPrefConverter converter);
    void Invalidate();
}
