using System.Reflection;
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
            new ProjectData { Name = "Player Asset Environment" }
                .Save(Path.Combine(root, ProjectWorkspace.ProjectFileName));
            var workspace = ProjectWorkspace.Open(root);
            WriteAssetFixtures(workspace);

            SetDataPath(staleAssets);
            BAsset.ClearLoadedAssets();
            ClearAtlasResolver();
            var texturePath = Path.Combine(workspace.AssetsPath, "Player.png");
            var stale = CreateSprite(texturePath);
            var staleTexture = ResolveTexture(stale);
            Require(Path.GetFileName(staleTexture).Equals("Player.png", StringComparison.OrdinalIgnoreCase) &&
                    !staleTexture.EndsWith("PlayerPacked.png", StringComparison.OrdinalIgnoreCase),
                "The stale Atlas resolver fixture was not primed against its original root.");

            _ = new ServiceCollection().AddBEnginePlayer(root);

            Require(Path.GetFullPath(Application.dataPath) == Path.GetFullPath(workspace.AssetsPath),
                "Player composition did not set Application.dataPath to the workspace Assets path.");
            var fresh = CreateSprite(texturePath);
            Require(!ReferenceEquals(stale, fresh) && fresh.assetPath == "Assets/Player.png" &&
                    fresh.OwnerGuid == stale.OwnerGuid && fresh.LocalIdentifier == stale.LocalIdentifier,
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
        var sourcePath = Path.Combine(workspace.AssetsPath, "Player.png");
        File.WriteAllBytes(sourcePath, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M/wHwAF/gL+AvzZVwAAAABJRU5ErkJggg=="));
        File.WriteAllText(sourcePath + ".meta", """
            format: BEngine.AssetMeta
            version: 1
            guid: 7a1fc8e24e4b4188a2612d1c97e71b91
            importer: TextureImporter
            assetType: Texture
            sourceHash: ''
            settings:
              textureType: Sprite
              spritePivotX: '0.5'
              spritePivotY: '0.5'
            """);
        File.WriteAllBytes(Path.Combine(workspace.AssetsPath, "PlayerPacked.png"), [2]);
        var oldDataPath = Application.dataPath;
        try
        {
            SetDataPath(workspace.AssetsPath);
            BAsset.ClearLoadedAssets();
            var sprite = CreateSprite(sourcePath);
            new TextureAtlas
            {
                name = "Player Atlas",
                Width = 1,
                Height = 1,
                Texture = "Assets/PlayerPacked.png",
                Sources = [sprite],
                Sprites =
                [
                    new TextureAtlasSprite
                    {
                        Name = "Player", Source = $"{sprite.OwnerGuid}:{sprite.LocalIdentifier}",
                        Width = 1, Height = 1
                    }
                ]
            }.Save(Path.Combine(workspace.AssetsPath, "Player.atlas.yaml"));
        }
        finally
        {
            SetDataPath(oldDataPath);
            BAsset.ClearLoadedAssets();
        }
    }

    private static Sprite CreateSprite(string texturePath)
    {
        var texture = BAsset.Load<Texture>(texturePath) ??
                      throw new InvalidOperationException("The Player Texture fixture did not load.");
        var sprite = texture.CreateSprite(new Vector2(Fix64.Half, Fix64.Half));
        sprite.name = "Player";
        return sprite;
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
