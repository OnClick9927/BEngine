using BEngine.Rendering.Rhi;

namespace BEngine.Editor.Diagnostics;

public readonly record struct FrameDebugTextureBinding(
    int Slot,
    string Label,
    int Width = 0,
    int Height = 0,
    GraphicsTextureFormat Format = default,
    GraphicsTextureUsage Usage = default,
    GraphicsTextureFilter MinFilter = default,
    GraphicsTextureFilter MagFilter = default,
    GraphicsTextureAddressMode AddressMode = default);
