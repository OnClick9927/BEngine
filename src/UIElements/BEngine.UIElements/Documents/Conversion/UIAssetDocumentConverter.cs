using BEngine.Documents;

namespace BEngine.UIElements;

internal sealed class UIAssetDocumentConverter : DocumentConverter<UIAssetDocument, VisualTreeAsset>
{
    protected override VisualTreeAsset ToBObject(UIAssetDocument document, DocumentConversionContext context)
    {
        Validate(document);
        return VisualTreeAsset.FromDocument(document, context.SourcePath);
    }

    protected override UIAssetDocument FromBObject(VisualTreeAsset asset, DocumentConversionContext context) =>
        asset.CreateDocument();

    internal static void Validate(UIAssetDocument document)
    {
        if (document.Format != "BEngine.UI")
            throw new InvalidDataException($"Unsupported UI document format '{document.Format}'.");
        if (document.Version != 1)
            throw new InvalidDataException($"Unsupported UI document version {document.Version}.");
        if (document.Root is null) throw new InvalidDataException("UI document has no root element.");
    }
}
