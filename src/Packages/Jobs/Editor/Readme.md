# Jobs

`BEngine.Jobs` provides dependency-aware, single-thread asynchronous scheduling, batched `IJobParallelFor` execution and unmanaged containers with schedule-time safety checks. The compatibility `IJob` paths never create worker threads. For explicitly pure, copied unmanaged values, `BackgroundJobs.ScheduleAsync<TJob,TResult>` is the separate bounded worker API; it never accepts scene objects, native containers or engine services and always completes on the owner frame thread.

- Use `job.Schedule()` for one job and `job.Schedule(length, batchSize)` for work divided into batches across frames.
- Pass a `JobHandle` when a later job reads or writes data owned by an earlier job. Combine independent handles with `JobHandle.CombineDependencies`.
- Store shared unmanaged data in `NativeArray<T>` or `NativeReference<T>`. Mark input-only fields `[ReadOnly]` and output-only fields `[WriteOnly]`.
- Prefer `await handle` or `await handle.CompleteAsync()` before accessing its containers. `Complete()` synchronously helps execute queued jobs on the engine thread.
- The Editor/Player drives `BTaskScheduler.Main`; custom hosts must tick that scheduler. `JobSystem.Shutdown()` cancels pending jobs and releases reservations.
- `[BurstCompile]` records optimization intent. BEngine prepares the closed generic `Execute` method and uses non-boxing, aggressively optimized dispatch. It does not invoke Unity Burst or an LLVM native compiler.

See `Doc/Jobs.md` for failure, lifetime and platform behavior.
