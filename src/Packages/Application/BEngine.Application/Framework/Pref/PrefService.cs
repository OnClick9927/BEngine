using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace BEngine.App.Pref;

/// <summary>Typed preferences scoped to one game/user namespace. Load is lazy; Save/Delete flush explicitly.
/// Not thread-safe: use from the owning game thread. Dispose releases memory without saving implicitly.</summary>
public sealed class PrefService : IPrefService
{
    public const string DefaultKey = "default";
    private readonly Dictionary<ContextKey, IPrefContext> _contexts = new();
    private IPrefLoader? _loader;
    private IPrefConverter? _converter;
    private bool _busy;

    /// <summary>The service does not own supplied loader/converter instances and never disposes them.</summary>
    public PrefService(string namespaceKey, IPrefLoader? loader = null, IPrefConverter? converter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceKey);
        NamespaceKey = namespaceKey;
        _loader = loader ?? new PlayerPrefsPrefLoader();
        _converter = converter ?? new YamlPrefConverter();
    }

    public string NamespaceKey { get; }
    public bool SaveOnDomainStop { get; set; } = true;
    void IGameService.OnDomainStopping() { if (SaveOnDomainStop) SaveAll(); }

    public T Load<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(string key = DefaultKey) where T : notnull, new() =>
        GetContext<T>(key).Value;

    public T Load<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(string key, Func<T> createDefault) where T : notnull =>
        GetContext(key, createDefault).Value;

    public PrefContext<T> GetContext<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(string key = DefaultKey) where T : notnull, new() =>
        GetContext<T>(key, static () => new T());

    public PrefContext<T> GetContext<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(string key, Func<T> createDefault) where T : notnull
    {
        EnsureAvailable();
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(createDefault);
        var lookup = new ContextKey(typeof(T), key);
        if (_contexts.TryGetValue(lookup, out var existing)) return (PrefContext<T>)existing;
        var storageKey = CreateStorageKey(typeof(T), key);
        _busy = true;
        try
        {
            var loaded = _loader!.TryLoad(storageKey, out var text);
            EnsureNotDisposed();
            var value = loaded ? _converter!.Deserialize(typeof(T), text!) : createDefault();
            EnsureNotDisposed();
            if (value is not T typed)
                throw new InvalidOperationException($"Preference '{key}' did not produce a non-null value of type '{typeof(T)}'.");
            var context = new PrefContext<T>(this, key, storageKey, typed);
            _contexts.Add(lookup, context);
            return context;
        }
        finally { _busy = false; }
    }

    public void Save<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(string key, T value) where T : notnull
    {
        EnsureAvailable();
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        var lookup = new ContextKey(typeof(T), key);
        _contexts.TryGetValue(lookup, out var existing);
        var storageKey = existing?.StorageKey ?? CreateStorageKey(typeof(T), key);
        _busy = true;
        try
        {
            var text = _converter!.Serialize(value, typeof(T));
            EnsureNotDisposed();
            _loader!.Save(storageKey, text);
            EnsureNotDisposed();
            _loader!.Flush();
            EnsureNotDisposed();
            if (existing is PrefContext<T> context) context.Value = value;
            else _contexts.Add(lookup, new PrefContext<T>(this, key, storageKey, value));
        }
        finally { _busy = false; }
    }

    /// <summary>Saves an already loaded context. Missing contexts are errors rather than implicit default creation.</summary>
    public void Save<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(string key = DefaultKey) where T : notnull
    {
        EnsureAvailable();
        ArgumentNullException.ThrowIfNull(key);
        if (!_contexts.TryGetValue(new(typeof(T), key), out var context))
            throw new InvalidOperationException($"Preference '{key}' of type '{typeof(T)}' has not been loaded.");
        SaveContext(context);
    }

    public void SaveAll()
    {
        EnsureAvailable();
        if (_contexts.Count == 0) return;
        _busy = true;
        try
        {
            foreach (var context in _contexts.Values)
            {
                var text = context.Serialize(_converter!);
                EnsureNotDisposed();
                _loader!.Save(context.StorageKey, text);
                EnsureNotDisposed();
            }
            _loader!.Flush();
            EnsureNotDisposed();
        }
        finally { _busy = false; }
    }

    public bool Delete<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(string key = DefaultKey) where T : notnull
    {
        EnsureAvailable();
        ArgumentNullException.ThrowIfNull(key);
        var lookup = new ContextKey(typeof(T), key);
        _contexts.TryGetValue(lookup, out var context);
        var storageKey = context?.StorageKey ?? CreateStorageKey(typeof(T), key);
        _busy = true;
        try
        {
            var deleted = _loader!.Delete(storageKey);
            EnsureNotDisposed();
            if (deleted) _loader!.Flush();
            EnsureNotDisposed();
            if (context is null) return deleted;
            _contexts.Remove(lookup);
            context.Invalidate();
            return true;
        }
        finally { _busy = false; }
    }

    public void Dispose()
    {
        if (_loader is null) return;
        foreach (var context in _contexts.Values) context.Invalidate();
        _contexts.Clear();
        _loader = null;
        _converter = null;
    }

    private void SaveContext(IPrefContext context)
    {
        _busy = true;
        try
        {
            var text = context.Serialize(_converter!);
            EnsureNotDisposed();
            _loader!.Save(context.StorageKey, text);
            EnsureNotDisposed();
            _loader!.Flush();
            EnsureNotDisposed();
        }
        finally { _busy = false; }
    }

    private void EnsureNotDisposed() => ObjectDisposedException.ThrowIf(_loader is null, this);
    private void EnsureAvailable()
    {
        EnsureNotDisposed();
        if (_busy) throw new InvalidOperationException("Preference callbacks cannot reenter the owning service.");
    }

    private string CreateStorageKey(Type type, string key) =>
        "BEngine.App.Pref/" + Uri.EscapeDataString(NamespaceKey) + "/" +
        Uri.EscapeDataString(StableTypeName(type)) + "/" + Uri.EscapeDataString(key);

    // Avoid assembly versions in persisted keys so ordinary game upgrades retain their preferences.
    private static string StableTypeName(Type type)
    {
        if (!type.IsGenericType && !type.IsArray) return type.FullName + "," + type.Assembly.GetName().Name;
        var result = new StringBuilder();
        AppendTypeName(result, type);
        return result.ToString();
    }
    private static void AppendTypeName(StringBuilder result, Type type)
    {
        if (type.IsArray)
        {
            AppendTypeName(result, type.GetElementType()!);
            result.Append('[').Append(',', type.GetArrayRank() - 1).Append(']');
            return;
        }
        var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        result.Append(definition.FullName).Append(',').Append(definition.Assembly.GetName().Name);
        if (!type.IsGenericType) return;
        result.Append('<');
        var arguments = type.GetGenericArguments();
        for (var index = 0; index < arguments.Length; index++)
        {
            if (index > 0) result.Append(';');
            AppendTypeName(result, arguments[index]);
        }
        result.Append('>');
    }
    private readonly record struct ContextKey(Type Type, string Key);
}
