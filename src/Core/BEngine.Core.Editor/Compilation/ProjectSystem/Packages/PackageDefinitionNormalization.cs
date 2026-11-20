using BEngine.Editor.Documents;

namespace BEngine.ProjectSystem;

public static partial class PackageDefinitionLoader
{
    public static PackageDefinitionDocument Normalize(
        PackageDefinitionDocument document, string sourceName = "package.yaml") =>
        PackageDefinitionRules.Normalize(document, sourceName);
}
