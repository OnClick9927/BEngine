using System.Globalization;

namespace BEngine.Localization;

/// <summary>Own one context per game/session or editor preview. All operations run on the engine thread.</summary>
public sealed class LocalizationContext : IDisposable
{
    private readonly Dictionary<string, Dictionary<string, Translation>> _tables = new(StringComparer.OrdinalIgnoreCase);
    private string[] _languageChain;
    private int _requestVersion;
    private bool _disposed;
    public string language { get; private set; }
    public string fallbackLanguage { get; }
    public bool pseudoLocalization { get; set; }
    public ILocalizationTableProvider? provider { get; set; }
    public ILocalizationPreferenceRecorder? recorder { get; set; }
    public event Action? changed;
    private readonly LinkedList<LocalizationBinding> _bindings = new();
    private long _bindingSequence;
    public event Action<string>? missingKey;

    public LocalizationContext(string fallbackLanguage = "en")
    {
        language = this.fallbackLanguage = NormalizeLanguage(fallbackLanguage);
        _languageChain = BuildChain(language);
    }

    public static string NormalizeLanguage(string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        var result = language.Trim().Replace('_', '-');
        if (result.Length > 64 || result.Split('-').Any(part => part.Length == 0 || part.Any(c => !char.IsAsciiLetterOrDigit(c))))
            throw new ArgumentException("Language identifiers must contain letters or digits separated by hyphens.", nameof(language));
        return result;
    }

    /// <summary>Copies table data. Editing a source asset cannot silently mutate an active session.</summary>
    public void MergeContext(LocalizationTable table)
    {
        EnsureAlive();
        ArgumentNullException.ThrowIfNull(table);
        table.Validate();
        ++_requestVersion;
        using var budget = LoopExecutionGuard.AllowIterations(table.entries.Count + 1);
        var locale = NormalizeLanguage(table.language);
        var merged = _tables.TryGetValue(locale, out var previous)
            ? new Dictionary<string, Translation>(previous, StringComparer.Ordinal) : new(StringComparer.Ordinal);
        foreach (var entry in table.entries) merged[entry.key] = new(entry.text, entry.asset);
        _tables[locale] = merged;
        NotifyChanged();
    }

    public bool RemoveLanguage(string language)
    {
        EnsureAlive();
        ++_requestVersion;
        var removed = _tables.Remove(NormalizeLanguage(language));
        if (removed) NotifyChanged();
        return removed;
    }

    public BValueTask<bool> InitializeAsync(string? initialLanguage = null, BCancellationToken cancellationToken = default)
    {
        var saved = recorder?.ReadLanguage();
        return SetLanguageAsync(!string.IsNullOrWhiteSpace(saved) ? saved : initialLanguage ?? fallbackLanguage, cancellationToken);
    }

    /// <returns>False if a newer language request superseded this operation.</returns>
    public async BValueTask<bool> SetLanguageAsync(string requestedLanguage, BCancellationToken cancellationToken = default)
    {
        EnsureAlive();
        var next = NormalizeLanguage(requestedLanguage);
        var chain = BuildChain(next);
        var version = ++_requestVersion;
        var pending = new Dictionary<string, Dictionary<string, Translation>>(StringComparer.OrdinalIgnoreCase);
        var source = provider;
        foreach (var locale in chain)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_tables.ContainsKey(locale) || source is null) continue;
            var tables = await source.LoadAsync(locale, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed || version != _requestVersion) return false;
            var values = new Dictionary<string, Translation>(StringComparer.Ordinal);
            foreach (var table in tables)
            {
                table.Validate();
                using var budget = LoopExecutionGuard.AllowIterations(table.entries.Count + 1);
                if (!NormalizeLanguage(table.language).Equals(locale, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Provider returned '{table.language}' for '{locale}'.");
                foreach (var entry in table.entries) values[entry.key] = new(entry.text, entry.asset);
            }
            pending[locale] = values;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed || version != _requestVersion) return false;
        // A recorder failure leaves the active language and tables untouched.
        recorder?.WriteLanguage(next);
        foreach (var pair in pending) _tables[pair.Key] = pair.Value;
        language = next;
        _languageChain = chain;
        NotifyChanged();
        return true;
    }

    /// <summary>Returns the untranslated-format template using the same language fallback rules.</summary>
    public string GetTemplate(string key)
    {
        EnsureAlive();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        foreach (var locale in _languageChain)
            if (_tables.TryGetValue(locale, out var table) && table.TryGetValue(key, out var value) && value.Text is { } text) return text;
        missingKey?.Invoke(key);
        return key;
    }

    public string GetText(string key, IReadOnlyDictionary<string, object?>? arguments = null)
    {
        EnsureAlive();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        foreach (var locale in _languageChain)
            if (_tables.TryGetValue(locale, out var table) && table.TryGetValue(key, out var value) && value.Text is { } text)
            {
                CultureInfo culture;
                try { culture = CultureInfo.GetCultureInfo(locale); }
                catch (CultureNotFoundException) { culture = CultureInfo.InvariantCulture; }
                var formatted = LocalizedTextFormatter.Format(text, arguments, culture);
                return pseudoLocalization ? LocalizedTextFormatter.PseudoLocalize(formatted) : formatted;
            }
        missingKey?.Invoke(key);
        return key;
    }

    public T? GetAsset<T>(string key) where T : BObject
    {
        EnsureAlive();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        foreach (var locale in _languageChain)
            if (_tables.TryGetValue(locale, out var table) && table.TryGetValue(key, out var value) && value.Asset is { isDestroyed: false } asset)
                return asset as T ?? throw new InvalidCastException($"Localized asset '{key}' is {asset.GetType().Name}, not {typeof(T).Name}.");
        missingKey?.Invoke(key);
        return null;
    }

    public LocalizationBinding BindText(string key, Action<string> apply, Func<IReadOnlyDictionary<string, object?>?>? arguments = null)
    {
        ArgumentNullException.ThrowIfNull(apply);
        return Bind(() => apply(GetText(key, arguments?.Invoke())));
    }

    public LocalizationBinding BindAsset<T>(string key, Action<T?> apply) where T : BObject
    {
        ArgumentNullException.ThrowIfNull(apply);
        return Bind(() => apply(GetAsset<T>(key)));
    }

    public LocalizationBinding BindProperty(string key, object target, string memberName)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!RuntimeTypeCache.TryFindInstanceMember(target.GetType(), memberName, out var member))
            throw new ArgumentException($"Unknown member '{memberName}'.", nameof(memberName));
        var accessor = RuntimeTypeCache.GetMemberAccessor(member);
        if (!accessor.CanWrite) throw new ArgumentException($"Member '{memberName}' is read-only.", nameof(memberName));
        if (accessor.ValueType == typeof(string)) return BindText(key, text => accessor.Setter!(target, text));
        if (!typeof(BObject).IsAssignableFrom(accessor.ValueType))
            throw new ArgumentException("Localized properties must be strings or BObject references.", nameof(memberName));
        return BindAsset<BObject>(key, asset =>
        {
            if (asset is not null && !accessor.ValueType.IsInstanceOfType(asset))
                throw new InvalidCastException($"Localized asset '{key}' is not assignable to '{memberName}'.");
            accessor.Setter!(target, asset);
        });
    }

    public LocalizationBinding Bind(Action refresh)
    {
        EnsureAlive();
        ArgumentNullException.ThrowIfNull(refresh);
        var binding = new LocalizationBinding(this, refresh);
        binding.Sequence = ++_bindingSequence;
        binding.Node = _bindings.AddLast(binding);
        try { binding.Refresh(); }
        catch { binding.Dispose(); throw; }
        return binding;
    }

    public void Refresh() { EnsureAlive(); NotifyChanged(); }
    internal void Unbind(LocalizationBinding binding)
    {
        // Keep the removed node's successor so a refresh can survive reentrant disposal.
        if (binding.Node is not { } node) return;
        binding.NextAfterRemoval = node.Next;
        _bindings.Remove(node); binding.Node = null;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ++_requestVersion;
        foreach (var binding in _bindings) binding.Detach();
        _bindings.Clear();
        changed = null;
        missingKey = null;
        _tables.Clear();
        provider = null;
        recorder = null;
    }

    private string[] BuildChain(string locale)
    {
        var chain = new List<string>();
        Add(locale);
        Add(fallbackLanguage);
        return chain.ToArray();
        void Add(string value)
        {
            while (true)
            {
                if (!chain.Contains(value, StringComparer.OrdinalIgnoreCase)) chain.Add(value);
                var separator = value.LastIndexOf('-');
                if (separator < 0) return;
                value = value[..separator];
            }
        }
    }
    private void NotifyChanged()
    {
        var lastSequence = _bindingSequence;
        var node = _bindings.First;
        while (node is not null && !_disposed)
        {
            var binding = node.Value;
            if (binding.Sequence > lastSequence) break;
            var next = node.Next;
            if (binding.Node is not null)
                try { binding.Refresh(); } catch (Exception exception) { Debug.LogException(exception); }
            node = next ?? binding.NextAfterRemoval;
            while (node is not null && node.Value.Node is null) node = node.Value.NextAfterRemoval;
        }
        if (_disposed) return;
        foreach (var callback in Delegate.EnumerateInvocationList(changed))
            try { callback(); } catch (Exception exception) { Debug.LogException(exception); }
    }
    private void EnsureAlive() => ObjectDisposedException.ThrowIf(_disposed, this);
    private readonly record struct Translation(string? Text, BObject? Asset);
}

public sealed class LocalizationBinding : IDisposable
{
    internal LinkedListNode<LocalizationBinding>? Node, NextAfterRemoval;
    internal long Sequence;
    private LocalizationContext? _context;
    private Action? _refresh;
    internal LocalizationBinding(LocalizationContext context, Action refresh) { _context = context; _refresh = refresh; }
    public void Refresh() => _refresh?.Invoke();
    public void Dispose() { _context?.Unbind(this); _context = null; _refresh = null; }
    internal void Detach() { Node = null; NextAfterRemoval = null; _context = null; _refresh = null; }
}
