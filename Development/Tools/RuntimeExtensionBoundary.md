# Runtime extension contracts

Core owns scene execution, dependency injection, frame budgets and callback dispatch. Feature packages consume their public contracts; they do not acquire ownership of the engine's service scopes or frame pump.

## Borrowed services

`BEngine.RuntimeServices.GetService<T>()` and `GetRequiredService<T>()` resolve in this order: a live current scene, the latest attached runtime provider, then the platform/default provider. A missing service in the latest attachment falls back to the platform, rather than searching older attachments. These queries retain the previous private locator's semantics and introduce no per-frame cache or scan.

Use them on the engine thread. Services and providers are borrowed. `AttachBEngineRuntimeServices()` returns only an attachment lease; disposing that lease never disposes the provider. Destroyed scenes are skipped while their former callbacks unwind. Packages obtain file, path and platform facades through the generic queries, rather than internal convenience properties.

## Frame participation

`Time.isFrameActive` and `FrameWorkBudget.isActive` are read-only observations. Frame creation and budget ownership remain engine-controlled. `FrameWorkBudget.GetCooperativeMaximumStep(normalLimit)` exposes the shared loading step bound and validates the caller's positive finite normal limit. It preserves the loading scope's limit selection, including strict nested scopes.

## Callback and component access

Scheduled zero-argument messages use `RuntimeTypeCache.CanInvokeMessage(type, name)` and the existing cached invocation API. The redundant internal lookup has been removed.

The delayed-invocation adapter preserves its existing `MissingMethodException` for null, empty, whitespace and absent method names; it does not leak the lower-level lookup's argument validation exception. `Development/Tools/Test-CoroutineBoundary.ps1` compiles the entire exported coroutine implementation under an unrelated assembly name to verify that it no longer needs Core friend access, and checks scheduling/cancellation and invalid inputs through the public API.

`GameObject.GetComponentsEnumerator()` returns a value enumerator in attachment order. It avoids snapshots and boxing when used directly. It must not survive component topology changes: adding/removing a component invalidates the enumerator, retaining the live List mutation checks required by collision callbacks. Dispose the enumerator normally; do not store it across frames.

## Pure-data work ownership

Adapters use `BackgroundWorkScheduler.RunForAssembly(owner, work, token, discard)` when their delegate lives in the adapter assembly but the job belongs to a different assembly. Collectible owner unload cancels the job. Completion and discard still wait for the worker's acknowledgement and run on the owning engine frame. The existing inferred-owner `Run` method remains available. Neither method permits workers to access scenes, engine service scopes or graphics.

This migration removes selected historical internal API dependencies. It does not claim that every package friend grant has already been removed: asset authoring and diagnostics still require a separate minimal-contract audit.

Validation: `Development/Tools/Test-RuntimeServiceBoundary.ps1` builds a consumer without Core friend access and exercises scope lifetime, zero-allocation lookup/enumeration, loading limits, cached callbacks and collectible worker ownership.
