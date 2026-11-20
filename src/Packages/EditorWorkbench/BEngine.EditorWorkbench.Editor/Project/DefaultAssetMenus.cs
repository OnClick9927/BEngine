namespace BEngine.Editor;
internal static class DefaultAssetMenus
{
    [MenuItem("Assets/Create/Folder", false, 10)]
    private static void CreateFolderFromMenu() => ProjectAssetCreation.CreateAndReveal(ProjectAssetCreation.CreateFolder);

    [MenuItem("Assets/Create/Folder", true)]
    [MenuItem("Assets/Create/C# Script", true)]
    [MenuItem("Assets/Create/Scripting/ScriptableObject Script", true)]
    [MenuItem("Assets/Create/Scene", true)]
    [MenuItem("Assets/Create/Prefab", true)]
    [MenuItem("Assets/Create/Text/Text File", true)]
    [MenuItem("Assets/Create/Text/Markdown File", true)]
    [MenuItem("Assets/Create/Data/JSON File", true)]
    [MenuItem("Assets/Create/Data/YAML File", true)]
    private static bool ValidateCreateFromMenu() => ProjectAssetCreation.CanCreateFromMenu();

    [MenuItem("Assets/Create/C# Script", false, 20)]
    private static void CreateScriptFromMenu() => ProjectAssetCreation.CreateAndReveal(ProjectAssetCreation.CreateScript);

    [MenuItem("Assets/Create/Scripting/ScriptableObject Script", false, 21)]
    private static void CreateScriptableObjectFromMenu() => ProjectAssetCreation.CreateAndReveal(ProjectAssetCreation.CreateScriptableObjectScript);

    [MenuItem("Assets/Create/Scene", false, 30)]
    private static void CreateSceneFromMenu() => ProjectAssetCreation.CreateAndReveal(ProjectAssetCreation.CreateScene);

    [MenuItem("Assets/Create/Prefab", false, 40)]
    private static void CreatePrefabFromMenu() => ProjectAssetCreation.CreateAndReveal(ProjectAssetCreation.CreatePrefab);


    [MenuItem("Assets/Create/Text/Text File", false, 400)]
    private static void CreateTextFromMenu() => ProjectAssetCreation.CreateAndReveal(ProjectAssetCreation.CreateText);

    [MenuItem("Assets/Create/Text/Markdown File", false, 401)]
    private static void CreateMarkdownFromMenu() => ProjectAssetCreation.CreateAndReveal(ProjectAssetCreation.CreateMarkdown);

    [MenuItem("Assets/Create/Data/JSON File", false, 410)]
    private static void CreateJsonFromMenu() => ProjectAssetCreation.CreateAndReveal(ProjectAssetCreation.CreateJson);

    [MenuItem("Assets/Create/Data/YAML File", false, 411)]
    private static void CreateYamlFromMenu() => ProjectAssetCreation.CreateAndReveal(ProjectAssetCreation.CreateYaml);

}
