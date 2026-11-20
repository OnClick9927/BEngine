# BEngine Tools for Visual Studio

Visual Studio 2022 / 2026, Windows x64. **Use Visual Studio 2026 for current BEngine .NET 10 projects.** Extension API compatibility with Visual Studio 2022 does not add .NET 10 project-system support to that IDE. Install .NET desktop development tools and the .NET SDK required by your BEngine version. Double-click `BEngine.VisualStudio.vsix`, select Visual Studio, and restart it when the installer requests it. The plugin does not install .NET again.

In BEngine Package Manager, install **IDE Integration** (`com.bengine.ide-integration`). Open your project in BEngine Editor; the package creates `BEngine.Project.sln` and the IntelliSense projects automatically. Open that solution in Visual Studio. Engine APIs, enabled package APIs, project Runtime and Editor code use Visual Studio's built-in Roslyn IntelliSense, navigation and diagnostics.

Use **Tools > BEngine > Attach to BEngine**. A single running Editor attaches immediately; multiple registered Editors show a selection dialog with project name and PID. You can also enable the **BEngine** toolbar from Visual Studio's toolbar menu. Set a C# breakpoint, attach, then enter Play mode in BEngine. Detaching does not terminate the Editor. Synchronize C# Projects refreshes references and source lists through the running Editor; its progress window supports cancellation. Open BEngine Solution opens the generated solution through Visual Studio's normal save/close prompts.

Discovery is scoped to the current solution/document's project. If no project is open, select a folder containing `Project.yaml`. The plugin checks the target PID, process start time and executable path before attaching with Visual Studio's **CoreCLR** debug engine. It never opens a debug server, scans unrelated folders or selects a process by executable name alone. Registrations are metadata under `<project>/Library/IDE`; there is no shared user-profile cache. Dead registrations are ignored. An unresponsive Editor causes synchronization to time out after 30 seconds.

This integration targets local managed Editor / Play mode debugging. Native code, browser WebAssembly, iOS/Android device debugging and application releases without symbols are separate debugging targets and are not advertised by this version. Code changes still follow BEngine's compilation/reload rules; this plugin does not claim Visual Studio Hot Reload support.

## Build

```powershell
./Build.ps1 -OutputDirectory <Temp-output> -ArtifactsDirectory <Temp-build>
```

NuGet uses the normal system cache. All build output and intermediate files use the explicit artifacts directory. No Visual Studio SDK assemblies are bundled. Source files stay in `Plugin/VisualStudio`; engine code stays under `src`.

## Official API references

- [Process2.Attach2](https://learn.microsoft.com/en-us/dotnet/api/envdte80.process2.attach2?view=visualstudiosdk-2022)
- [AsyncPackage initialization](https://learn.microsoft.com/en-us/visualstudio/extensibility/how-to-use-asyncpackage-to-load-vspackages-in-the-background?view=visualstudio)
- [Visual Studio 2022/2026 extension compatibility](https://learn.microsoft.com/en-us/visualstudio/extensibility/migration/extension-compatibility?view=visualstudio)
- [.NET SDK and Visual Studio targeting support](https://learn.microsoft.com/en-us/dotnet/core/porting/versioning-sdk-msbuild-vs)
