using System.Text;
using BEngine.Editor.Documents;

namespace BEngine.Editor;

public static class AssemblyDefinitionAssetUtility
{
    public static string Create(string folderPath = "Assets")
    {
        if (!AssetDatabase.IsValidFolder(folderPath))
            throw new InvalidDataException($"Assembly definitions can only be created in an Assets folder: {folderPath}");

        var assetPath = AssetDatabase.GenerateUniqueAssetPath(
            $"{folderPath.TrimEnd('/', '\\')}/New Assembly Definition.asmdef.yaml");
        var fileName = AssetPathUtility.SplitNameAndExtension(assetPath).Name;
        var assemblyName = ToIdentifier(fileName);
        AssetModificationProcessorDispatcher.OnWillCreateAsset(assetPath);
        new AssemblyDefinitionDocument
        {
            Name = assemblyName,
            RootNamespace = assemblyName
        }.Save(AssetDatabase.ResolveAssetPath(assetPath));
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        EditorApplication.delayCall += () => CompilationPipeline.RequestScriptCompilation();
        return assetPath;
    }

    [MenuItem("Assets/Create/Assembly Definition", false, 100)]
    private static void CreateFromAssetsMenu() => ProjectAssetCreation.CreateAndReveal(Create);

    [MenuItem("Assets/Create/Assembly Definition", true)]
    private static bool ValidateCreateFromAssetsMenu() => ProjectAssetCreation.CanCreateFromMenu();

    private static string ToIdentifier(string value)
    {
        var result = new StringBuilder(value.Length);
        var separate = false;
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character) || character == '_')
            {
                if (separate && result.Length > 0 && result[^1] != '.')
                    result.Append(char.IsDigit(character) ? "._" : ".");
                result.Append(character);
                separate = false;
            }
            else separate = true;
        }
        var identifier = result.ToString().Trim('.');
        if (identifier.Length == 0) identifier = "GameAssembly";
        if (char.IsDigit(identifier[0])) identifier = "_" + identifier;
        return identifier;
    }
}
