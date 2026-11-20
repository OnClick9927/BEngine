# Source layout

`src` contains built-in Core modules, installable feature packages and host
composition. Keep new code with the module, package, or host that owns it.

| Directory | Responsibility |
| --- | --- |
| `Core` | Three built-in assemblies (`BEngine.Core`, `BEngine.Core.Editor`, `BEngine.Player`) and their source modules. |
| `Packages/<Package>` | Installable feature code, package metadata, resources, and package-owned tooling. |
| `Platform` | Cross-platform contracts, host implementations, target build modules, native/browser bridges, and platform-source export tooling. |
| `Hub` | Launcher and package-catalog application. |

The sibling `CommonPackages` BEngine project owns 17 optional packages under
`CommonPackages/Assets/Packages`: Jobs, Reactive, StandardAsset, ArtImport2D,
Codex, Cinemachine, Localization, PixelPerfect2D, PostProcessing,
PropertyAttributes, ShaderGraph, SpriteShape2D, SpriteSkinning2D, Testing,
TiledMap, Timeline and VisualScripting. Maintain and export these through the
engine's Package Manager; they are outside the engine source solution and its
bundled-package inventory. See [CommonPackages](../../CommonPackages/README.md).

Shared source, build configuration, export scripts and contract probes live
outside `src`, under `Development`. See [the development layout](../README.md).

Each Core assembly owns its source folders: `Core/BEngine.Core` contains Foundation
objects and scenes, reusable values, serialization, async execution, coroutines,
runtime assets and pooling; `Core/BEngine.Core.Editor` contains compilation, shared
editor services, asset authoring and Player build workflows; `Core/BEngine.Player`
contains Player startup and content sessions. These built-in modules have no
package manifests and cannot be disabled by a project. Platform interfaces live in
`Platform/Abstractions` and `Platform/Contracts`. Feature package manifests declare
package dependencies and built-in assembly `hostReferences`.

The solution mirrors physical ownership. After changing projects or source layout,
run `./Development/Scripts/SyncSolutionLayout.ps1 -Check` from the Engine repository directory.
Use `./Development/Tools/Test-PackageBoundaries.ps1` to check the package dependency graph.
Validation output and generated files belong in the repository `Temp` directory;
test source belongs in `Development/Tools/Tests`.

See [Core](../../src/Core/README.md), [Visual Studio](VisualStudio.md), and
[platform ownership](../Tools/PlatformIntegrationOwnership.md) for the detailed boundaries.
Use package manifests and current source for present ownership.
