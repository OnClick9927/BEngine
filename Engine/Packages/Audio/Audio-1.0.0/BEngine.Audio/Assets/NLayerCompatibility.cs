using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace BEngine.Audio;

internal static class NLayerCompatibility
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    // NLayer 3.0.0 initializes short-block window indexes for bands 0..11 only.
    // Band 12 also contains coded samples and must apply its own subblock gain.
    // Initialize the decoder table before the FIRST frame (later repair cannot undo
    // contaminated overlap history). Keep this narrow shim with the pinned version
    // until the upstream decoder includes the fix; no file/sample rewriting occurs.
    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicFields, typeof(NLayer.MpegFile))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicFields, typeof(NLayer.MpegFrameDecoder))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicConstructors | DynamicallyAccessedMemberTypes.NonPublicMethods | DynamicallyAccessedMemberTypes.NonPublicFields,
        "NLayer.Decoder.LayerIIIDecoder", "NLayer")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicFields, "NLayer.Decoder.MpegStreamReader", "NLayer")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Pinned NLayer internal members are preserved explicitly by DynamicDependency.")]
    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "Pinned LayerIIIDecoder constructor is preserved explicitly by DynamicDependency.")]
    internal static NLayer.MpegFile Open(Stream stream)
    {
        var file = new NLayer.MpegFile(stream);
        try
        {
            var decoder = Value(file, "_decoder"); var layerField = Field(decoder, "_layerIIIDecoder");
            var layer = Activator.CreateInstance(layerField.FieldType, nonPublic: true) ?? throw Unsupported();
            var firstFrame = Value(Value(file, "_reader"), "_first");
            var prepare = layer.GetType().GetMethod("PrepTables", Fields) ?? throw Unsupported();
            prepare.Invoke(layer, [firstFrame]);
            var bands = Value(layer, "_sfBandIndexS") as int[] ?? throw Unsupported();
            var windows = Value(layer, "_cbwLookupS") as byte[] ?? throw Unsupported();
            if (bands.Length != 14 || windows.Length < 576 || bands[13] != 192) throw Unsupported();
            var index = bands[12] * 3; var width = bands[13] - bands[12];
            if (index < 0 || width <= 0 || index + width * 3 != 576) throw Unsupported();
            using var budget = LoopExecutionGuard.AllowIterations(width * 3 + 4);
            for (byte window = 0; window < 3; window++)
                for (var i = 0; i < width; i++) windows[index++] = window;
            layerField.SetValue(decoder, layer);
            return file;
        }
        catch { file.Dispose(); throw; }
    }
    private static object Value(object owner, string name) => Field(owner, name).GetValue(owner) ?? throw Unsupported();
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "All inspected NLayer fields are preserved by Open's DynamicDependency declarations.")]
    private static FieldInfo Field(object owner, string name) => owner.GetType().GetField(name, Fields) ?? throw Unsupported();
    private static InvalidOperationException Unsupported() => new("NLayer decoder layout changed; review the pinned short-block compatibility fix.");
}
