#if GRAPHICS
using System.Runtime.CompilerServices;
using BEngine;
using BEngine.AssetBundles;

internal static class GraphicsChecks
{
    internal static void Run(Action<bool, string> check, Func<BTask<ResourceLoadResult>, ResourceLoadResult> complete,
        Func<VirtualAssetBundleEntry[], string, IResourceObjectProvider> archive, Func<string, byte[]> data)
    {
        RuntimeHelpers.RunModuleConstructor(typeof(Texture).Module.ModuleHandle);
        RuntimeHelpers.RunModuleConstructor(typeof(Sprite).Module.ModuleHandle);
        var owner = Guid.NewGuid();
        var settings = new Dictionary<string, string> { ["textureType"] = "Sprite", ["spritePivotX"] = "0.25", ["spritePivotY"] = "0.75",
            ["spriteBorderLeft"] = "2", ["spriteBorderRight"] = "3", ["sRGBTexture"] = "false", ["isReadable"] = "true",
            ["maxTextureSize"] = "1000", ["pixelsPerUnit"] = "64", ["filterMode"] = "Point", ["generateMipMaps"] = "true" };
        var entries = new[] {
            VirtualAssetBundleEntry.FromMemory("Assets/Resources/icon.png", data("icon.png"), "Texture", owner, importer: "TextureImporter", importerSettings: settings),
            VirtualAssetBundleEntry.FromMemory($"guid:{owner:N}#subasset=2800001", data("icon.png"), "Texture", Guid.NewGuid(), entry: "children/image.png", ownerGuid: owner, localIdentifier: 2800001, importer: "TextureImporter", importerSettings: settings),
            VirtualAssetBundleEntry.FromMemory($"guid:{owner:N}#subasset=21300000", [], "Sprite", Guid.NewGuid(), entry: "children/sprite.bin", ownerGuid: owner, localIdentifier: 21300000),
            VirtualAssetBundleEntry.FromMemory("Assets/Resources/plain.png", data("icon.png"), "Texture", importer: "TextureImporter"),
            VirtualAssetBundleEntry.FromMemory("Assets/Resources/test.shader", data("shader"), "Shader"),
            VirtualAssetBundleEntry.FromMemory("Assets/Resources/test.material.yaml", data("test.material.yaml"), "Material"),
            VirtualAssetBundleEntry.FromMemory("Assets/Resources/test.ttf", [], "Font") };
        using var manager = new VirtualAssetBundleManager(new("graphics", "v1", entries));
        manager.InitializeAsync().GetAwaiter().GetResult();
        var provider = new AssetBundleResourceProvider(manager);
        void Verify(IResourceObjectProvider objects, IResourceAssetProvider assets, string label)
        {
            check(assets.TryLoadAsset("icon", "Resources", typeof(Texture), out var value) && value is Texture, label + ": texture decodes");
            var texture = (Texture)value;
            check(texture.width == 1 && texture.height == 1 && !texture.sRGB && texture.isReadable && texture.mipMaps &&
                texture.maxSize == 1024 && texture.pixelsPerUnit == 64 && texture.filterMode == TextureFilterMode.Point, label + ": image header and import settings preserved");
            check(assets.TryLoadAsset($"guid:{owner:N}#subasset=2800001", "Resources", typeof(Texture), out var sub) && sub.assetPath == "Assets/Resources/icon.png", label + ": real texture child retains owner path");
            check(objects.TryLoadObject("icon", "Resources", typeof(Sprite), out var spriteValue), label + ": sprite constructed by Rendering2D");
            var sprite = (Sprite)spriteValue;
            check(sprite.Texture == "@bundle/Assets/Resources/icon.png", label + ": virtual source path contains exactly one prefix and one address");
            check(sprite.pivot.x == (Fix64)0.25 && sprite.pivot.y == (Fix64)0.75 && sprite.border.x == 2 && sprite.border.z == 3 &&
                sprite.pixelsPerUnit == 64 && sprite.OwnerGuid == owner.ToString("N") && sprite.LocalIdentifier == 21300000, label + ": sprite import metadata and stable identity preserved");
            check(objects.TryLoadObject($"guid:{owner:N}#subasset=21300000", "Resources", typeof(Sprite), out var alias) && ReferenceEquals(sprite, alias), label + ": sprite path and GUID alias share cache");
            check(!objects.TryLoadObject("plain", "Resources", typeof(Sprite), out _), label + ": non-sprite texture rejected");
            check(assets.TryLoadAsset("test.shader", "Resources", typeof(Shader), out var shader) && shader.name == "test", label + ": shader format owned by Graphics");
            check(assets.TryLoadAsset("test.ttf", "Resources", typeof(Font), out var font) && font.name == "test.ttf", label + ": font format and original display name owned by Graphics");
            check(assets.TryLoadAsset("test.material.yaml", "Resources", typeof(Material), out var material) && material is Material { name: "TestMaterial", renderQueue: 3010 }, label + ": material format owned by Graphics");
            var asyncResult = complete(((IAsyncResourceProvider)objects).LoadAsync("icon", "Resources", typeof(Sprite), new()));
            check(ReferenceEquals(sprite, asyncResult.Asset), label + ": async sprite uses identical cache");
        }
        Verify(provider, provider, "Virtual");
        using (var builtIn = (IDisposable)archive(entries, "graphics")) Verify((IResourceObjectProvider)builtIn, (IResourceAssetProvider)builtIn, "Archive");
        // A cold async texture load must not call the synchronous payload reader.
        var snapshot = new VirtualAssetBundleSnapshot("graphics", "v2", [entries[0]]);
        var reads = 0; var asyncReads = 0; var releases = 0;
        using var bridge = VirtualAssetBundleManager.CreateSnapshotBridge(AssetBundleCatalogSerializer.SerializeCatalog(snapshot.Catalog),
            AssetBundleCatalogSerializer.SerializeVersion(snapshot.Version), _ => { reads++; return data("icon.png"); },
            (_, _) => { asyncReads++; return BValueTask.FromResult((data("icon.png"), (Action)(() => releases++))); });
        bridge.InitializeAsync().GetAwaiter().GetResult();
        var asyncProvider = new AssetBundleResourceProvider(bridge);
        var result = complete(asyncProvider.LoadAsync("icon", "Resources", typeof(Texture), new()));
        check(result.Asset is Texture { width: 1, height: 1 } && reads == 0 && asyncReads == 1 && releases == 1, "Cold async texture reads payload once and releases lease once");
        var prepared = 0; var committed = 0; var decoded = 0;
        RuntimeAssetCodecRegistry.RegisterPrepared<Material, string>(
            (_, _) => { decoded++; return new Material { name = "override" }; },
            (_, _) => { Interlocked.Increment(ref prepared); return "override"; },
            (value, _) => { committed++; return new Material { name = value }; });
        try
        {
            var materialAddress = entries.Single(e => e.AssetType == "Material").Address;
            check(AssetBundleAssetLoader.LoadAsset(manager, materialAddress, typeof(Material)) is Material { name: "override" }, "Registered byte decoder retains priority over catalog format fallback");
            var loaded = complete(new AssetBundleResourceProvider(manager).LoadAsync(materialAddress, "Resources", typeof(Material), new()));
            check(loaded.Asset is Material { name: "override" }, "Asynchronous registered byte decoder remains active");
            check(prepared == committed && (committed == 1 || decoded == 2), "Prepared codec commit is preserved on supported schedulers");
        }
        finally { RuntimeAssetCodecRegistry.Unregister<Material>(); }
        check(AssetBundleAssetLoader.LoadAsset(manager, entries.Single(e => e.AssetType == "Material").Address, typeof(Material)) is Material { name: "TestMaterial" }, "Removing override restores the original material codec");
    }
}
#endif
