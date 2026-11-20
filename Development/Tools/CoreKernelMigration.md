# Minimal Core migration

## Responsibility boundaries

```text
Core
├─ BEngine.Core                 DI contracts, explicit module registration and retirement
└─ BEngine.Core.Editor          Package descriptions, normalization, versions and dependency plans

Packages
├─ Foundation                   Objects, scenes, fixed mathematics, BTask, resources and scheduling
├─ Compilation                  Projects, compilation, package activation and code reload
├─ Serialization                YAML encoding and decoding
├─ Player                       Default runtime host, startup and content sessions
├─ Platform…                    Managed platform contracts, adapters and build providers
└─ Existing feature packages    Asset pipeline, graphics, rendering, UI, workbench and game framework

Platform
├─ BEngine.Editor.Host          Descriptor-driven entry point with no concrete package references
├─ BEngine.Editor.Host.Contracts
├─ Native / Resources           Native and browser bridges
└─ ExportPlatformSources.ps1    Metadata-driven native template export
```

Runtime Core references `Microsoft.Extensions.DependencyInjection.Abstractions`; the host supplies its container. `AddBEngineCore` accepts explicit `IEngineServiceModule` instances. It does not discover packages, open files, initialize scenes or create windows/devices. Editor Core accepts in-memory package data and returns validation/version/order results. Filesystem access, YAML, Hub communication, assembly loading, Roslyn and package UI are implemented outside Core.

## Compatibility and integration

- Moved implementations keep their assembly and type names. No scene, Prefab, asset GUID or project setting format migration is required.
- Legacy DI identities in `BEngine` and package-data identities in `BEngine.Editor` forward into the new kernels. Core has no forwarding or reference to feature packages.
- Manually maintained compiler references must include `BEngine.Core.dll`; editor consumers also include `BEngine.Core.Editor.dll`. Repository project/package compilers and standalone test consumers have been updated.
- The neutral editor entry contract is implemented by the workbench host; the former identity in `BEngine.Editor` is forwarded for existing consumers.
- A package assembly with `bootstrap: true` uses its verified, precompiled installation image. Source/asmdef dependency validation still runs. Editor compilation and Player release input collection use this metadata rather than requiring a generated project cache pointer.
- Native mobile templates belong to their adapter packages. Export discovers their locations from tooling metadata; source and installed template references are checked separately.
- Source navigation searches the new Core and package locations. Player host discovery follows the Player distribution template location.
- The solution retains the order Core Runtime → Core Editor → package Runtime → package Editor → project Runtime → project Editor, with dependency ordering within stages. Bootstrap packages remain package-stage projects.
- Application remains the game framework and workflow composition package. Default editor composition and platform/build implementations do not move back into Application.

## Verification and measurements

The verification directory is `Temp/Engine/Verification/CoreKernel_20261115`; isolated Snake fixtures and logs use `Temp/Snake` according to its local instructions. `VerificationSummary.json` and `VerificationReport.md` in the verification directory record the final results and limitations.

Release kernel DLL sizes are 15,872 bytes for Runtime and 34,816 bytes for Editor, excluding framework dependencies, symbols and host executables. The independent kernel suite checks the real catalog of 66 packages and 97 assembly steps without loading the engine object model, renderer, workbench or Player. Source and PE boundary checks cover both kernels and both neutral editor host assemblies.

Existing integration suites cover scene/resource identity, 500-sprite rendering, zero-allocation warm paths, compilation ownership, public build mechanisms, Player content sessions and native template references. The live Editor fixture checks real shader compilation, source monitoring, project code replacement, scene/selection retention, asset import and GPU rendering. Actual Windows/Web builds exercise release collection beyond the editor startup path.

This migration changes ownership and bootstrap composition. It does not by itself establish a reduction in complete game startup time, frame CPU time or total package size; those depend on the enabled packages and publishing configuration. iOS runtime execution still requires the corresponding Apple toolchain.
