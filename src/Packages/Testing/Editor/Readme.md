# Test Framework

Open **Window > General > Test Runner** to discover and run tests from the current project.

- `[Test(TestMode.Edit)]` runs without a scene.
- `[Test(TestMode.Play)]` creates a real `Scene` and `SceneRuntime`; use `TestContext.Current.Scene` and `Tick()` for lifecycle checks.
- `[Test(TestMode.Player)]` is built into a Windows Player and executed after its startup scene loads. Results return to the editor as JSON.
- Double-click a result, or use **Open**, to jump to the first portable-PDB sequence point.
- Add `[assembly: BEngine.Testing.Coverage]`, or the same fully qualified attribute to a type or method, to enable statement and branch probes. Export produces LCOV (`DA` and `BRDA`) and JSON in `Temp/TestRunner/Coverage`.

The package has its own assertions and does not require NUnit or xUnit in game assemblies.

Web Player tests create an isolated browser profile in the owning project's
`Temp/TestRunner/BrowserProfiles` and remove that profile after the run. Direct launcher callers can
set `PlayerTestLaunchRequest.ProjectPath`; `RunBuiltPlayerAsync` accepts an optional `projectPath`.
When no project is open, provide an existing absolute project path explicitly.
