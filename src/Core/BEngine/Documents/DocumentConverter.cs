namespace BEngine.Documents;

public abstract class DocumentConverter<TDocument, TObject> : IDocumentConverter
    where TDocument : Document
    where TObject : BObject
{
    private static readonly Type[] SupportedObjectTypes = [typeof(TObject)];

    public Type DocumentType => typeof(TDocument);
    public IReadOnlyList<Type> ObjectTypes => SupportedObjectTypes;

    public BObject ToBObject(Document document, DocumentConversionContext context) =>
        ToBObject((TDocument)document, context);

    public Document FromBObject(BObject value, DocumentConversionContext context) =>
        FromBObject((TObject)value, context);

    protected abstract TObject ToBObject(TDocument document, DocumentConversionContext context);
    protected abstract TDocument FromBObject(TObject value, DocumentConversionContext context);
}
