using BEngine.HotUpdate;
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

public static class BuildTargetCatalog
{
    private static readonly BuildTargetDescriptor[] Targets =
    [
        Target("windows-x64", BuildTargetPlatform.Windows, BuildArchitecture.X64, "win-x64",
            ManagedCodeRuntimeKind.CoreClr, true,
            GraphicsBackend.Direct3D12, GraphicsBackend.Direct3D11, GraphicsBackend.Vulkan,
            GraphicsBackend.OpenGL),
        Target("windows-arm64", BuildTargetPlatform.Windows, BuildArchitecture.Arm64, "win-arm64",
            ManagedCodeRuntimeKind.CoreClr, true,
            GraphicsBackend.Direct3D12, GraphicsBackend.Direct3D11, GraphicsBackend.Vulkan,
            GraphicsBackend.OpenGL),
        Target("linux-x64", BuildTargetPlatform.Linux, BuildArchitecture.X64, "linux-x64",
            ManagedCodeRuntimeKind.CoreClr, true, GraphicsBackend.Vulkan, GraphicsBackend.OpenGL),
        Target("linux-arm64", BuildTargetPlatform.Linux, BuildArchitecture.Arm64, "linux-arm64",
            ManagedCodeRuntimeKind.CoreClr, true, GraphicsBackend.Vulkan, GraphicsBackend.OpenGL),
        Target("macos-x64", BuildTargetPlatform.MacOS, BuildArchitecture.X64, "osx-x64",
            ManagedCodeRuntimeKind.CoreClr, true,
            GraphicsBackend.Metal, GraphicsBackend.Vulkan, GraphicsBackend.OpenGL),
        Target("macos-arm64", BuildTargetPlatform.MacOS, BuildArchitecture.Arm64, "osx-arm64",
            ManagedCodeRuntimeKind.CoreClr, true,
            GraphicsBackend.Metal, GraphicsBackend.Vulkan, GraphicsBackend.OpenGL),
        Target("android-arm64", BuildTargetPlatform.Android, BuildArchitecture.Arm64, "android-arm64",
            ManagedCodeRuntimeKind.AotInterpreter, false,
            GraphicsBackend.Vulkan, GraphicsBackend.OpenGLES),
        Target("ios-arm64", BuildTargetPlatform.IOS, BuildArchitecture.Arm64, "ios-arm64",
            ManagedCodeRuntimeKind.AotInterpreter, false, GraphicsBackend.Metal),
        Target("web-wasm", BuildTargetPlatform.Web, BuildArchitecture.Wasm32, "browser-wasm",
            ManagedCodeRuntimeKind.AotInterpreter, false,
            GraphicsBackend.WebGPU, GraphicsBackend.WebGL)
    ];

    private static readonly BuildPlatformDescriptor[] PlatformSelections =
    [
        Platform("windows", BuildTargetPlatform.Windows),
        Platform("macos", BuildTargetPlatform.MacOS),
        Platform("linux", BuildTargetPlatform.Linux),
        Platform("android", BuildTargetPlatform.Android),
        Platform("ios", BuildTargetPlatform.IOS),
        Platform("web", BuildTargetPlatform.Web)
    ];

    /// <summary>Concrete targets used internally by Player providers and manifests.</summary>
    public static IReadOnlyList<BuildTargetDescriptor> All => Array.AsReadOnly(Targets);

    /// <summary>Architecture-neutral platforms exposed by Editor and project settings.</summary>
    public static IReadOnlyList<BuildPlatformDescriptor> Platforms =>
        Array.AsReadOnly(PlatformSelections);

    public static BuildTargetDescriptor Get(string targetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        var concrete = Targets.FirstOrDefault(target =>
            target.TargetId.Equals(targetId, StringComparison.OrdinalIgnoreCase));
        if (concrete is not null) return concrete;
        var platform = PlatformSelections.FirstOrDefault(selection =>
            selection.PlatformId.Equals(targetId, StringComparison.OrdinalIgnoreCase));
        return platform is not null
            ? Resolve(platform.Platform)
            : throw new KeyNotFoundException($"Unknown BEngine build target or platform '{targetId}'.");
    }

    public static bool TryGet(string targetId, out BuildTargetDescriptor descriptor)
    {
        try
        {
            descriptor = Get(targetId);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
        {
            descriptor = null!;
            return false;
        }
    }

    public static BuildPlatformDescriptor GetPlatform(string platformId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platformId);
        return PlatformSelections.FirstOrDefault(selection =>
                   selection.PlatformId.Equals(platformId, StringComparison.OrdinalIgnoreCase)) ??
               throw new KeyNotFoundException($"Unknown BEngine build platform '{platformId}'.");
    }

    public static string GetPlatformId(BuildTargetPlatform platform) =>
        PlatformSelections.FirstOrDefault(selection => selection.Platform == platform)?.PlatformId ??
        throw new KeyNotFoundException($"Unknown BEngine build platform '{platform}'.");

    /// <summary>
    /// Converts both legacy concrete ids (for example windows-x64) and current logical ids
    /// to the architecture-neutral value persisted by Player Settings.
    /// </summary>
    public static string NormalizePlatformId(string targetOrPlatformId) =>
        GetPlatformId(Get(targetOrPlatformId).Platform);

    public static BuildTargetDescriptor Resolve(BuildTargetPlatform platform) =>
        Resolve(platform, PreferredArchitecture(platform));

    public static BuildTargetDescriptor Resolve(
        BuildTargetPlatform platform,
        BuildArchitecture architecture)
    {
        return Targets.FirstOrDefault(target =>
                   target.Platform == platform && target.Architecture == architecture) ??
               throw new PlatformNotSupportedException(
                   $"BEngine has no {platform} target for {architecture}.");
    }

    public static BuildTargetDescriptor InferCurrentDesktop()
    {
        var platform = OperatingSystem.IsWindows()
            ? BuildTargetPlatform.Windows
            : OperatingSystem.IsMacOS() ? BuildTargetPlatform.MacOS : BuildTargetPlatform.Linux;
        return Resolve(platform, DesktopArchitecture());
    }

    public static string InferCurrentDesktopPlatformId() =>
        GetPlatformId(InferCurrentDesktop().Platform);

    private static BuildArchitecture PreferredArchitecture(BuildTargetPlatform platform)
    {
        if (platform == BuildTargetPlatform.Web) return BuildArchitecture.Wasm32;
        if (platform is BuildTargetPlatform.Android or BuildTargetPlatform.IOS)
            return BuildArchitecture.Arm64;
        return IsCurrentOperatingSystem(platform)
            ? DesktopArchitecture()
            : BuildArchitecture.X64;
    }

    private static BuildArchitecture DesktopArchitecture() =>
        RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => BuildArchitecture.Arm64,
            _ => BuildArchitecture.X64
        };

    private static bool IsCurrentOperatingSystem(BuildTargetPlatform platform) => platform switch
    {
        BuildTargetPlatform.Windows => OperatingSystem.IsWindows(),
        BuildTargetPlatform.Linux => OperatingSystem.IsLinux(),
        BuildTargetPlatform.MacOS => OperatingSystem.IsMacOS(),
        _ => false
    };

    private static BuildTargetDescriptor Target(
        string id,
        BuildTargetPlatform platform,
        BuildArchitecture architecture,
        string runtimeIdentifier,
        ManagedCodeRuntimeKind runtime,
        bool builtInHost,
        params GraphicsBackend[] backends) =>
        new(id, platform, architecture, runtimeIdentifier, runtime, Array.AsReadOnly(backends), builtInHost);

    private static BuildPlatformDescriptor Platform(string id, BuildTargetPlatform platform) =>
        new(id, platform);
}
