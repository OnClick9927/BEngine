# Editor callback contracts

Core owns callback execution, cooperative loop protection, failure throttling and the optional presentation-state isolation contract. It does not decide whether the default editor pauses Play Mode or opens a dialog. EditorWorkbench owns that policy and unsubscribes when its assembly is unregistered. The protection remains usable without any graphics or editor UI package.

Packages and platform adapters use `BEngine.Editor.EditorCallbacks`. The existing internal guard and dispatcher remain implementation details; this facade shares their fault state and loop scopes rather than creating a second implementation.

- `Invoke` / `TryInvoke` execute one extension callback. Ordinary failures are logged and return false (or the supplied fallback). `EditorCallbackExitException` propagates so the presentation boundary can end its current GUI pass.
- `Dispatch` isolates notification subscribers individually. An ordinary failure or callback exit ends that subscriber; later subscribers still execute. Callback identity continues to include the originating method's module ID and metadata token.
- Stateful overloads let callers pass values without a capturing closure. Generic notification dispatch uses those overloads internally. Feature strings, loop scopes and UI-backend snapshots may still allocate; the API does not promise allocation-free execution.
- `Report` records a failure at an existing boundary with the same five-second repeat throttle. It requires a nonempty feature identity and a nonnull exception.
- `BeginLoopScope` protects work whose presentation state is managed elsewhere. `LoopExecutionStopped` reports the stopped source and limit to presentation providers; it is not a progress event.

The backend captured on entry restores its own state in `finally`, including when a callback replaces the global backend. `IEditorFeatureIsolation` remains optional; no GUI package is initialized by Core callback execution. Consumers retain their existing main-thread requirements; this contract does not make GUI work thread-safe.

Local Unity 2022.3.62f2c1 API metadata documents `GUIUtility.ExitGUI` as aborting the current immediate-mode GUI evaluation by throwing an exit exception. BEngine likewise preserves the distinction between exiting one GUI pass and containing notification-handler failures. This comparison does not claim Unity has BEngine's DI, loop guard or implementation structure.

`Test-EditorCallbackContracts.ps1` runs a package-free public Core consumer and a separate default-workbench policy consumer. Only the latter injects a test host through fixture reflection; callback execution, service attachment and cleanup are tested through public contracts. Actual Editor smoke and the wider authoring regressions exercise the installed presentation implementation.
