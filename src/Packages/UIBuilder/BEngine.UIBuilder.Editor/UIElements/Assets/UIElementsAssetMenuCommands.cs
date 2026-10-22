using BEngine.Editor;
using BEngine.Platform;

namespace BEngine.UIElements.Editor;

internal static class UIElementsAssetMenuCommands
{
    [OnOpenAsset]
    private static bool OpenDocumentInBuilder(string path)
    {
        if (!path.EndsWith(".bxml", StringComparison.OrdinalIgnoreCase)) return false;
        UIBuilderWindow.Open(path);
        return true;
    }

    [MenuItem("Assets/Create/UI Toolkit/UI Document", false, 300)]
    private static void CreateDocument() => Create("New UI Document.bxml",
        "<UXML>\n  <VisualElement name=\"Root\" />\n</UXML>\n");

    [MenuItem("Assets/Create/UI Toolkit/UI Document", true)]
    [MenuItem("Assets/Create/UI Toolkit/Style Sheet", true)]
    private static bool ValidateCreate() => ProjectWindowUtil.GetActiveFolderPath() is not null;

    [MenuItem("Assets/Create/UI Toolkit/Style Sheet", false, 301)]
    private static void CreateStyleSheet() => Create("New UI Style Sheet.bss",
        ".root {\n  flex-direction: column;\n}\n");

    private static void Create(string fileName, string contents)
    {
        var folder = ProjectWindowUtil.GetActiveFolderPath();
        if (folder is null) return;
        var fileSystem = EditorServices.GetRequiredService<FileUtility>();
        var paths = EditorServices.GetRequiredService<PathUtility>();
        var assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folder.TrimEnd('/', '\\')}/{fileName}");
        var fullPath = paths.GetFullPath(paths.Combine(EditorApplication.projectPath,
            assetPath.Replace('/', paths.DirectorySeparatorChar)));
        fileSystem.CreateDirectory(paths.GetDirectoryName(fullPath)!);
        fileSystem.WriteAllText(fullPath, contents);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        ProjectWindowUtil.ShowCreatedAsset(assetPath, beginRename: true);
    }
}
