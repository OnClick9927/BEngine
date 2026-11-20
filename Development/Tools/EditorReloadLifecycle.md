# Editor assembly lifecycle

Core Editor owns assembly replacement notification pairing through
`AssemblyReloadEvents.BeginReload`. Its returned scope publishes the before event
immediately and the paired after event when completed or disposed, even when
replacement or cleanup fails. Transactions are exclusive through both callbacks,
reject reentrancy and require completion on the creating editor thread. Cleanup
releases ownership even when the presentation isolation backend throws.

`BeginInitialization` uses the same ownership protocol for first load. It emits no
before event and publishes readiness only on explicit `Complete`; disposal before
completion aborts without announcing a ready editor. Scopes hold no project or
assembly. The existing public events and private notification implementation stay
in Core Editor; no free-standing notification raiser is exposed.

`ProjectScriptHostSession.PrepareEditorExtensions` prepares reflection and warmups
before presentation. `InitializeEditorExtensions` synchronizes editor reflection
and initializes default-context engine extensions plus the session's active
package/project graphs. Other collectible tool graphs are excluded. Active owned
assemblies take precedence over default-context copies with the same simple name.
The host can skip a second synchronization immediately after preparation. These
operations preserve class-constructor, once-per-module initialization method and
ordered scripts-reloaded callback behavior. They do not publish readiness or
create windows, scenes, project policy, tasks or a game framework.

EditorWorkbench retains source-change scheduling, progress, reload locks, undo and
clipboard policy, menu/selection restoration, importer reconciliation and Play Mode
policy. First-load readiness follows importer reconciliation. Synchronous reload
scope ownership encloses early cleanup and compilation setup; asynchronous apply
releases the reload scope even if compilation transaction completion throws.

`Test-EditorReloadLifecycle.ps1` uses non-friend consumers for notification ordering,
abort and failure cleanup, concurrency/reentrancy rejection, owner-thread checks,
actual project compilation, isolated graph exclusion, repeat initialization,
retirement and collection. The real Editor fixture exercises initial startup,
Runtime plus Editor replacement, Editor-only replacement and scene/selection
preservation. All verification products belong under the workspace Temp directory.
