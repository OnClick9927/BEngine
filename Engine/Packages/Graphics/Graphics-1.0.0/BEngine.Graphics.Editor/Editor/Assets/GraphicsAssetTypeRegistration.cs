using EditorCapabilities = BEngine.Editor.EditorServices;
using System.Runtime.CompilerServices;
using BEngine.Platform;
namespace BEngine.Editor;
internal static class GraphicsAssetTypeRegistration
{
    private static FileUtility FileSystem => EditorCapabilities.GetRequiredService<FileUtility>();
    private static PathUtility Paths => EditorCapabilities.GetRequiredService<PathUtility>();
    [ModuleInitializer]
    internal static void Register()
    {
        PlayerResourceTextFormats.Register(".shader", ".glsl", ".glslinc", ".hlsl", ".hlslinc",
            ".cg", ".wgsl", ".msl", ".metal");
        RuntimeBuildResources.Register(typeof(Texture).Assembly.GetName().Name!, "Icons", "Shaders/Common");
        foreach(var suffix in new[]{".png",".jpg",".jpeg",".bmp",".tga",".gif"}) RegisterTexture(suffix);
        foreach(var suffix in new[]{".ttf",".otf",".woff",".woff2"}) RegisterFont(suffix);
        RegisterShader(".shader"); RegisterShader(".cg");
        AssetTypeRegistry.Register<Material>(".material.yaml","Material",EditorBuiltinIcons.Assets.Material);
        EditorAssetAdapters.Register(new TextureAssetInspector());
        EditorAssetAdapters.Register(new RenderTextureAssetInspector());
        EditorObjectSnapshotAdapters.Register(new RenderTextureSnapshotAdapter());
        EditorAssetAdapters.Register(new GraphicsAssetPreview(typeof(Font)));
        EditorAssetAdapters.Register(new GraphicsAssetPreview(typeof(Shader)));
        EditorAssetAdapters.Register(new GraphicsAssetPreview(typeof(Material)));
        EditorAssetAdapters.Register(new GraphicsSourcePreview());
    }
    private static void RegisterTexture(string suffix) =>
        AssetTypeRegistry.Register<BEngine.Texture>(suffix, "Texture", LoadTexture,
            EditorBuiltinIcons.Assets.Image, typeof(TextureImporter));

    private static void RegisterFont(string suffix) =>
        AssetTypeRegistry.Register<BEngine.Font>(suffix, "Font", LoadFont,
            EditorBuiltinIcons.Assets.Font, typeof(FontImporter));

    private static void RegisterShader(string suffix) =>
        AssetTypeRegistry.Register<Shader>(suffix, "Shader", LoadShader,
            EditorBuiltinIcons.Assets.Shader, typeof(ShaderImporter),
            static context => new TextAsset(FileSystem.ReadAllText(context.ImportedPath)));

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

    private static Shader LoadShader(AssetLoadContext context) => new(
        Paths.GetFileNameWithoutExtension(context.SourcePath),
        FileSystem.ReadAllText(context.ImportedPath));

    private static bool TryReadImageSize(string path, out int width, out int height)
    {
        width = height = 0;
        return FileSystem.FileExists(path) &&
            BEngine.Rendering.ImageCodec.TryGetInfo(FileSystem.ReadAllBytes(path), out width, out height);
    }
}
