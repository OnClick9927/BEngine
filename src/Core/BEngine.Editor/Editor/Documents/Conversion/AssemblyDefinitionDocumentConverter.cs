using BEngine.Documents;
using BEngine.Editor.Documents;

namespace BEngine.Editor;

internal sealed class AssemblyDefinitionDocumentConverter
    : DocumentConverter<AssemblyDefinitionDocument, AssemblyDefinitionAsset>
{
    internal static AssemblyDefinitionDocumentConverter Shared { get; } = new();

    protected override AssemblyDefinitionAsset ToBObject(
        AssemblyDefinitionDocument document,
        DocumentConversionContext context) => new()
    {
        name = Path.GetFileName(context.SourcePath),
        assetPath = context.SourcePath,
        assetType = "AssemblyDefinition",
        definition = document
    };

    protected override AssemblyDefinitionDocument FromBObject(
        AssemblyDefinitionAsset value,
        DocumentConversionContext context) => value.definition;
}
