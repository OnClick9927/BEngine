# Platform Adapters

Core owns platform contracts and application-facing Utility classes for file, path,
clock, window and input services, together with the graphics abstraction. Hosts install
a target adapter before calling engine APIs. Business code receives FileUtility,
PathUtility and the other facades through DI; platform implementations consume the SPI.
Optional subsystems contribute their own services through these public contracts.
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

```csharp
WindowsPlatform.Initialize();
// A custom host can install its own IServiceProvider with PlatformRuntime.Initialize.
```

The Windows example is exported and exercised against Tools/Hfs/hfs.exe. Desktop
rendering supports Vulkan on Windows and OpenGL on Windows/Linux/macOS. Linux and
macOS binaries need testing on those systems.

Platform contains no concrete package references, package entry types, build
providers, or application lifecycle templates. Distribution tooling owns those
composition decisions and consumes these adapters through public contracts.
The editor launcher likewise invokes a distribution entry through
`IEditorApplicationEntry` and an adjacent `editor.application.json` descriptor.
Changing the selected application does not require recompiling the launcher.

Android, iOS and Web adapters provide target runtime services. Lifecycle glue and
publishing policy belong to their consumers. iOS packaging requires macOS and
Xcode; mobile applications require validation on their target devices.

All engine metadata uses BEngine.YamlUtility. .NET host metadata, NuGet metadata and
the external Codex protocol keep the formats required by those integrations.
