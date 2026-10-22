using System.Collections.ObjectModel;
using System.Runtime.Loader;
using System.Xml.Linq;

namespace BEngine.UIElements;

/// <summary>Registers project/package controls without changing the built-in UXML serializer. Use on the main thread.</summary>
public static class UxmlFactoryRegistry
{
    private static readonly Dictionary<XName, Registration> ByName = [];
    private static readonly Dictionary<Type, Registration> ByType = [];
    public static long revision { get; private set; }
    private static readonly HashSet<string> BuiltInNames = typeof(VisualElement).Assembly.GetExportedTypes()
        .Where(type => typeof(VisualElement).IsAssignableFrom(type)).Select(type => type.Name).ToHashSet(StringComparer.Ordinal);

    /// <param name="elementName">An XML name, preferably namespaced, e.g. "{Game.UI}HealthBar".</param>
    /// <param name="factory">Creates a fresh element from attributes. Common attributes/styles are applied afterward.</param>
    /// <param name="serializeAttributes">Writes custom state as attributes; common properties are serialized automatically.</param>
    /// <param name="serializeChildren">False for controls whose factory recreates its internal child hierarchy.</param>
    public static IDisposable Register<T>(string elementName, Func<IReadOnlyDictionary<string, string>, T> factory,
        Func<T, IReadOnlyDictionary<string, string>> serializeAttributes, bool serializeChildren = true) where T : VisualElement =>
        Register(elementName, factory, serializeAttributes, new UxmlFactoryOptions(), serializeChildren);

    public static IDisposable Register<T>(string elementName, Func<IReadOnlyDictionary<string, string>, T> factory,
        Func<T, IReadOnlyDictionary<string, string>> serializeAttributes, UxmlFactoryOptions options,
        bool serializeChildren = true) where T : VisualElement
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementName);
        ArgumentNullException.ThrowIfNull(factory); ArgumentNullException.ThrowIfNull(serializeAttributes);
        ArgumentNullException.ThrowIfNull(options);
        var name = XName.Get(elementName);
        if (typeof(T).Assembly == typeof(VisualElement).Assembly ||
            (name.NamespaceName.Length == 0 && (BuiltInNames.Contains(name.LocalName) || name.LocalName is "UXML" or "Style" or "SourceAttribute")))
            throw new ArgumentException("Custom factories cannot replace built-in controls or reserved tags.", nameof(elementName));
        if (ByName.ContainsKey(name) || ByType.ContainsKey(typeof(T)))
            throw new InvalidOperationException($"UXML name '{name}' or type '{typeof(T).FullName}' is already registered.");
        var registration = new Registration(name, typeof(T), attributes => factory(attributes),
            element => serializeAttributes((T)element), serializeChildren, options);
        ByName.Add(name, registration); ByType.Add(typeof(T), registration);
        revision++;
        return registration;
    }

    public static IReadOnlyList<string> registeredElements => ByName.Keys.Select(name => name.ToString()).Order(StringComparer.Ordinal).ToArray();
    public static IReadOnlyList<UxmlFactoryDescriptor> libraryElements => ByName.Values.Where(item => item.ShowInLibrary)
        .Select(item => item.Descriptor).OrderBy(item => item.Category, StringComparer.Ordinal)
        .ThenBy(item => item.DisplayName, StringComparer.Ordinal).ThenBy(item => item.ElementName, StringComparer.Ordinal).ToArray();

    /// <summary>Creates an authorable instance with the registration's default attributes.</summary>
    public static VisualElement CreateDefault(string elementName)
    {
        var name = XName.Get(elementName);
        if (!ByName.TryGetValue(name, out var registration)) throw new InvalidOperationException($"UXML factory '{name}' is no longer registered.");
        var node = new XElement(name);
        foreach (var attribute in registration.DefaultAttributes) node.SetAttributeValue(XName.Get(attribute.Key), attribute.Value);
        // Use the serializer so common attributes (name, style, binding, etc.) have the same semantics as a loaded UXML file.
        return UxmlSerializer.DeserializeElement(node.ToString());
    }
    internal static bool IsRegistered(Type type) => ByType.ContainsKey(type);
    internal static bool SerializeChildren(VisualElement element) => !ByType.TryGetValue(element.GetType(), out var registration) || registration.Children;

    internal static VisualElement? Create(XElement node)
    {
        if (!ByName.TryGetValue(node.Name, out var registration)) return null;
        var attributes = new ReadOnlyDictionary<string, string>(node.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration)
            .ToDictionary(attribute => attribute.Name.ToString(), attribute => attribute.Value, StringComparer.Ordinal));
        var element = registration.Factory(attributes);
        if (element is null || element.GetType() != registration.Type || element.parent is not null)
            throw new InvalidDataException($"UXML factory '{node.Name}' must return a fresh, unparented '{registration.Type.FullName}'.");
        return element;
    }

    internal static XElement? Serialize(VisualElement element)
    {
        if (!ByType.TryGetValue(element.GetType(), out var registration)) return null;
        var node = new XElement(registration.Name);
        foreach (var attribute in registration.Serialize(element)) node.SetAttributeValue(XName.Get(attribute.Key), attribute.Value);
        return node;
    }

    private sealed class Registration : IDisposable
    {
        internal XName Name { get; }
        internal Type Type { get; }
        internal Func<IReadOnlyDictionary<string, string>, VisualElement> Factory { get; }
        internal Func<VisualElement, IReadOnlyDictionary<string, string>> Serialize { get; }
        internal bool Children { get; }
        internal UxmlFactoryDescriptor Descriptor { get; }
        internal bool ShowInLibrary { get; }
        internal IReadOnlyDictionary<string, string> DefaultAttributes { get; }
        private readonly AssemblyLoadContext? _context;
        internal Registration(XName name, Type type, Func<IReadOnlyDictionary<string, string>, VisualElement> factory,
            Func<VisualElement, IReadOnlyDictionary<string, string>> serialize, bool children, UxmlFactoryOptions options)
        {
            Name = name; Type = type; Factory = factory; Serialize = serialize; Children = children;
            Descriptor = new(name.ToString(), string.IsNullOrWhiteSpace(options.DisplayName) ? name.LocalName : options.DisplayName,
                string.IsNullOrWhiteSpace(options.Category) ? "Custom Controls" : options.Category,
                string.IsNullOrWhiteSpace(options.Tooltip) ? name.ToString() : options.Tooltip);
            ShowInLibrary = options.ShowInLibrary;
            DefaultAttributes = new ReadOnlyDictionary<string, string>(options.DefaultAttributes is null
                ? new Dictionary<string, string>() : new Dictionary<string, string>(options.DefaultAttributes, StringComparer.Ordinal));
            _context = AssemblyLoadContext.GetLoadContext(type.Assembly);
            if (_context?.IsCollectible == true) _context.Unloading += Unloading;
        }
        private void Unloading(AssemblyLoadContext context) => Dispose();
        public void Dispose()
        {
            if (_context?.IsCollectible == true) _context.Unloading -= Unloading;
            if (ByName.TryGetValue(Name, out var current) && ReferenceEquals(current, this)) { ByName.Remove(Name); revision++; }
            if (ByType.TryGetValue(Type, out current) && ReferenceEquals(current, this)) ByType.Remove(Type);
        }
    }
}
