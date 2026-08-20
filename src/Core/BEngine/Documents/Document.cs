using BEngine.Serialization;

namespace BEngine.Documents;

public abstract class Document
{
    public string ToYaml()
    {
        DocumentValidationRegistry.Validate(this);
        return YamlUtility.Serialize(this);
    }

    public void Save(string path)
    {
        DocumentValidationRegistry.Validate(this);
        YamlUtility.Save(this, path);
    }

    public BObject ToBObject() => ToBObject(default);

    public BObject ToBObject(DocumentConversionContext context) =>
        DocumentConversionRegistry.ToBObject(this, context);

    public static TDocument FromYaml<TDocument>(string yaml) where TDocument : Document
    {
        var document = YamlUtility.Deserialize<TDocument>(yaml);
        DocumentValidationRegistry.Validate(document);
        return document;
    }

    public static TDocument Load<TDocument>(string path) where TDocument : Document
    {
        var document = YamlUtility.Load<TDocument>(path);
        DocumentValidationRegistry.Validate(document);
        return document;
    }

    public static Document FromBObject(BObject value) => FromBObject(value, default);

    public static Document FromBObject(BObject value, DocumentConversionContext context) =>
        DocumentConversionRegistry.FromBObject(value, context);

    public static TDocument FromBObject<TDocument>(BObject value)
        where TDocument : Document => FromBObject<TDocument>(value, default);

    public static TDocument FromBObject<TDocument>(BObject value, DocumentConversionContext context)
        where TDocument : Document
    {
        return (TDocument)DocumentConversionRegistry.FromBObject(value, typeof(TDocument), context);
    }

    public static BObject LoadBObject<TDocument>(string path) where TDocument : Document =>
        Load<TDocument>(path).ToBObject(new DocumentConversionContext(path));

    public static TObject LoadBObject<TDocument, TObject>(string path)
        where TDocument : Document
        where TObject : BObject
    {
        var value = LoadBObject<TDocument>(path);
        return value as TObject ?? throw new InvalidDataException(
            $"{typeof(TDocument).FullName} converts to {value.GetType().FullName}, not {typeof(TObject).FullName}.");
    }

    public static TObject LoadBObject<TDocument, TObject>(string path, IServiceProvider services)
        where TDocument : Document
        where TObject : BObject
    {
        ArgumentNullException.ThrowIfNull(services);
        var value = Load<TDocument>(path).ToBObject(new DocumentConversionContext(path, services));
        return value as TObject ?? throw new InvalidDataException(
            $"{typeof(TDocument).FullName} converts to {value.GetType().FullName}, not {typeof(TObject).FullName}.");
    }

    public static void SaveBObject(BObject value, string path) =>
        FromBObject(value, new DocumentConversionContext(path)).Save(path);

    public static void SaveBObject<TDocument>(BObject value, string path) where TDocument : Document =>
        FromBObject<TDocument>(value, new DocumentConversionContext(path)).Save(path);
}
