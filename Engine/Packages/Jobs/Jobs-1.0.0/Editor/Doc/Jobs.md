# Jobs runtime guide

`BackgroundJobs.ScheduleAsync` is the opt-in value-only worker path. Implement `IBackgroundJob<TResult>` on an unmanaged struct and return an unmanaged result. The struct is copied before admission; its `Execute` method receives only a system cancellation token. Use it for decompression, hashing or other pure data transforms. Do not put `BObject`, `Scene`, `NativeArray`, graphics handles or service locators in the job. Cancellation is cooperative and the returned `BTask` completes on the owning frame thread. The API is bounded and falls back to one cooperative step on WebAssembly.

## Scheduling and dependencies

The scheduler runs on the owning engine thread through `BTaskScheduler.Main.FrameAdvanced`. It does not create background threads or queue work to the .NET thread pool. A `JobHandle` represents completion and the full dependency closure. A dependent job starts only after every upstream job succeeds. Upstream exceptions fault the dependent handle without executing its body. The same implementation runs on desktop and single-thread WebAssembly.

`IJobParallelFor` keeps its compatibility name and divides `[0, length)` into fixed batches. Batches execute sequentially and yield across frames; the frame scheduler processes at most 64 queued batches per frame. Choose a batch size that keeps each callback short. Prefer `await job.Schedule()` or `await handle.CompleteAsync()`. `Complete()` helps execute pending batches immediately on the current engine thread; a dependency on the currently executing job is rejected instead of hanging.

## Container safety

`NativeArray<T>` and `NativeReference<T>` allocate unmanaged memory and keep a shared lifetime record across struct copies. Scheduling inspects container fields and reserves their declared access:

- `[ReadOnly]` allows reads and rejects writes from that job.
- `[WriteOnly]` allows writes and rejects reads from that job.
- An unmarked field reserves reads and writes.

Concurrent readers are accepted. Any overlap containing a writer requires the existing handle in the new job's dependency closure. While reservations exist, main-thread reads, writes and disposal throw. Disposing one struct copy invalidates every copy. Bounds and use-after-dispose checks are always enabled.

The safety system controls ownership and declared access even though execution is single-threaded, preventing access to partially computed results across frames.

## Compilation behavior

Closed job types are cached, their concrete `Execute` method is prepared through the active .NET runtime when supported, and execution uses constrained generic calls with aggressive optimization flags. `[BurstCompile]` is exposed as an intent marker in `JobCompilationInfo`; it is not Unity Burst and does not promise LLVM vectorization. Player AOT compilation still needs the concrete job types referenced by game code.

## Lifecycle

`workerCount` and worker metrics are always zero. The optional count argument to `Initialize` remains accepted for source compatibility and does not create workers. Pending jobs subscribe to the frame loop only while work exists. Shutdown, application quit and collectible assembly unload release pending reservations and cancel handles. Initialize can start a fresh scheduler afterward. Custom hosts must advance `BTaskScheduler.Main` on their engine thread. Dispose native containers during component or application teardown after completing or canceling their jobs.
