using BEngine.Editor;
using BEngine.Animation;
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
        Require(EditorAssetIcons.GetIconPath("Characters.atlas.yaml").EndsWith("AssetAtlas.png"),
            "TextureAtlas icon mapping is missing.");
        Require(EditorAssetIcons.GetIconPath("Studio.guiskin.yaml").EndsWith("AssetSkin.png"),
            "GUISkin icon mapping is missing.");
        Require(EditorBuiltinIcons.Resolve("ScriptableObject Icon") == EditorBuiltinIcons.Assets.Data,
            "ScriptableObject Unity-style icon alias is missing.");
        Require(EditorBuiltinIcons.Resolve("Sprite Icon") == EditorBuiltinIcons.Assets.Image &&
                EditorBuiltinIcons.Resolve("Texture2D Icon") == EditorBuiltinIcons.Assets.Image,
            "Unity-style Sprite/Texture2D icon aliases are missing.");
        Require(EditorBuiltinIcons.Resolve("Toolbar Plus More") == EditorBuiltinIcons.Toolbar.AddDropdown &&
                EditorBuiltinIcons.Resolve("d_console.warnicon") == EditorBuiltinIcons.Toolbar.Warning &&
                EditorBuiltinIcons.Resolve("UndoHistory") == EditorBuiltinIcons.Toolbar.UndoHistory &&
                EditorBuiltinIcons.Resolve("Layout") == EditorBuiltinIcons.Toolbar.Layout,
            "Unity-style toolbar/history/layout icon aliases are missing.");
        Require(EditorBuiltinIcons.Resolve("BoxCollider2D Icon") == EditorBuiltinIcons.Components.Collider2D &&
                EditorBuiltinIcons.Resolve("Animation Icon") == EditorBuiltinIcons.Components.Animator &&
                EditorBuiltinIcons.Resolve("TilemapRenderer Icon") == EditorBuiltinIcons.Components.Tilemap &&
                EditorBuiltinIcons.Resolve("NavMeshAgent Icon") == EditorBuiltinIcons.Components.Navigation,
            "Unity-style component icon aliases are missing.");
        RequireTypeIcon(typeof(Scene), "AssetScene.png");
        RequireTypeIcon(typeof(PrefabAsset), "AssetPrefab.png");
        RequireTypeIcon(typeof(Material), "AssetMaterial.png");
        RequireTypeIcon(typeof(Shader), "AssetShader.png");
        RequireTypeIcon(typeof(BEngine.Texture), "AssetImage.png");
        RequireTypeIcon(typeof(Sprite), "AssetImage.png");
        RequireTypeIcon(typeof(Font), "AssetFont.png");
        RequireTypeIcon(typeof(MonoScript), "AssetScript.png");
        RequireTypeIcon(typeof(Script), "AssetScript.png");
        RequireTypeIcon(typeof(BEngine.TextAsset), "AssetText.png");
        RequireTypeIcon(typeof(TextureAtlas), "AssetAtlas.png");
        RequireTypeIcon(typeof(GUISkin), "AssetSkin.png");
        RequireTypeIcon(typeof(IconProbeAsset), "AssetData.png");
        RequireTypeIcon(typeof(AnimationClip), "AssetAnimation.png");
        RequireTypeIcon(typeof(AnimatorController), "AssetAnimation.png");
        RequireTypeIcon(typeof(VisualTreeAsset), "AssetMarkup.png");
        RequireTypeIcon(typeof(StyleSheet), "AssetStyle.png");
        RequireComponentIcon(typeof(SpriteRenderer), "SpriteRenderer.png");
        RequireComponentIcon(typeof(ParticleSystem2D), "ParticleSystem2D.png");
        RequireComponentIcon(typeof(Animator), "Animator.png");
        RequireComponentIcon(typeof(BEngine.Animation.Animation), "Animator.png");
        RequireComponentIcon(typeof(UIDocument), "UIDocument.png");
        Require(AssetPreview.GetMiniThumbnail(new GUISkin()).EndsWith("AssetSkin.png"),
            "GUISkin Inspector preview did not use its registered type icon.");
        Require(AssetPreview.GetMiniThumbnail(new DirectIconProbeAsset()).EndsWith("AssetFont.png"),
            "A direct external BAsset subclass did not use its EditorIcon in AssetPreview.");

        var repository = FindRepositoryRoot();
        foreach (var category in new[]
                 {
                     typeof(EditorBuiltinIcons.Assets), typeof(EditorBuiltinIcons.Components),
                     typeof(EditorBuiltinIcons.Toolbar)
                 })
        {
            foreach (var field in category.GetFields(
                         System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (field.FieldType != typeof(string) || field.GetRawConstantValue() is not string resourcePath)
                    continue;
                var iconFile = Path.Combine(repository, "src", "Core", "Editor",
                    resourcePath.Replace('/', Path.DirectorySeparatorChar));
                Require(File.Exists(iconFile), $"Built-in icon is missing: {resourcePath}");
                Require(FileUIRenderResourceResolver.Shared.TryResolveTexture(iconFile, out var texture) &&
                        texture.Width == 32 && texture.Height == 32,
                    $"Built-in icon is not a decodable 32x32 GPU texture: {resourcePath}");
            }
        }

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
        Console.WriteLine("PROJECT_ASSET_ICONS_OK|empty,closed,open,all-types,unity-aliases,preview,all-png,gpu-tree");
        return 0;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireTypeIcon(Type type, string expectedFileName)
    {
        var icon = EditorIconRegistry.GetIconPath(type);
        Require(icon is not null && icon.EndsWith(expectedFileName, StringComparison.OrdinalIgnoreCase),
            $"{type.Name} did not resolve the expected {expectedFileName} icon.");
    }

    private static void RequireComponentIcon(Type type, string expectedFileName)
    {
        var icon = EditorIconRegistry.GetComponentIconPath(type);
        Require(icon.EndsWith(expectedFileName, StringComparison.OrdinalIgnoreCase),
            $"{type.Name} did not resolve the expected {expectedFileName} component icon.");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "BEngine.sln")))
                return directory.FullName;
        throw new DirectoryNotFoundException("BEngine repository root was not found.");
    }
}

internal sealed class IconProbeAsset : ScriptableObject
{
}

[EditorIcon("Icons/Assets/AssetFont.png")]
internal sealed class DirectIconProbeAsset : BAsset
{
}
