# Code authoring and script symbols

`ProjectCodeModel.Capture(ProjectWorkspace)` exposes a read-only authoring snapshot using the same assembly-definition graph, source ownership, Runtime/Editor split, selected package references and compilation symbols as the project compiler. Capturing does not compile, load additional assemblies, modify package state or substitute another build order.

`CodeEditorIntegration.Register(ICodeEditorIntegration)` lets an editor extension handle a `CodeEditorOpenRequest` containing the project, source path, line and column. Registrations are disposable. Returning false preserves the existing configured-editor and shell-opening fallback. The Core extension point contains no IDE or package identity.

Project scripts compiled for editor execution use portable PDBs and the existing Debug optimization policy. Source transformations retain the original document via `#line` and a SHA-256 `#pragma checksum`; the physical compiler snapshot maps to a distinct stable virtual directory so its transformed checksum cannot replace the original document checksum. This supports exact-source breakpoint binding without disabling loop guards. A debug-source revision participates in content-cache identity to invalidate older symbols.

These mechanisms do not disable Release optimization, configure a concrete debugger, register a network endpoint or add IDE tooling to runtime applications.
