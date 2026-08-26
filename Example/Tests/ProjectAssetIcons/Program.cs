using BEngine.Editor;
using BEngine.Rendering;
using BEngine.UIElements;

namespace BEngine.ExampleTests.ProjectAssetIcons;

internal static class Program
{
    private static int Main()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "ProjectAssetIconsFixture");
        if (Directory.Exists(fixture)) Directory.Delete(fixture, recursive: true);
        var empty = Directory.CreateDirectory(Path.Combine(fixture, "Empty")).FullName;
        var filled = Directory.CreateDirectory(Path.Combine(fixture, "Filled")).FullName;
        File.WriteAllText(Path.Combine(filled, "Player.cs"), "class Player {}");

        Require(EditorAssetIcons.GetIconPath(empty) == EditorAssetIcons.EmptyFolder,
            "Empty folder did not use the empty-folder icon.");
        Require(EditorAssetIcons.GetIconPath(filled) == EditorAssetIcons.ClosedFolder,
            "Non-empty folder did not use the closed-folder icon.");
        Require(EditorAssetIcons.GetIconPath(filled, expanded: true) == EditorAssetIcons.OpenFolder,
            "Expanded folder did not use the open-folder icon.");
        Require(EditorAssetIcons.GetIconPath("Player.cs").EndsWith("AssetScript.png"),
            "C# script icon mapping is missing.");
        Require(EditorAssetIcons.GetIconPath("Main.scene.yaml").EndsWith("AssetScene.png"),
            "Scene icon mapping lost priority to generic YAML.");
        Require(EditorAssetIcons.GetIconPath("Robot.prefab.yaml").EndsWith("AssetPrefab.png"),
            "Prefab icon mapping lost priority to generic YAML.");
        Require(EditorAssetIcons.GetIconPath("Editor.uxml").EndsWith("AssetMarkup.png"),
            "UXML icon mapping is missing.");
        Require(EditorAssetIcons.GetIconPath("Editor.uss").EndsWith("AssetStyle.png"),
            "USS icon mapping is missing.");
        var iconFile = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(),
            "src", "Core", "Editor", EditorAssetIcons.ClosedFolder.Replace('/', Path.DirectorySeparatorChar)));
        Require(FileUIRenderResourceResolver.Shared.TryResolveTexture(iconFile, out var texture) &&
                texture.Width == 32 && texture.Height == 32,
            "Built-in project icon could not be decoded into a GPU texture.");

        var child = new TreeViewItem(2, "Player.cs", "Player.cs",
            IconPath: EditorAssetIcons.GetIconPath("Player.cs"));
        var folder = new TreeViewItem(1, "Filled", filled, [child],
            EditorAssetIcons.ClosedFolder, EditorAssetIcons.OpenFolder);
        var tree = new TreeView { items = [folder] };
        tree.CollapseItem(folder.Id);
        var collapsedImages = UIRenderListBuilder.Build(tree, 320, 100).Commands
            .Where(command => command.Type == UIRenderCommandType.Image).Select(command => command.Content).ToArray();
        Require(collapsedImages.Contains(EditorAssetIcons.ClosedFolder) &&
                !collapsedImages.Contains(EditorAssetIcons.OpenFolder),
            "Collapsed TreeView row rendered the wrong folder icon.");
        tree.ExpandItem(folder.Id);
        var expandedImages = UIRenderListBuilder.Build(tree, 320, 100).Commands
            .Where(command => command.Type == UIRenderCommandType.Image).Select(command => command.Content).ToArray();
        Require(expandedImages.Contains(EditorAssetIcons.OpenFolder) &&
                expandedImages.Any(path => path.EndsWith("AssetScript.png")),
            "Expanded TreeView did not render the open folder and child resource icons.");

        Directory.Delete(fixture, recursive: true);
        Console.WriteLine("PROJECT_ASSET_ICONS_OK|empty,closed,open,script,scene,prefab,uxml,uss,png,gpu-tree");
        return 0;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
