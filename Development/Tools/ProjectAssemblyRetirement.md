# Project and package editor registration lifetime

Core Editor registers its own assembly-retirement cleanup with Core's existing
`RuntimeAssemblyCleanup` mechanism. It removes editor initialization history,
template providers, source processors, build-resource contributions, settings
providers and reflection warmups owned by the retiring assembly. Neither Core
Runtime nor Platform names or references a package to accomplish this cleanup.

Settings providers carry an internal weak-key association with their exact
assembly owner. Two independently loaded assemblies with the same name are
different owners; retiring one must preserve the other. Retired cached provider
delegates are released before registry change callbacks run. Reflection queries
exclude retired owners while runtime retirement is still in progress, and cached
type/method arrays are invalidated without invoking extension warmups during unload.

Package unloading reuses this editor cleanup through its existing runtime cleanup
call. It retains package-specific resource roots, service scopes, shadow-directory
cleanup and host-event handling. Project compilation sessions still own only their
own loading scopes; this change does not activate a project or turn an isolated
`ProjectScriptSession` into an exclusive command-line project session.

`Test-ProjectAssemblyRetirement.ps1` exercises real compiled project assemblies,
independent same-name owners, reload with the same MVID, module-initialization and
cleanup failures, an existing surviving session, and garbage collection without a
later registry query. The initialization assertion invokes the existing internal
Core initialization mechanism by reflection from the test harness; production
callers receive no private-access grant or new initialization API.

Baseline reproduction and verification records are in
`Temp/Engine/Verification/ProjectToolLifetime_20261113`. The complete Core/package
semantic review remains open, including the generic command-line project scope.
