using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using BEngine.Documents;

namespace BEngine.Editor;

internal static class BuiltInAssetTypeRegistration
{
    [ModuleInitializer]
    internal static void Register()
    {
        RegisterTexture(".png");
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
        var texture = new BEngine.Texture { name = Path.GetFileName(context.SourcePath) };
        if (TryReadPngSize(context.ImportedPath, out var width, out var height))
        {
            texture.width = width;
            texture.height = height;
        }
        if (AssetImporter.GetAtPath(context.AssetPath) is TextureImporter importer) importer.ApplyTo(texture);
        return texture;
    }

    private static BEngine.Font LoadFont(AssetLoadContext context)
    {
        var font = new BEngine.Font { name = Path.GetFileName(context.SourcePath) };
        if (AssetImporter.GetAtPath(context.AssetPath) is FontImporter importer) importer.ApplyTo(font);
        return font;
    }

    private static MonoScript LoadScript(AssetLoadContext context)
    {
        var script = new MonoScript { name = Path.GetFileName(context.SourcePath) };
        script.SetImportedContents(File.ReadAllText(context.ImportedPath),
            FindScriptClass(Path.GetFileNameWithoutExtension(context.SourcePath)));
        return script;
    }

    private static Shader LoadShader(AssetLoadContext context) => new(
        Path.GetFileNameWithoutExtension(context.SourcePath), File.ReadAllText(context.ImportedPath));

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

    private static bool TryReadPngSize(string path, out int width, out int height)
    {
        width = height = 0;
        if (!Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            return false;
        Span<byte> header = stackalloc byte[24];
        using var stream = File.OpenRead(path);
        if (stream.Read(header) != header.Length ||
            !header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return false;
        width = BinaryPrimitives.ReadInt32BigEndian(header[16..20]);
        height = BinaryPrimitives.ReadInt32BigEndian(header[20..24]);
        return width > 0 && height > 0;
    }
}
