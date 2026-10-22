using System.Runtime.CompilerServices;
using BEngine.Serialization;
using BEngine.Platform;

namespace BEngine.Editor;

internal static class BuiltInAssetTypeRegistration
{
    private static FileUtility FileSystem => EditorServiceAccess.GetRequiredService<FileUtility>();
    private static PathUtility Paths => EditorServiceAccess.GetRequiredService<PathUtility>();
    [ModuleInitializer]
    internal static void Register()
    {
        foreach (var suffix in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".gif" }) RegisterTexture(suffix);
        RegisterFont(".ttf");
        RegisterFont(".otf");
        RegisterFont(".woff");
        RegisterFont(".woff2");
        AssetTypeRegistry.Register<MonoScript>(".cs", "Script", LoadScript,
            EditorBuiltinIcons.Assets.Script, typeof(ScriptImporter));
        RegisterShader(".shader");
        RegisterShader(".cg");
        AssetTypeRegistry.Register<Scene>(".scene.yaml", "Scene", LoadScene,
            EditorBuiltinIcons.Assets.Scene);
        AssetTypeRegistry.Register<PrefabAsset>(".prefab.yaml", "Prefab",
            EditorBuiltinIcons.Assets.Prefab);
        AssetTypeRegistry.Register<Material>(".material.yaml", "Material",
            EditorBuiltinIcons.Assets.Material);
        AssetTypeRegistry.Register<GUISkin>(GUISkin.FileExtension, nameof(GUISkin),
            context => GUISkin.Load(context.ImportedPath), EditorBuiltinIcons.Assets.Skin);
    }

    private static void RegisterTexture(string suffix) =>
        AssetTypeRegistry.Register<BEngine.Texture>(suffix, "Texture", LoadTexture,
            EditorBuiltinIcons.Assets.Image, typeof(TextureImporter));

    private static void RegisterFont(string suffix) =>
        AssetTypeRegistry.Register<BEngine.Font>(suffix, "Font", LoadFont,
            EditorBuiltinIcons.Assets.Font, typeof(FontImporter));

    private static void RegisterShader(string suffix) =>
        AssetTypeRegistry.Register<Shader>(suffix, "Shader", LoadShader,
            EditorBuiltinIcons.Assets.Shader, typeof(ShaderImporter));

    private static BEngine.Texture LoadTexture(AssetLoadContext context)
    {
        var texture = new BEngine.Texture { name = Paths.GetFileName(context.SourcePath) };
        if (TryReadImageSize(context.ImportedPath, out var width, out var height))
        {
            texture.width = width;
            texture.height = height;
        }
        if (AssetImporter.GetAtPath(context.AssetPath) is TextureImporter importer) importer.ApplyTo(texture);
        return texture;
    }

    private static BEngine.Font LoadFont(AssetLoadContext context)
    {
        var font = new BEngine.Font { name = Paths.GetFileName(context.SourcePath) };
        if (AssetImporter.GetAtPath(context.AssetPath) is FontImporter importer) importer.ApplyTo(font);
        return font;
    }

    private static MonoScript LoadScript(AssetLoadContext context)
    {
        var script = new MonoScript { name = Paths.GetFileName(context.SourcePath) };
        script.SetImportedContents(FileSystem.ReadAllText(context.ImportedPath),
            FindScriptClass(Paths.GetFileNameWithoutExtension(context.SourcePath)));
        return script;
    }

    private static Shader LoadShader(AssetLoadContext context) => new(
        Paths.GetFileNameWithoutExtension(context.SourcePath),
        FileSystem.ReadAllText(context.ImportedPath));

    private static Scene LoadScene(AssetLoadContext context)
    {
        var scene = SceneAssetSerialization.Load(context.ImportedPath);
        scene.path = context.AssetPath;
        return scene;
    }

    private static Type? FindScriptClass(string typeName) => TypeCache.GetAllTypes()
        .FirstOrDefault(type => type.Name.Equals(typeName, StringComparison.Ordinal) &&
                                (typeof(MonoBehaviour).IsAssignableFrom(type) ||
                                 typeof(ScriptableObject).IsAssignableFrom(type)));

    private static bool TryReadImageSize(string path, out int width, out int height)
    {
        width = height = 0;
        return FileSystem.FileExists(path) &&
            BEngine.Rendering.ImageCodec.TryGetInfo(FileSystem.ReadAllBytes(path), out width, out height);
    }
}
