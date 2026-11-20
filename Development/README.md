# Engine development

`src` contains the Core, Hub, Packages and Platform source trees and `BEngine.sln`.
Supporting files live together here:

| Directory | Contents |
| --- | --- |
| `Build` | MSBuild settings, dependency versions, generated assembly metadata and IDE workload configuration. |
| `Scripts` | Engine export, version queries, source solution synchronization and package layout migration. |
| `Shared` | Source and branding assets used by several engine and host projects. |
| `Tools` | Build tools, contract probes, verification scripts and their maintenance notes. |
| `Documentation` | Source layout and Visual Studio instructions. |

The three `Directory.*` files at the repository root are automatic MSBuild entry
points. They import the configuration in `Build`. Plugin projects retain their
own build settings. Build and verification output stays in the workspace `Temp`.

Run from the Engine repository directory:

```powershell
dotnet build src/BEngine.sln -c Release
./Development/Scripts/SyncSolutionLayout.ps1 -Check
./Development/Tools/Test-CoreKernel.ps1
./Development/Tools/Test-PackageBoundaries.ps1
./Development/Scripts/ExportEngine.ps1 -Configuration Release
```

`Tools/BEngine.VeldridSafety` is a build dependency of the Veldrid backend. It
checks Vulkan return values in output assemblies before distribution. Package
layout migration remains part of export to support existing installations.

See [source ownership](Documentation/SourceLayout.md) and
[Visual Studio](Documentation/VisualStudio.md) for navigation and build details.
