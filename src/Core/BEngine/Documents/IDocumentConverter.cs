using System.Reflection;

namespace BEngine.Documents;

public interface IDocumentConverter
{
    Type DocumentType { get; }
    IReadOnlyList<Type> ObjectTypes { get; }
    BObject ToBObject(Document document, DocumentConversionContext context);
    Document FromBObject(BObject value, DocumentConversionContext context);
    void UnregisterAssembly(Assembly assembly) { }
}
