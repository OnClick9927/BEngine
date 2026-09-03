using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using BEngine.Content;

namespace BEngine.ExampleTests.AssetBundleHotUpdate;

internal static class BuiltInResourceArchiveTests
{
    private const string SceneAddress = "Assets/Aot/AOT.scene.yaml";
    private const string UiAddress = "Assets/Aot/UI/AOT.uxml";
    private const string DuplicatePayloadAddress = "Assets/__BEngine/Test/AOT-copy.scene.yaml";
    private const string ShaderAddress = "Assets/Resources/Shaders/Test/Test.vulkan.vert.glsl";
    private const string SplashAddress = "Assets/__BEngine/Player/Splash.png";
    private const string RuntimeMetadataAddress = "Assets/__BEngine/Player/runtime.bmeta";

    internal static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"BEngineBuiltInResources_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var firstPath = Path.Combine(root, "first.bresources");
            var secondPath = Path.Combine(root, "second.bresources");
            var sceneBytes = Encoding.UTF8.GetBytes("scene: AOT\nroot: Startup\n");
            var uiBytes = Encoding.UTF8.GetBytes("<ui:UXML><ui:Button text=\"Check Update\" /></ui:UXML>");
            var shaderBytes = Encoding.UTF8.GetBytes(
                "float4 bengine_archive_plaintext_probe() : SV_Target { return 1; }");
            var pngSignature = new byte[]
            {
                0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a
            };
            var pngBytes = new byte[64 * 1024 + 17];
            pngSignature.CopyTo(pngBytes, 0);
            for (var index = pngSignature.Length; index < pngBytes.Length; index++)
                pngBytes[index] = (byte)(index * 31 + 17);
            var runtimeMetadataBytes = Encoding.UTF8.GetBytes(
                "BENGBMET-runtime-metadata-plaintext-probe");
            var scene = BuiltInResourceArchiveWriteEntry.FromMemory(
                SceneAddress,
                sceneBytes,
                "BEngine.Scene",
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                importer: "SceneImporter",
                importerSettings: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["format"] = "yaml",
                    ["runtime"] = "aot"
                });
            var ui = BuiltInResourceArchiveWriteEntry.FromMemory(
                UiAddress,
                uiBytes,
                "BEngine.UIElements.VisualTreeAsset",
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                importer: "UxmlImporter");
            var duplicatePayload = BuiltInResourceArchiveWriteEntry.FromMemory(
                DuplicatePayloadAddress,
                sceneBytes,
                "BEngine.Scene",
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                importer: "SceneImporter");
            var shader = BuiltInResourceArchiveWriteEntry.FromMemory(
                ShaderAddress,
                shaderBytes,
                "ShaderSource",
                Guid.Parse("44444444-4444-4444-4444-444444444444"),
                importer: "BuiltInResource");
            var splash = BuiltInResourceArchiveWriteEntry.FromMemory(
                SplashAddress,
                pngBytes,
                "Texture2D",
                Guid.Parse("55555555-5555-5555-5555-555555555555"),
                importer: "BuiltInResource");
            var runtimeMetadata = BuiltInResourceArchiveWriteEntry.FromMemory(
                RuntimeMetadataAddress,
                runtimeMetadataBytes,
                "RuntimeMetadata",
                Guid.Parse("66666666-6666-6666-6666-666666666666"),
                importer: "BuiltInResource");

            BuiltInResourceArchive.Write(firstPath,
                [ui, shader, scene, splash, duplicatePayload, runtimeMetadata]);
            BuiltInResourceArchive.Write(secondPath,
                [runtimeMetadata, duplicatePayload, splash, scene, shader, ui]);
            var rawArchiveBytes = File.ReadAllBytes(firstPath);
            TestAssert.That(rawArchiveBytes.SequenceEqual(File.ReadAllBytes(secondPath)),
                "Built-in resource archive output was not deterministic across input order.");
            AssertObfuscated(rawArchiveBytes, Encoding.UTF8.GetBytes(SceneAddress), "asset address");
            AssertObfuscated(rawArchiveBytes, sceneBytes, "scene YAML");
            AssertObfuscated(rawArchiveBytes, shaderBytes, "Shader source");
            AssertObfuscated(rawArchiveBytes, pngSignature, "PNG signature");
            AssertObfuscated(rawArchiveBytes, "BENGBMET"u8, "runtime metadata magic");

            var archive = BuiltInResourceArchive.Open(firstPath);
            TestAssert.That(archive.Entries.Count == 6,
                "Built-in resource archive did not expose every index entry.");
            TestAssert.That(
                archive.EnumerateAddresses("Assets/Aot").SequenceEqual([SceneAddress, UiAddress]),
                "Built-in resource archive address enumeration was incomplete or unstable.");
            TestAssert.That(archive.TryGetEntry(SceneAddress.ToUpperInvariant(), out var sceneEntry) &&
                            sceneEntry.AssetType == "BEngine.Scene" &&
                            sceneEntry.ImporterSettings["runtime"] == "aot",
                "Built-in resource metadata lookup did not preserve identity and importer settings.");
            TestAssert.That(archive.TryGetEntry(DuplicatePayloadAddress, out var duplicatePayloadEntry) &&
                            duplicatePayloadEntry.PayloadOffset == sceneEntry.PayloadOffset &&
                            duplicatePayloadEntry.Size == sceneEntry.Size &&
                            duplicatePayloadEntry.Sha256 == sceneEntry.Sha256,
                "Built-in resource archive did not deduplicate identical plaintext payloads.");
            TestAssert.That(archive.ReadBytes(SceneAddress).SequenceEqual(sceneBytes),
                "Built-in resource synchronous item read returned different bytes.");
            TestAssert.That((await archive.ReadBytesAsync(UiAddress).ConfigureAwait(false)).SequenceEqual(uiBytes),
                "Built-in resource asynchronous item read returned different bytes.");
            TestAssert.That(archive.ReadBytes(SplashAddress).SequenceEqual(pngBytes) &&
                            archive.ReadBytes(RuntimeMetadataAddress).SequenceEqual(runtimeMetadataBytes),
                "Built-in resource archive did not restore binary packaged resources.");

            var wholeArchiveTamperPath = Path.Combine(root, "whole-tamper.bresources");
            File.Copy(firstPath, wholeArchiveTamperPath);
            FlipLastByte(wholeArchiveTamperPath);
            TestAssert.Throws<InvalidDataException>(
                () => BuiltInResourceArchive.Open(wholeArchiveTamperPath),
                "integrity");

            var itemTamperPath = Path.Combine(root, "item-tamper.bresources");
            File.Copy(firstPath, itemTamperPath);
            var itemArchive = BuiltInResourceArchive.Open(itemTamperPath);
            TestAssert.That(itemArchive.TryGetEntry(UiAddress, out var uiEntry),
                "Built-in resource archive lost the UI entry before the item tamper test.");
            FlipPayloadByte(itemTamperPath, uiEntry);
            TestAssert.Throws<InvalidDataException>(
                () => itemArchive.ReadBytes(UiAddress),
                "integrity");

            var legacyPath = Path.Combine(root, "legacy-v1.bresources");
            WriteLegacyV1Archive(legacyPath, SceneAddress, sceneBytes);
            var legacyArchive = BuiltInResourceArchive.Open(legacyPath);
            TestAssert.That(legacyArchive.ReadBytes(SceneAddress).SequenceEqual(sceneBytes),
                "Built-in resource archive did not preserve v1 read compatibility.");

            var duplicatePath = Path.Combine(root, "duplicate.bresources");
            var duplicate = BuiltInResourceArchiveWriteEntry.FromMemory(
                SceneAddress.ToUpperInvariant(),
                sceneBytes,
                "BEngine.Scene",
                Guid.Parse("33333333-3333-3333-3333-333333333333"));
            TestAssert.Throws<InvalidDataException>(
                () => BuiltInResourceArchive.Write(duplicatePath, [scene, duplicate]),
                "duplicate");

            TestAssert.Throws<InvalidDataException>(
                () => BuiltInResourceArchiveWriteEntry.FromMemory(
                    "Assets/Aot/../../outside.dll",
                    sceneBytes,
                    "System.Reflection.Assembly"),
                "address");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void FlipLastByte(string path)
    {
        var bytes = File.ReadAllBytes(path);
        TestAssert.That(bytes.Length > 0, "Built-in resource archive was unexpectedly empty.");
        bytes[^1] ^= 0xff;
        File.WriteAllBytes(path, bytes);
    }

    private static void FlipPayloadByte(string path, BuiltInResourceArchiveEntry entry)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Span<byte> header = stackalloc byte[24];
        stream.ReadExactly(header);
        var headerSize = BinaryPrimitives.ReadUInt16LittleEndian(header[10..]);
        var indexLength = BinaryPrimitives.ReadInt64LittleEndian(header[16..]);
        stream.Position = checked(headerSize + indexLength + entry.PayloadOffset);
        var value = stream.ReadByte();
        TestAssert.That(value >= 0, "Built-in resource payload was unexpectedly empty or truncated.");
        stream.Position--;
        stream.WriteByte((byte)(value ^ 0xff));
        stream.Flush(flushToDisk: true);
    }

    private static void AssertObfuscated(
        ReadOnlySpan<byte> archive,
        ReadOnlySpan<byte> plaintext,
        string description)
    {
        TestAssert.That(archive.IndexOf(plaintext) < 0,
            $"Built-in resource archive exposed the {description} as plaintext.");
    }

    private static void WriteLegacyV1Archive(string path, string address, byte[] payload)
    {
        var payloadHash = SHA256.HashData(payload);
        byte[] index;
        using (var indexStream = new MemoryStream())
        using (var writer = new BinaryWriter(indexStream, Encoding.UTF8, leaveOpen: true))
        {
            WriteLegacyString(writer, address);
            var guid = Guid.Parse("77777777-7777-7777-7777-777777777777");
            writer.Write(guid.ToByteArray());
            writer.Write(guid.ToByteArray());
            writer.Write(0L);
            WriteLegacyString(writer, "BEngine.Scene");
            WriteLegacyString(writer, "SceneImporter");
            writer.Write(0);
            writer.Write(0L);
            writer.Write((long)payload.Length);
            writer.Write(payloadHash);
            writer.Flush();
            index = indexStream.ToArray();
        }

        var body = new byte[index.Length + payload.Length];
        index.CopyTo(body, 0);
        payload.CopyTo(body, index.Length);
        using var output = new MemoryStream();
        using (var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("BENGBRES"u8);
            writer.Write((ushort)1);
            writer.Write((ushort)64);
            writer.Write(1);
            writer.Write((long)index.Length);
            writer.Write((long)payload.Length);
            writer.Write(SHA256.HashData(body));
            TestAssert.That(output.Position == 64,
                "Legacy built-in resource fixture header size is inconsistent.");
            writer.Write(body);
            writer.Flush();
        }
        File.WriteAllBytes(path, output.ToArray());
    }

    private static void WriteLegacyString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}
