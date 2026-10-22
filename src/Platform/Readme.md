# Platform Adapters

Core owns platform contracts and application-facing Utility classes for file, path,
clock, window and input services, together with the graphics abstraction. Hosts install
a target adapter before calling engine APIs. Business code receives FileUtility,
PathUtility and the other facades through DI; platform implementations consume the SPI.
Networking and audio contracts and implementations belong to their own packages.
See [平台接口与调用入口](PlatformBoundaries.md) for the complete mapping and examples.

| Assembly | Contents |
| --- | --- |
| BEngine.Platform.DotNet | Physical IO, system paths, environment and build-target catalog |
| BEngine.Platform.Desktop | Vulkan/OpenGL devices and desktop service composition |
| BEngine.Windows | Windows registration, IME and system font rasterization shared by Editor/Player |
| BEngine.Platform.DotNet.Editor | Process execution, source watching, editor environment and collectible assembly loading |
| BEngine.Windows.Editor | Native editor windows, Win32 integration, clipboard, dialogs, screen sampling, image/text decoding and Shaderc |
| BEngine.Linux, BEngine.macOS | Desktop service composition for the target |
| BEngine.Android, BEngine.iOS, BEngine.Web | Shared runtime registration for the SDK lifecycle hosts |
| BEngine.Platform.Build | Common .NET publish, workload inspection and payload validation |
| BEngine.*.Build | Target build providers loaded through IPlatformBuildModule |

```csharp
WindowsPlatform.Initialize();
// A custom host can install its own IServiceProvider with PlatformRuntime.Initialize.
```

The Windows example is exported and exercised against Tools/Hfs/hfs.exe. Desktop
rendering supports Vulkan on Windows and OpenGL on Windows/Linux/macOS. Linux and
macOS binaries need testing on those systems.

Android, iOS and Web have build providers, SDK lifecycle templates and runtime-only
dependency exports. Their native rendering, input and audio hosts are not implemented
by these templates. Android also requires a verified mixed AOT/interpreter host.
Their build providers report these missing capabilities and refuse to publish a
logic-only application. This repository does not yet provide complete mobile/Web
players. iOS packaging requires macOS and Xcode; platform workloads are not installed
on the Windows development machine used for this export.

ExportEngine.ps1 writes BuildHosts and BuildRuntime. Android/iOS/Web subdirectories
contain the selected platform closure without GLFW, Veldrid, Editor or Launcher.
Source and exported host projects select the same closure through
BEnginePlayerPlatform. Build plugins are editor tools and never ship in a game.

All engine metadata uses BEngine.YamlUtility. .NET host metadata, NuGet metadata and
the external Codex protocol keep the formats required by those integrations.
