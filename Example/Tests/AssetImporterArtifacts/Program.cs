using System.Buffers.Binary;
using System.Text;
using BEngine.Editor;
using BEngine.ProjectSystem.Editor;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.AssetDatabase;

namespace BEngine.ExampleTests.AssetImporterArtifacts;

internal static class Program
{
    private const string ProbeSuffix = ".codex-import-probe";

    private static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), $"BEngineImporterArtifacts_{Guid.NewGuid():N}");
        try
        {
            AssetTypeRegistry.Register<DefaultAsset>(ProbeSuffix, "Importer Probe",
                importerType: typeof(ProbeImporter));
            Run(root);
            Console.WriteLine(
                "ASSET_IMPORTER_ARTIFACTS_OK|default-copy,png-normalize,settings-fingerprint," +
                "importer-type-fingerprint,atomic-failure");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"ASSET_IMPORTER_ARTIFACTS_FAILED|{exception}");
            return 1;
        }
        finally
        {
            AssetTypeRegistry.Unregister(ProbeSuffix);
            TryDelete(root);
        }
    }

    private static void Run(string root)
    {
        var workspace = ProjectWorkspaceFactory.Create(root, "Importer Artifacts");
        var data = Path.Combine(workspace.AssetsPath, "ImporterArtifacts");
        Directory.CreateDirectory(data);

        var binaryPath = Path.Combine(data, "payload.bin");
        var binary = new byte[] { 0, 4, 8, 15, 16, 23, 42, 255 };
        File.WriteAllBytes(binaryPath, binary);

        var pngPath = Path.Combine(data, "image.png");
        var png = CreatePng();
        File.WriteAllBytes(pngPath, png);

        var probePath = Path.Combine(data, "payload" + ProbeSuffix);
        File.WriteAllText(probePath, "source-v1");

        var database = new ProjectAssetDatabase(workspace);
        database.Refresh();

        var binaryRecord = Required(database, "Assets/ImporterArtifacts/payload.bin");
        Require(File.ReadAllBytes(binaryRecord.ArtifactPath).AsSpan().SequenceEqual(binary),
            "DefaultImporter did not copy Source bytes to the Artifact unchanged.");

        var textureRecord = Required(database, "Assets/ImporterArtifacts/image.png");
        var normalizedPng = File.ReadAllBytes(textureRecord.ArtifactPath);
        Require(!normalizedPng.AsSpan().SequenceEqual(png) && ReadPngSize(normalizedPng) == (2, 2),
            "TextureImporter did not decode and re-encode the PNG Artifact.");

        var probeRecord = Required(database, "Assets/ImporterArtifacts/payload" + ProbeSuffix);
        Require(ProbeImporter.importCount == 1 &&
                File.ReadAllText(probeRecord.ArtifactPath) == "ProbeImporter|default|source-v1",
            "The external AssetImporter override did not produce the initial Artifact.");

        database.Refresh();
        Require(ProbeImporter.importCount == 1,
            "An unchanged Source/importer/settings fingerprint rebuilt the Artifact.");

        var meta = BEngine.YamlUtility.Load<AssetMetaDocument>(probePath + ".meta");
        meta.Settings = new Dictionary<string, string>
        {
            ["z"] = "last",
            ["mode"] = "configured",
            ["a"] = "first"
        };
        meta.Save(probePath + ".meta");
        database.Refresh();
        probeRecord = Required(database, "Assets/ImporterArtifacts/payload" + ProbeSuffix);
        Require(ProbeImporter.importCount == 2 &&
                File.ReadAllText(probeRecord.ArtifactPath) == "ProbeImporter|configured|source-v1",
            "Changing import settings did not rebuild the Artifact.");

        meta = BEngine.YamlUtility.Load<AssetMetaDocument>(probePath + ".meta");
        meta.Settings = new Dictionary<string, string>
        {
            ["a"] = "first",
            ["mode"] = "configured",
            ["z"] = "last"
        };
        meta.Save(probePath + ".meta");
        database.Refresh();
        Require(ProbeImporter.importCount == 2,
            "Import settings dictionary order changed the deterministic fingerprint.");

        AssetTypeRegistry.Register<DefaultAsset>(ProbeSuffix, "Importer Probe V2",
            importerType: typeof(ReplacementProbeImporter));
        database.Refresh();
        probeRecord = Required(database, "Assets/ImporterArtifacts/payload" + ProbeSuffix);
        Require(ReplacementProbeImporter.importCount == 1 &&
                File.ReadAllText(probeRecord.ArtifactPath) == "ReplacementProbeImporter|source-v1",
            "Changing only the resolved importer type did not rebuild the Artifact.");

        var stableArtifact = File.ReadAllBytes(probeRecord.ArtifactPath);
        meta = BEngine.YamlUtility.Load<AssetMetaDocument>(probePath + ".meta");
        meta.Settings["throw"] = bool.TrueString;
        meta.Save(probePath + ".meta");
        var failure = Capture(() => database.Refresh());
        Require(failure is InvalidOperationException &&
                File.ReadAllBytes(probeRecord.ArtifactPath).AsSpan().SequenceEqual(stableArtifact),
            "A failed importer replaced or damaged the previously committed Artifact.");
        Require(!Directory.EnumerateFiles(Path.GetDirectoryName(probeRecord.ArtifactPath)!, "*.importing")
                .Any(),
            "A failed importer left a temporary Artifact behind.");

        var textureArtifact = File.ReadAllBytes(textureRecord.ArtifactPath);
        File.WriteAllText(pngPath, "not a png");
        failure = Capture(() => database.Refresh());
        Require(failure is InvalidDataException &&
                File.ReadAllBytes(textureRecord.ArtifactPath).AsSpan().SequenceEqual(textureArtifact),
            "A rejected PNG replaced the last valid Texture Artifact.");
    }

    private static AssetRecord Required(ProjectAssetDatabase database, string path) =>
        database.GetRecord(path) ?? throw new InvalidOperationException($"Missing AssetRecord: {path}");

    private static Exception? Capture(Action action)
    {
        try { action(); return null; }
        catch (Exception exception) { return exception; }
    }

    private static byte[] CreatePng()
    {
        using var bitmap = new System.Drawing.Bitmap(2, 2,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        bitmap.SetPixel(0, 0, System.Drawing.Color.FromArgb(255, 12, 34, 56));
        bitmap.SetPixel(1, 0, System.Drawing.Color.FromArgb(200, 78, 90, 123));
        bitmap.SetPixel(0, 1, System.Drawing.Color.FromArgb(160, 210, 111, 9));
        bitmap.SetPixel(1, 1, System.Drawing.Color.FromArgb(255, 1, 2, 3));
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }

    private static (int Width, int Height) ReadPngSize(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        Require(bytes.Length >= 24 && bytes[..8].SequenceEqual(signature),
            "Texture Artifact is not a PNG.");
        return (BinaryPrimitives.ReadInt32BigEndian(bytes[16..20]),
            BinaryPrimitives.ReadInt32BigEndian(bytes[20..24]));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void TryDelete(string path)
    {
        if (!Directory.Exists(path)) return;
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public sealed class ProbeImporter : AssetImporter
    {
        public static int importCount;

        public override void Import(AssetImportContext context)
        {
            importCount++;
            var mode = context.settings.GetValueOrDefault("mode", "default");
            context.WriteArtifact(Encoding.UTF8.GetBytes(
                $"{nameof(ProbeImporter)}|{mode}|{File.ReadAllText(context.sourcePath)}"));
        }
    }

    public sealed class ReplacementProbeImporter : AssetImporter
    {
        public static int importCount;

        public override void Import(AssetImportContext context)
        {
            importCount++;
            File.WriteAllBytes(context.artifactPath, Encoding.UTF8.GetBytes(
                $"{nameof(ReplacementProbeImporter)}|{File.ReadAllText(context.sourcePath)}"));
            if (context.settings.GetValueOrDefault("throw") == bool.TrueString)
                throw new InvalidOperationException("Expected importer failure after writing temporary output.");
        }
    }
}
