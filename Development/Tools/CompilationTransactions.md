# Compilation transaction ownership

Core Editor owns the process's active compilation state and paired compilation
notifications. `CompilationPipeline.BeginCompilation(context)` returns an exclusive
`CompilationScope`. Begin and dispose on the host's editor thread. The context
passed to the start and finish events is the same object. `isCompiling` remains
true throughout both notifications and becomes false after finish dispatch.

```csharp
using var transaction = CompilationPipeline.BeginCompilation(workspace);
var request = scriptHost.PrepareCompilation();
// The host may run request.Build(token) on a worker, then marshal back to its
// editor thread before calling scriptHost.Apply(result) and disposing transaction.
```

The transaction coordinates state and notifications, not compiler work or a UI.
`ProjectScriptHostSession` owns compilation, cancellation/draining and loaded
assemblies. The host owns its transaction lifetime across that work, including
failure and cancellation. A retained disposed scope does not retain its context.
It cannot end a later transaction; duplicate or reentrant finish is harmless.
Concurrent/nested begins and disposal from a different thread are rejected.

Subscriber exceptions use the existing Core callback isolation. Infrastructure
failures remain observable, but ownership is released even if finish dispatch
fails. An infrastructure failure during begin attempts matching finish cleanup;
if both fail, the caller receives both exceptions. Never abandon an active scope.

EditorServices' existing `EditorApplication.isCompiling` getter reads Core state;
it has no setter or independent field. EditorWorkbench uses the public scope for
initial compilation and a continuous background transaction across superseded
requests. Scheduling failure, cancellation, shutdown and completed apply release
that scope. Console clearing, progress UI, menu state, selection and reload policy
remain with EditorWorkbench. Core does not name or reference these packages.
Cancel callbacks may fail: the workbench still disposes the source and finishes
the transaction in `finally`, while preserving the error for its caller.

The old internal start/finish raisers and EditorApplication's internal setter are
removed. Recompile tooling built against those internals and use the public scope.
Existing public compilation events and the read-only EditorApplication property
remain available. Scene/Prefab formats, GUIDs, manifests and Player data do not
change. Assembly-level notifications remain owned by the actual compiler/load
operations, and a transaction does not synthesize per-assembly success messages.

The local Unity 2022.3.62f2 Editor XML documents `isCompiling` as read-only and
compilation start/finish events as main-thread notifications with a shared context.
This is a boundary reference; the disposable transaction is BEngine's host API.

Verification: `Test-CompilationTransactions.ps1` builds Core-only and facade
consumers without friend access, including failure isolation, thread ownership,
reentrancy and context collection. A separate workbench fixture injects cancellation
state into an uninitialized host (without a window) to verify faulting callbacks,
superseded work and duplicate cancellation against the actual cleanup method.
`Test-ProjectScriptHostSession.ps1` also covers
transactions around real initial/incremental/background builds, failed source,
canceled process work and recovery. No GUI or game framework is needed for the
Core mechanism. The overall internal API audit remains incomplete.
