using BEngine.Platform;
using BEngine.Rendering.Rhi;
using System.Runtime.InteropServices;

namespace BEngine.Build;

public enum BuildTargetPlatform
{
    Windows,
    Linux,
    MacOS,
    Android,
    IOS,
    Web
}

public enum BuildArchitecture
{
    X64,
    Arm64,
    Arm,
    Wasm32
}

public sealed record BuildTargetDescriptor(
    string TargetId,
    BuildTargetPlatform Platform,
    BuildArchitecture Architecture,
    string RuntimeIdentifier,
    ManagedCodeRuntimeKind ManagedCodeRuntime,
    IReadOnlyList<GraphicsBackend> GraphicsBackends,
    bool HasBuiltInPlayerHost);

public sealed record BuildPlatformDescriptor(
    string PlatformId,
    BuildTargetPlatform Platform);
