namespace BEngine.Documents;

public sealed class DocumentObject : BObject
{
    public Document document { get; }

    internal DocumentObject(Document document)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
        name = document.GetType().Name;
    }
}
