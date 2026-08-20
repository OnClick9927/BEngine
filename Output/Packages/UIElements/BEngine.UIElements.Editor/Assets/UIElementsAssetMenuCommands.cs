using System.Text;
using BEngine.Editor;

namespace BEngine.UIElements.Editor;

internal static class UIElementsAssetMenuCommands
{
    [MenuItem("Assets/Create/UI Toolkit/UI Document", false, 300)]
    private static void CreateDocument() => Create("New UI Document.uxml",
        "<UXML>\n  <VisualElement name=\"Root\" />\n</UXML>\n");

    [MenuItem("Assets/Create/UI Toolkit/UI Document", true)]
    [MenuItem("Assets/Create/UI Toolkit/Style Sheet", true)]
    private static bool ValidateCreate() => ProjectWindowUtil.GetActiveFolderPath() is not null;

    [MenuItem("Assets/Create/UI Toolkit/Style Sheet", false, 301)]
    private static void CreateStyleSheet() => Create("New UI Style Sheet.uss",
        ".root {\n  flex-direction: column;\n}\n");

    private static void Create(string fileName, string contents)
    {
        var folder = ProjectWindowUtil.GetActiveFolderPath();
        if (folder is null) return;
        var assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folder.TrimEnd('/', '\\')}/{fileName}");
        var fullPath = Path.GetFullPath(Path.Combine(EditorApplication.projectPath,
            assetPath.Replace('/', Path.DirectorySeparatorChar)));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, contents, new UTF8Encoding(false));
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        ProjectWindowUtil.ShowCreatedAsset(assetPath, beginRename: true);
    }
}
