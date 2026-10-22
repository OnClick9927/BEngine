# Engine improvements

Repository layout update (2026-10-22): sources now live in `Engine/src`, exported
engine files in `Engine/Engine`, and the Example player/content in
`Example/Project/Build`. Paths in older verification entries below describe the
layout at the time of those checks.

## Scope and constraints

- Q32.32 Fix64 remains the simulation numeric type.
- Runtime state, BTask, networking polling and updates remain on the owning main thread.
- Align existing public package APIs and behavior with Unity where applicable; document engine-specific differences.
- Game sprites and pixel artwork are the reported clarity problem.
- Preserve pre-existing workspace changes.

## Progress

- [x] Fix the 18 findings from the 2026-09-07 source review and add focused regression coverage.
- [x] Add explicit AssetBundle build groups and selective dependency-aware installation/update.
- [x] Audit public package behavior against Unity and fix misleading/no-op implementations.
- [x] Audit file/network/I/O boundaries and implement platform service adapters.
- [x] Improve pixel-art rendering, texture sampling and screen scaling with visual regression checks.
- [x] Expand descriptions, documentation, importable examples and multiple focused skills for all 8 packages.
- [x] Produce an integrated Example, export the engine, build the player and validate split updates.

## Validation

- SceneRuntimeArchitecture: Release build and console regression passed. 512 steady-state Tick frames allocated 0 bytes. 256 sprites allocated 39,136 bytes/frame after payload reuse (previously 98,528).
- AssetBundleHotUpdate: Release build and regression passed, including selective initial installation, optional dependency download, content reuse, update and restart, integrity checks and rollback. Bundled Point sampling now has a GPU-description assertion.
- Networking: Release build and regression passed, including real TCP/UDP loopback, chunked HTTP through both the socket and .NET platform adapters, cancellation, Abort and Dispose classification.
- VulkanRendering --pixel-clarity: real Vulkan offscreen render/readback passed at 640x360, 360x640 and 853x481. Point output had respectively 1024, 256 and 1024 exact source-color pixels and zero mixed-color pixels. Subpixel motion was stable, pixel motion visible, runtime filter changes and Texture.Apply were observed.
- Pixel screenshots: `Temp/Engine/review/PixelClarity/sharp-*.png` and `bilinear-*.png`.
- PackageRuntimeRegression: animation crossfade/root motion/forward and reverse loop events, navigation area masks/partial paths/braking, simulated bodies, audio mute/one-shot pause, and camera activation/blends passed.
- PropertyAttributes: runtime/editor attribute tests passed, including decorator height and prefix/suffix rendering.
- VulkanRendering: complete existing GPU regression passed on Intel UHD Vulkan. Real post-processing readback now verifies desaturation and disabling effects.
- VulkanRendering --package-showcase: all eight packages rendered at 1280x720, 640x360 and 360x640. Forced GC preserved in-memory sprites; source colors, tile output, movement and grading passed.
- PackageExamples: all 21 archives (3 Core plus 18 package examples) imported/reimported and compiled. PackagedExampleLayout confirmed archive hashes match the exported copies and Package Manager discovers them.
- PackageDocumentation: Core plus eight packages passed; all eight packages have expanded runtime guides and at least three Skills (30 total). Structured YAML metadata, skill names/descriptions and workflow content passed the repository validator.
- SourceLayout: source/platform boundary and runtime threading audit passed (25 non-test projects).
- Release solution build: zero warnings and errors. Engine export returned BENGINE_EXPORT_OK at Engine/BEgine.
- Windows HFS integration: first install v1, decline v2 and retain v1, install v2, decline rollback and retain v2, then activate v1 from the remote latest pointer all passed. Tests also checked an empty sandbox, content hashes and runtime code/resource proof files. HFS was stopped after validation.
- PackageShowcase Windows build and content build passed. The packaged player downloaded all eight package assemblies/resources, entered the game and ran for five seconds without error logs. Evidence: Tools/Temp/PackageShowcaseValidation/811bb30f1e89440ba64c9fce4a14aa80.

## Implementation notes

- AssetImporter.assetBundleName/assetBundleVariant and BuildPipeline.BuildAssetBundles support importer assignments and explicit build maps. Player builds use importer groups; unassigned content uses hotupdate-content. Serialized cross-bundle references add dependencies.
- AssetBundleRuntimeOptions.RequiredBundleNames defaults to null (all bundles). Explicit roots include dependencies and mandatory code/package resources. Previously installed optional bundles remain included in later updates. IAssetBundleDownloadManager exposes size estimation and dependency download against the active catalog.
- PixelPerfectCamera uses assetsPPU, reference resolution and integer zoom. It expands the visible world to match the actual viewport; it does not add letterboxing. Use Point sampling for pixel artwork; rotated or nonuniformly scaled sprites cannot preserve an integer pixel grid.
- Vulkan now implements offscreen targets and includes framebuffer output formats in pipeline cache keys. Texture resources and bindings are retired after the submitted frame has completed.
- TCP/UDP facades use replaceable transport interfaces. Socket calls are isolated under Networking/Adapters. HTTPS/WebSocket OS/browser async I/O is isolated in BEngine.Platform.DotNet; download callbacks and operation completion enter runtime only from Poll.

## Corrected behavior

The original review is retained under Temp/Engine/review/SourceReview20260907.md as a historical pre-fix report.

| Review findings | Correction |
| --- | --- |
| 1, 10 | Cancellation and Subject callbacks tolerate reentrant unsubscription without skipping later subscribers. |
| 2, 3, 7, 15 | One host frame advances Time once across scenes; Start precedes the first update; lifecycle mutation and callback state changes are rechecked. |
| 4 | Structural Undo marks the affected scene dirty even when another scene is active. |
| 5, 6 | Unchanged runtime textures avoid RGBA conversion; animation assets cache parsed data and invalidate on asset-generation changes. |
| 8, 9 | SHA verification has a configurable frame byte budget; bundle opening avoids redundant verification; caller cancellation cannot evict a healthy shared load. |
| 11, 12 | UI pointer events dispatch once through the propagation path; text cache keys distinguish case. |
| 13, 14 | Partial navigation paths retain reachable endpoints; animation events repeat across forward and reverse loop boundaries. |
| 16, 17 | Stopped coroutines dispose their enumerators; HTTP Abort/Dispose resolve as cancellation. |
| 18 | Sprite and particle visibility checks precede lighting and shadow evaluation. |

Integration testing additionally found and corrected lost Texture ownership after GC, Vulkan post-process backface culling, zero-intensity vignette darkening, cold atlas import source resolution, and required-component duplication when scene records are reordered. The scene loader now reuses unbound components created by RequireComponent and restores their saved IDs and fields.

## Delivery and limits

- Engine: Engine/BEgine/BEngine.Editor.exe. Eight exported packages: Engine/Packages.
- Integrated source project: Example/PackageShowcase. Player: Example/ShowcaseBuild/package showcase/package showcase.exe. Content: Example/ShowcaseBuild/hotres/package-showcase.
- Split update source project: Example/Project. Player: Example/Build/bengine 2d showcase/bengine 2d showcase.exe. Content: Example/Build/hotres/bengine-2d-showcase.
- Reproduce delivery with Tools/Validate-PackageShowcase.ps1 and Tools/Run-WindowsHotUpdateValidation.ps1. Read Example/PackageShowcase/Readme.md for manual launch and HFS commands.
- Platform and AB API guide: Core/Editor/Doc/PlatformAndBundles.md. Per-package contracts and limitations: Packages/<package>/Editor/Doc/RuntimeGuide.md.
- The implementation follows Unity-style APIs within this fixed-point 2D engine; it is not binary-compatible with Unity or a complete Unity feature set. Animation remains single-layer 2D, navigation uses a grid and lightweight avoidance, and pixel snapping cannot remove aliasing from arbitrary rotation or nonuniform scaling.
- Windows/Vulkan is verified on hardware. Portrait viewport checks are not Android/iOS device tests. TCP's built-in fallback accepts IP addresses/localhost; other DNS policies belong in a platform adapter. HTTPS and WebSocket adapters are implemented, but public TLS endpoints and a real WebSocket server were not exercised in this validation.
- Runtime simulation, BTask continuations, network completion and callbacks stay on the main thread. OS async I/O is isolated in the non-runtime BEngine.Platform.DotNet assembly.

## References

- [Unity Start lifecycle](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.Start.html)
- [Unity AssetBundleBuild](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetBundleBuild.html)
