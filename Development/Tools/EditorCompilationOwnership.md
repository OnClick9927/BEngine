# Package composition and shader compilation

Core Editor registers `IProjectPackageManagerFactory` with its package compiler.
The factory creates activated managers without owning their projects or lifetime;
the caller disposes each manager. Hosts can replace the factory through ordinary
service registration. EditorWorkbench no longer resolves the internal compiler or
uses the internal manager constructor. Existing public constructors and manifest
editing remain available.

`ProjectShaderCompilationSession` owns a workspace and compiler request epochs.
Prepare snapshots inputs and compiler identity on the owner thread. Build runs
once on a worker and serializes this session's invocations. A newer request or
CancelPending invalidates previous requests/results; it does not abort arbitrary
extension code. Caller cancellation remains caller-owned. Dispose cancels the
session lifetime without blocking the editor thread or disposing a gate still
used by retiring workers. Late results, cancelled results and repeated applies
cannot publish. Foreign results and wrong-thread operations are rejected.

The compiler registration owns its format query, compilation and artifact
application. Core checks the registration again before application; results from
a displaced or retired registration are not applied. Application is attempted
once even if the extension throws partway through. Graphics retains shader
parsing, import settings, compilation hashes, cached artifacts, current pointers,
pointer retirement and render cache invalidation. Core never interprets those
formats. Providers apply synchronously on the owner thread; registration changes
are serialized with application.

EditorWorkbench owns worker scheduling, cancellation of scheduled jobs, error
presentation and Play Mode refresh policy. It uses the same session for startup
and background edits. Scheduling failure invalidates pending work; queued main
thread callbacks can safely encounter a retired session. No per-frame scans or
collections were added.

`Test-EditorCompilationOwnership.ps1` uses non-friend consumers for request
ownership, cancellation, provider replacement, failure cleanup, concurrency,
retirement, collectible provider release and real package source activation.
The Graphics test uses the real adapter with a controlled byte compiler backend
to inspect cache/pointer transactions without a GPU. The actual Editor fixture
uses the Windows native shader backend, source monitoring and project reload.
Verification output belongs under workspace Temp; the short project roots avoid
the Windows SDK path limit in generated script project paths.
