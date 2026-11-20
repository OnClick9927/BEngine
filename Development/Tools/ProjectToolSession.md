# Exclusive project tooling

`BEngine.ProjectSystem.Editor.ProjectToolSession` activates one project on the
engine owner thread without loading the default workbench or game framework.
The contract is implemented in Core Editor; platform presentation backends are
optional and resolved only when a tool actually requests presentation.

```csharp
using var session = ProjectToolSession.Open(workspace, cancellationToken);
// Enabled packages have registered their extensions. Apply caller-owned policy here.
session.InitializeScripts(cancellationToken);
// Import, build or inspect the activated project using its registered extensions.
```

Open acquires the existing exclusive editor project lock before changing global
project state. Project Runtime is compiled and activated before project Editor.
Compilation notifications and output publication use the existing compiler
mechanisms. Only assemblies owned by this activation are passed to editor
initialization; unrelated isolated script sessions remain untouched.

Dispose retires owned code and restores the resource path, package state and
temporary environment. Cleanup continues after individual extension failures,
then reports collected errors. Compiled output and diagnostics follow the
existing persistence policy; mapped assembly files can delay disposable cache
deletion until their load contexts collect. The caller must release any retained
types, instances and delegates to allow collection.

The session is exclusive and synchronous. Open, InitializeScripts and Dispose
must run on the same owning engine thread. Initialization can run once; cancellation
or failed initialization retires the session automatically. Open rejects an
existing editor or exclusive tool session before changing their state.

`ProjectScriptSession` remains an isolated compiler/load scope, without project
activation or editor initialization. `BPackageManager.OpenManifest` resolves and
edits package declarations without changing active runtime package state.

The command-line player workflow in EditorWorkbench composes this contract with
`PlayerProjectPolicies`; Core does not select game policies or reference packages.
No game framework, IMGUI or workbench dependency is added to Core or Platform.

Regression entry: `Development/Tools/Test-ProjectToolSession.ps1`. It exercises real source
compilation, package activation, generated runtime policy, importer execution,
independent sessions, failure rollback and collectible load-context retirement.

Package and project compiler identities map generated SDK sources, temporary
reference copies and output paths as well as source snapshots. Rebuilding the
same release in different exclusive instance directories must produce identical
assembly bytes; original project source locations remain available in debug
symbols. Compiler cache revisions invalidate artifacts created before this
mapping, without relaxing Web precompiled-code hash validation.
