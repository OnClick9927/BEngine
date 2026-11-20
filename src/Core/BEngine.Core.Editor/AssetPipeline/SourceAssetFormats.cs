using System.Runtime.CompilerServices;
using BEngine.Platform;

namespace BEngine.Editor;

/// <summary>Default source-document formats supplied by this backend.</summary>
internal static class SourceAssetFormats
{
    [ModuleInitializer]
    internal static void Register()
    {
        AssetTypeRegistry.Register(".asmdef.yaml", "AssemblyDefinition");
        RegisterText(".json", "JSON");
        RegisterText(".xml", "XML");
        RegisterText(".md", "Markdown");
        RegisterText(".txt", "Text");
        RegisterText(".yaml", "YamlAsset");
        RegisterText(".html", "HTML Document");
        RegisterText(".htm", "HTML Document");
    }

    private static void RegisterText(string suffix, string name) =>
        AssetTypeRegistry.Register<TextAsset>(suffix, name, static context => new TextAsset(
            EditorServices.GetRequiredService<FileUtility>().ReadAllText(context.ImportedPath))
        {
            name = EditorServices.GetRequiredService<PathUtility>().GetFileName(context.SourcePath)
        });
}
