using System.Runtime.CompilerServices;
using BEngine.Hosting;
using BEngine.Platform;
using BEngine.Rendering;

namespace BEngine;

internal static class GraphicsModule
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        RuntimeAssetCodecRegistry.RegisterFileLoader<Texture>(path => GraphicsAssetLoading.LoadFile(path, typeof(Texture)) as Texture);
        RuntimeAssetCodecRegistry.RegisterFileLoader<Font>(path => GraphicsAssetLoading.LoadFile(path, typeof(Font)) as Font);
        RuntimeAssetCodecRegistry.RegisterFileLoader<Shader>(path => GraphicsAssetLoading.LoadFile(path, typeof(Shader)) as Shader);
        RuntimeAssetCodecRegistry.Register<Texture>(DecodeTexture);
        RuntimeAssetCodecRegistry.Register<Font>((bytes, path) => new Font { name = Path.GetFileName(path) });
        RuntimeResourceCodecs.Register(new TextureResourceCodec());
        RuntimeResourceCodecs.Register(new MaterialResourceCodec());
        RuntimeResourceCodecs.Register(new ShaderResourceCodec());
        RuntimeResourceCodecs.Register(new FontResourceCodec());
        RuntimeObjectCodecs.Register(new MaterialCodec());
        RuntimeObjectCodecs.Register(new ShaderCodec());
        EncodedImages.Current = new EncodedImageCodec();
        HostStartupRenderers.Factory = static device => new HostStartupRenderer(device);
        RuntimeAssemblyCleanup.Register(static assembly =>
        {
            RenderContributorRegistry.UnregisterAssembly(assembly);
            TextureImportResolver.UnregisterAssembly(assembly);
            if (assembly != typeof(GraphicsModule).Assembly) return;
            if (EncodedImages.Current?.GetType().Assembly == assembly) EncodedImages.Current = null;
            if (HostStartupRenderers.Factory?.Method.DeclaringType?.Assembly == assembly) HostStartupRenderers.Factory = null;
        });
    }

    private static Texture DecodeTexture(ReadOnlyMemory<byte> bytes, string path)
    {
        if (!ImageCodec.TryGetInfo(bytes.Span, out var width, out var height))
            throw new InvalidDataException($"Resource Texture '{path}' is not a supported image.");
        return new Texture { width = width, height = height };
    }

    private sealed class EncodedImageCodec : IEncodedImageCodec
    {
        public bool TryGetInfo(byte[] bytes, out int width, out int height) => ImageCodec.TryGetInfo(bytes, out width, out height);
        public bool TryDecode(ReadOnlySpan<byte> bytes, out int width, out int height, out byte[] rgba) => ImageCodec.TryDecode(bytes, out width, out height, out rgba);
    }
    private sealed class MaterialCodec : RuntimeObjectCodec
    {
        public override Type ObjectType => typeof(Material);
        public override string WriteReference(BObject value)
        {
            var material = (Material)value;
            return string.IsNullOrWhiteSpace(material.assetPath) && material.name.StartsWith("Default ", StringComparison.Ordinal)
                ? $"builtin-material:{Uri.EscapeDataString(material.shader.shaderName)}|{Uri.EscapeDataString(material.name)}" : material.assetPath;
        }
        public override BObject? ReadReference(string reference, IReadOnlyDictionary<string, string>? legacyFields = null)
        {
            if (!reference.StartsWith("builtin-material:", StringComparison.Ordinal)) return BAsset.Load<Material>(reference);
            var parts = reference["builtin-material:".Length..].Split('|', 2);
            if (parts.Length != 2) throw new InvalidDataException("Invalid built-in material reference.");
            return Material.GetBuiltIn(Uri.UnescapeDataString(parts[0]), Uri.UnescapeDataString(parts[1]));
        }
        public override BObject Clone(BObject value)
        {
            var source = (Material)value;
            return new Material(source) { name = source.name, hideFlags = source.hideFlags };
        }
    }
    private sealed class ShaderCodec : RuntimeObjectCodec
    {
        public override Type ObjectType => typeof(Shader);
        public override BObject CreateCloneInstance() => new Shader(string.Empty);
        public override string WriteReference(BObject value) => value is Shader shader && string.IsNullOrWhiteSpace(shader.assetPath)
            ? $"builtin-shader:{Uri.EscapeDataString(shader.shaderName)}" : ((Shader)value).assetPath;
        public override BObject? ReadReference(string reference, IReadOnlyDictionary<string, string>? legacyFields = null) =>
            reference.StartsWith("builtin-shader:", StringComparison.Ordinal)
                ? Shader.Find(Uri.UnescapeDataString(reference["builtin-shader:".Length..])) : BAsset.Load<Shader>(reference);
    }
}
