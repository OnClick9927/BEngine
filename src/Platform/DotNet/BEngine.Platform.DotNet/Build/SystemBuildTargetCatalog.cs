using BEngine.Platform;
using BEngine.Rendering.Rhi;
using System.Runtime.InteropServices;
namespace BEngine.Build;
public sealed class SystemBuildTargetCatalog : IBuildTargetCatalog
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
        Target("android-x64", BuildTargetPlatform.Android, BuildArchitecture.X64, "android-x64",
            ManagedCodeRuntimeKind.AotInterpreter, false, GraphicsBackend.OpenGLES),
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
    public IReadOnlyList<BuildTargetDescriptor> All => Array.AsReadOnly(Targets);

    /// <summary>Architecture-neutral platforms exposed by Editor and project settings.</summary>
    public IReadOnlyList<BuildPlatformDescriptor> Platforms =>
        Array.AsReadOnly(PlatformSelections);

    public BuildTargetDescriptor Get(string targetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        var concrete = Matches3381();
        [global::BEngine.SkipLoopGuard]
        global::BEngine.Build.BuildTargetDescriptor? Matches3381()
        {
            var matchSource3381 = Targets;
            ArgumentNullException.ThrowIfNull(matchSource3381, "source");
            global::System.ReadOnlySpan<global::BEngine.Build.BuildTargetDescriptor> matchValues3381 = matchSource3381;
            foreach (var target in matchValues3381)
            {
                if (target.TargetId.Equals(targetId, StringComparison.OrdinalIgnoreCase))
                    return target;
            }
        
            return default!;
        }
        if (concrete is not null) return concrete;
        var platform = Matches3571();
        [global::BEngine.SkipLoopGuard]
        global::BEngine.Build.BuildPlatformDescriptor? Matches3571()
        {
            var matchSource3571 = PlatformSelections;
            ArgumentNullException.ThrowIfNull(matchSource3571, "source");
            global::System.ReadOnlySpan<global::BEngine.Build.BuildPlatformDescriptor> matchValues3571 = matchSource3571;
            foreach (var selection in matchValues3571)
            {
                if (selection.PlatformId.Equals(targetId, StringComparison.OrdinalIgnoreCase))
                    return selection;
            }
        
            return default!;
        }
        return platform is not null
            ? Resolve(platform.Platform)
            : throw new KeyNotFoundException($"Unknown BEngine build target or platform '{targetId}'.");
    }

    public bool TryGet(string targetId, out BuildTargetDescriptor descriptor)
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

    public BuildPlatformDescriptor GetPlatform(string platformId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platformId);
        return Matches4409() ?? throw new KeyNotFoundException($"Unknown BEngine build platform '{platformId}'.");
        [global::BEngine.SkipLoopGuard]
        global::BEngine.Build.BuildPlatformDescriptor? Matches4409()
        {
            var matchSource4409 = PlatformSelections;
            ArgumentNullException.ThrowIfNull(matchSource4409, "source");
            global::System.ReadOnlySpan<global::BEngine.Build.BuildPlatformDescriptor> matchValues4409 = matchSource4409;
            foreach (var selection in matchValues4409)
            {
                if (selection.PlatformId.Equals(platformId, StringComparison.OrdinalIgnoreCase))
                    return selection;
            }
        
            return default!;
        }
    }

    public string GetPlatformId(BuildTargetPlatform platform) =>
        PlatformSelections.FirstOrDefault(selection => selection.Platform == platform)?.PlatformId ??
        throw new KeyNotFoundException($"Unknown BEngine build platform '{platform}'.");

    /// <summary>
    /// Converts both legacy concrete ids (for example windows-x64) and current logical ids
    /// to the architecture-neutral value persisted by Player Settings.
    /// </summary>
    public string NormalizePlatformId(string targetOrPlatformId) =>
        GetPlatformId(Get(targetOrPlatformId).Platform);

    public BuildTargetDescriptor Resolve(BuildTargetPlatform platform) =>
        Resolve(platform, PreferredArchitecture(platform, PlatformUtility.Current));

    public BuildTargetDescriptor Resolve(
        BuildTargetPlatform platform,
        BuildArchitecture architecture)
    {
        return Matches5545() ?? throw new PlatformNotSupportedException($"BEngine has no {platform} target for {architecture}.");
        [global::BEngine.SkipLoopGuard]
        global::BEngine.Build.BuildTargetDescriptor? Matches5545()
        {
            var matchSource5545 = Targets;
            ArgumentNullException.ThrowIfNull(matchSource5545, "source");
            global::System.ReadOnlySpan<global::BEngine.Build.BuildTargetDescriptor> matchValues5545 = matchSource5545;
            foreach (var target in matchValues5545)
            {
                if (target.Platform == platform && target.Architecture == architecture)
                    return target;
            }
        
            return default!;
        }
    }

    public BuildTargetDescriptor InferCurrentDesktop() =>
        InferCurrentDesktop(PlatformUtility.Current);

    public BuildTargetDescriptor InferCurrentDesktop(PlatformUtility platformService)
    {
        ArgumentNullException.ThrowIfNull(platformService);
        var platform = platformService.IsWindows
            ? BuildTargetPlatform.Windows
            : platformService.IsMacOS ? BuildTargetPlatform.MacOS : BuildTargetPlatform.Linux;
        return Resolve(platform, DesktopArchitecture(platformService));
    }

    public string InferCurrentDesktopPlatformId() =>
        GetPlatformId(InferCurrentDesktop().Platform);

    private static BuildArchitecture PreferredArchitecture(
        BuildTargetPlatform platform,
        PlatformUtility platformService)
    {
        if (platform == BuildTargetPlatform.Web) return BuildArchitecture.Wasm32;
        if (platform is BuildTargetPlatform.Android or BuildTargetPlatform.IOS)
            return BuildArchitecture.Arm64;
        return IsCurrentOperatingSystem(platform, platformService)
            ? DesktopArchitecture(platformService)
            : BuildArchitecture.X64;
    }

    private static BuildArchitecture DesktopArchitecture(PlatformUtility platformService) =>
        platformService.OperatingSystemArchitecture switch
        {
            Architecture.Arm64 => BuildArchitecture.Arm64,
            _ => BuildArchitecture.X64
        };

    private static bool IsCurrentOperatingSystem(
        BuildTargetPlatform platform,
        PlatformUtility platformService) => platform switch
    {
        BuildTargetPlatform.Windows => platformService.IsWindows,
        BuildTargetPlatform.Linux => platformService.IsLinux,
        BuildTargetPlatform.MacOS => platformService.IsMacOS,
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
