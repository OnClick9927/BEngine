using BEngine.Serialization;
using YamlDotNet.Serialization;

namespace BEngine.Documents;

public abstract class Document
{
    [YamlIgnore]
    internal bool IsRuntimeSnapshot { get; set; }

    public string ToYaml()
    {
        DocumentValidationRegistry.Validate(this);
        return YamlUtility.Serialize(this);
    }

    public void Save(string path)
    {
        EnsureCanPersist(this);
        DocumentValidationRegistry.Validate(this);
        YamlUtility.Save(this, path);
    }

    public BObject ToBObject() => ToBObject(default);

    public BObject ToBObject(DocumentConversionContext context)
    {
        var value = DocumentConversionRegistry.ToBObject(this, context);
        if (IsRuntimeSnapshot && value is Scene scene) scene.MarkRuntimeOnly();
        return value;
    }

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

    public static Document FromBObject(BObject value, DocumentConversionContext context)
    {
        ArgumentNullException.ThrowIfNull(value);
        var document = DocumentConversionRegistry.FromBObject(value, context);
        document.IsRuntimeSnapshot = IsRuntimeSource(value);
        return document;
    }

    public static TDocument FromBObject<TDocument>(BObject value)
        where TDocument : Document => FromBObject<TDocument>(value, default);

    public static TDocument FromBObject<TDocument>(BObject value, DocumentConversionContext context)
        where TDocument : Document
    {
        ArgumentNullException.ThrowIfNull(value);
        var document = (TDocument)DocumentConversionRegistry.FromBObject(value, typeof(TDocument), context);
        document.IsRuntimeSnapshot = IsRuntimeSource(value);
        return document;
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

    public static void SaveBObject(BObject value, string path)
    {
        EnsureCanPersist(value);
        FromBObject(value, new DocumentConversionContext(path)).Save(path);
    }

    public static void SaveBObject<TDocument>(BObject value, string path) where TDocument : Document
    {
        EnsureCanPersist(value);
        FromBObject<TDocument>(value, new DocumentConversionContext(path)).Save(path);
    }

    private static void EnsureCanPersist(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.IsRuntimeSnapshot)
            throw new InvalidOperationException("Runtime snapshots are transient and cannot be saved.");
    }

    private static void EnsureCanPersist(BObject value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (IsRuntimeSource(value))
            throw new InvalidOperationException("Runtime objects are transient and cannot be saved.");
    }

    private static bool IsRuntimeSource(BObject value)
    {
        if (value.IsRuntimeOnly) return true;
        if (Application.isPlaying && SceneRuntime.currentScene is not null) return true;
        var scene = GetOwningScene(value);
        return scene is not null && (scene.IsRuntimeOnly || SceneRuntime.IsRunningScene(scene));
    }

    private static Scene? GetOwningScene(BObject value)
    {
        try
        {
            return value switch
            {
                Scene scene => scene,
                GameObject gameObject => gameObject.SceneUnchecked,
                Component component => component.GameObjectUnchecked.SceneUnchecked,
                _ => null
            };
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
