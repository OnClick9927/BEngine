using System.Collections.Concurrent;
using System.Reflection;

namespace BEngine.Documents;

public static class DocumentConversionRegistry
{
    private static readonly object Sync = new();
    private static readonly Dictionary<Type, IDocumentConverter> ByDocument = [];
    private static readonly List<IDocumentConverter> Converters = [];
    private static readonly ConcurrentDictionary<Type, IDocumentConverter> ByObject = [];

    static DocumentConversionRegistry()
    {
        Register(SceneDocumentConverter.Shared);
        Register(PrefabDocumentConverter.Shared);
    }

    public static void Register(IDocumentConverter converter)
    {
        ArgumentNullException.ThrowIfNull(converter);
        if (!typeof(Document).IsAssignableFrom(converter.DocumentType))
            throw new ArgumentException($"{converter.DocumentType.FullName} is not a Document.", nameof(converter));
        if (converter.ObjectTypes.Count == 0 || converter.ObjectTypes.Any(type => !typeof(BObject).IsAssignableFrom(type)))
            throw new ArgumentException("Document converters must declare at least one BObject type.", nameof(converter));

        lock (Sync)
        {
            if (ByDocument.TryGetValue(converter.DocumentType, out var previous)) Converters.Remove(previous);
            ByDocument[converter.DocumentType] = converter;
            Converters.Add(converter);
            ByObject.Clear();
        }
    }

    public static BObject ToBObject(Document document, DocumentConversionContext context)
    {
        ArgumentNullException.ThrowIfNull(document);
        DocumentValidationRegistry.Validate(document);
        IDocumentConverter? converter;
        lock (Sync) ByDocument.TryGetValue(document.GetType(), out converter);
        return converter?.ToBObject(document, context) ?? new DocumentObject(document);
    }

    public static Document FromBObject(BObject value, DocumentConversionContext context)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value is DocumentObject wrapper) return wrapper.document;
        var objectType = value.GetType();
        if (!ByObject.TryGetValue(objectType, out var converter))
        {
            converter = ResolveObjectConverter(objectType) ?? throw new NotSupportedException(
                $"No Document converter is registered for {objectType.FullName}.");
            ByObject.TryAdd(objectType, converter);
        }
        var document = converter.FromBObject(value, context);
        DocumentValidationRegistry.Validate(document);
        return document;
    }

    public static Document FromBObject(BObject value, Type documentType, DocumentConversionContext context)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(documentType);
        if (!typeof(Document).IsAssignableFrom(documentType))
            throw new ArgumentException($"{documentType.FullName} is not a Document.", nameof(documentType));
        if (value is DocumentObject wrapper)
        {
            if (documentType.IsInstanceOfType(wrapper.document)) return wrapper.document;
            throw new InvalidDataException(
                $"DocumentObject contains {wrapper.document.GetType().FullName}, not {documentType.FullName}.");
        }

        IDocumentConverter? converter;
        lock (Sync) ByDocument.TryGetValue(documentType, out converter);
        if (converter is null || !converter.ObjectTypes.Any(type => type.IsInstanceOfType(value)))
            throw new NotSupportedException(
                $"No {documentType.FullName} converter is registered for {value.GetType().FullName}.");
        var document = converter.FromBObject(value, context);
        DocumentValidationRegistry.Validate(document);
        return document;
    }

    public static void UnregisterAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        IDocumentConverter[] snapshot;
        lock (Sync) snapshot = [.. Converters];
        foreach (var converter in snapshot) converter.UnregisterAssembly(assembly);
    }

    private static IDocumentConverter? ResolveObjectConverter(Type objectType)
    {
        lock (Sync)
        {
            return Converters.LastOrDefault(converter =>
                converter.ObjectTypes.Any(candidate => candidate.IsAssignableFrom(objectType)));
        }
    }
}
