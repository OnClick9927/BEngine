# Authoring tool contracts

Core Editor owns package source compilation and editor-template provider selection.
`PackageSourceValidation.Compile` compiles the supplied snapshot and dependency
closure in an isolated working directory without loading assemblies or opening a
project. It uses the existing Runtime / Editor dependency compiler, generates its
Library / Temp / Logs below that directory, and reports failures to the caller.
The authoring tool owns that directory and its cleanup. Cancellation is checked
before compilation and between compiler work items; it does not forcibly terminate
an already accepted external compiler process.

`PackageEditorTemplates.Create` selects the registered provider or the existing
headless menu-command fallback. Package naming, source snapshot construction,
repository export location, prompts and publication transactions remain in the
workbench.

`PlayerBuildLayout` exposes its existing default addresses and read-only output
preflight. The build pipeline repeats the same guards at execution time. No UI
policy or publication transaction is delegated to callers by this preflight.
Command-line version parsing uses the existing `ContentVersionLabel` contract.

EditorServices owns `DefaultAsset.CreateReadOnlyReference`: it creates a fresh
unsaved browser reference and binds paths through Core's existing resource-reference
API. Metadata setters stay nonpublic. Workbench chooses the displayed label and
does not write Core or EditorServices private metadata.

Prefab tools query `RuntimeAssetReferences.IsTransient` so objects in running or
transient scenes are protected as well as explicitly transient roots. They no
longer inspect the runtime-only backing flag.

`Test-AuthoringToolContracts.ps1` builds a consumer without private-access grants.
It checks previous-binary output guards, a real junction, template providers,
resource references, transient hierarchy protection, and real dependency-ordered
Runtime/Editor compilation including missing dependencies and compiler failures.

This is not a claim that the complete Core/package responsibility audit is finished.
Command-line project activation, compilation lifetime and initialization still need
a generic owner-scoped contract; they must not be solved by exposing all compiler
internals or moving workbench policy into Core.
