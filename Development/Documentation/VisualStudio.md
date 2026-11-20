# Visual Studio source solution

Open `src/BEngine.sln` with Visual Studio 2026 and the .NET desktop development workload.
The workload configuration can be imported from `Development/Build/.vsconfig`.
The solution follows the source directory ownership:

```text
BEngine
├─ Core               BEngine.Core, BEngine.Core.Editor and BEngine.Player
├─ Hub                Launcher
├─ Packages
│  └─ <package>       Runtime, Editor, resources and development tools
└─ Platform           Neutral host and native/browser bridges
```

Project nodes represent their own directories. Code already displayed by a loaded
project is not repeated as solution items. Resources, documentation, package
descriptors and platform templates remain accessible under their physical owner.
Local assembly definitions and other files are visible inside their project.
Platform sources outside a project are displayed at their real solution path.
Shared sources, branding and developer tools live in `Development` outside the
source solution; their build references remain active. Generated files are hidden.
Source excluded for a selected graphics backend remains visible in its physical
folder without being compiled for that backend. These project display rules live
in `Development/Build/BEngine.SourceLayout.targets`.
Generated directories such as `bin`, `obj`, `.vs`, `Temp` and `BuildRuntime` are
excluded. Contract-probe source lives in `Development/Tools/Tests` and is excluded from the
engine solution; generated validation artifacts belong in the repository `Temp`.

## Synchronizing the solution

After adding, removing or relocating a project or a resource, run from `Engine`:

```powershell
./Development/Scripts/SyncSolutionLayout.ps1
./Development/Scripts/SyncSolutionLayout.ps1 -Check
```

The script discovers projects from disk and updates nesting and all solution
configuration mappings. Existing project
GUIDs and configuration selections are preserved. New GUIDs are deterministic.
The `-Check` command reports drift without writing files; repeated synchronization
does not change the solution.

## Special projects

An optional, unconditional property in a project's own `PropertyGroup` controls its
solution role; no concrete package names are embedded in the synchronizer:

```xml
<BEngineSolutionRole>Template</BEngineSolutionRole>
```

- Omitted or `Default`: load and build normally.
- `Template`: expose the project file as a solution item, without loading or
  restoring it. Used for platform-specific Player hosts, native mobile adapters
  and alternate Runtime builds. The corresponding publishing workflow still
  builds the project with its required workload and settings.
- `Publish`: load for navigation and explicit builds, but omit from the solution's
  default build. Used for publishing composition projects, whose platform and
  output settings are selected by the publishing workflow.
- `Tool`: load and build as an independent development tool. It is not an engine
  or package Runtime/Editor compilation phase.

The ordinary solution build requires no Android, iOS or WebAssembly workload.
These workloads are needed only for the corresponding platform publishing jobs.

## Build order

MSBuild determines build order from each project's `ProjectReference` dependencies.
Project Runtime/Editor compilation is managed by the editor against these engine
and package outputs. Normal incremental MSBuild checks apply.

To compile the source solution without exporting installation files:

```powershell
dotnet build ./src/BEngine.sln -c Release -m:1 /nodeReuse:false `
  -p:NuGetAudit=false -p:BEngineEngineExportOwner=false -p:BEnginePackageExportOwner=false
```
