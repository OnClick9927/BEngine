namespace BEngine.Platform;

public enum EditorColorSpace { Gray, Rgb, Cmyk }

/// <summary>Normalized 16-bit source components, in the declared color-space order.</summary>
public readonly record struct EditorColorSample(ushort X, ushort Y = 0, ushort Z = 0, ushort W = 0);

/// <summary>Optional native editor color management, independent of any source-file format.
/// The source profile is borrowed only during OpenToSrgb; each call creates an independent lease.</summary>
public interface IEditorColorManagementHost
{
    IEditorColorTransform OpenToSrgb(ReadOnlyMemory<byte> sourceProfile, EditorColorSpace sourceSpace);
}

/// <summary>Operation-owned transform, used by one import operation at a time.
/// Output is normalized sRGB; input memory is borrowed during Convert only.</summary>
public interface IEditorColorTransform : IDisposable
{
    void Convert(ReadOnlySpan<EditorColorSample> source, Span<EditorColorSample> destination);
}
