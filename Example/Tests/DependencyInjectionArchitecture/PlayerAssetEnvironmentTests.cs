using System.Reflection;
using BEngine.Documents;
using BEngine.Player;
using BEngine.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.ExampleTests.DependencyInjectionArchitecture;

internal static class PlayerAssetEnvironmentTests
{
    internal static void Verify()
    {
        var previousDataPath = Application.dataPath;
        var root = Path.Combine(Path.GetTempPath(), "BEnginePlayerAssetEnvironment", Guid.NewGuid().ToString("N"));
        var staleAssets = Path.Combine(root, "StaleAssets");
        Directory.CreateDirectory(staleAssets);
        try
        {
            new ProjectDocument { Name = "Player Asset Environment" }
                .Save(Path.Combine(root, ProjectWorkspace.ProjectFileName));
            var workspace = ProjectWorkspace.Open(root);
            WriteAssetFixtures(workspace);

            SetDataPath(staleAssets);
            BAsset.ClearLoadedAssets();
            ClearAtlasResolver();
            var spritePath = Path.Combine(workspace.AssetsPath, "Player.sprite.yaml");
            var stale = BAsset.Load<Sprite>(spritePath) ??
                        throw new InvalidOperationException("The stale Sprite cache fixture did not load.");
            Require(ResolveTexture(stale) == "Assets/Player.png",
                "The stale Atlas resolver fixture was not primed against its original root.");

            _ = new ServiceCollection().AddBEnginePlayer(root);

            Require(Path.GetFullPath(Application.dataPath) == Path.GetFullPath(workspace.AssetsPath),
                "Player composition did not set Application.dataPath to the workspace Assets path.");
            var fresh = BAsset.Load<Sprite>(spritePath) ??
                        throw new InvalidOperationException("The Player Sprite fixture did not reload.");
            Require(!ReferenceEquals(stale, fresh) && fresh.assetPath == "Assets/Player.sprite.yaml",
                "Player composition retained a stale BAsset instance or bound it before dataPath initialization.");
            Require(ResolveTexture(fresh) == "Assets/PlayerPacked.png",
                "Player composition retained the stale Atlas index instead of discovering the project Atlas.");
        }
        finally
        {
            SetDataPath(previousDataPath);
            BAsset.ClearLoadedAssets();
            ClearAtlasResolver();
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void WriteAssetFixtures(ProjectWorkspace workspace)
    {
        File.WriteAllBytes(Path.Combine(workspace.AssetsPath, "Player.png"), [1]);
        File.WriteAllBytes(Path.Combine(workspace.AssetsPath, "PlayerPacked.png"), [2]);
        new Sprite { name = "Player", Texture = "Assets/Player.png" }
            .Save(Path.Combine(workspace.AssetsPath, "Player.sprite.yaml"));
        new TextureAtlas
        {
            name = "Player Atlas",
            Width = 1,
            Height = 1,
            Texture = "Assets/PlayerPacked.png",
            SpriteReferences = ["Assets/Player.sprite.yaml"],
            Sprites =
            [
                new TextureAtlasSprite
                {
                    Name = "Player", Source = "Assets/Player.sprite.yaml", Width = 1, Height = 1
                }
            ]
        }.Save(Path.Combine(workspace.AssetsPath, "Player.atlas.yaml"));
    }

    private static string ResolveTexture(Sprite sprite)
    {
        var resolver = ResolverType();
        var method = resolver.GetMethod("Resolve", BindingFlags.Static | BindingFlags.NonPublic,
            binder: null, types: [typeof(Sprite)], modifiers: null) ??
                     throw new MissingMethodException(resolver.FullName, "Resolve(Sprite)");
        var renderData = method.Invoke(null, [sprite]) ??
                         throw new InvalidOperationException("Atlas resolution returned no render data.");
        return (string)(renderData.GetType().GetProperty("Texture")?.GetValue(renderData) ?? string.Empty);
    }

    private static void ClearAtlasResolver()
    {
        var resolver = ResolverType();
        resolver.GetMethod("Clear", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
    }

    private static Type ResolverType() => typeof(BAsset).Assembly.GetType(
        "BEngine.TextureAtlasResolver", throwOnError: true)!;

    private static void SetDataPath(string value) => typeof(Application).GetProperty(
        nameof(Application.dataPath), BindingFlags.Static | BindingFlags.Public)!
        .GetSetMethod(nonPublic: true)!.Invoke(null, [value]);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
