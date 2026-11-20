# Player startup log ownership

Core Player owns `PlayerStartupLog`: process-wide log capture, host headers,
timestamped flow/failure records, engine/platform/process event subscriptions,
ordered buffered writing, fallback when a chosen file cannot be opened, and
shutdown. It reuses `IPlayerLogWriter` / `PlayerHostServices.CreateBufferedLogWriter`
and the public filesystem, path, application-path and platform capabilities.
It neither selects a resource format nor depends on a feature package.

The public `Start` overload accepts a chosen path and optional explicit host
capabilities. The existing process-wide behavior remains: an active session is
reused, and it must be disposed before beginning another session. Disposal drains
accepted records and detaches subscriptions. Browser hosts retain synchronous
writing and do not subscribe to `AppDomain.ProcessExit`.

AssetBundles retains only its content-specific path selector and compatibility
calls. That selector reads deployed metadata/resource layout, persistent-data
overrides and the existing log-path override. It supplies the resolved path to
Core; it does not own a writer, mutable current session or event subscriptions.
Its flow-label mapping and AB update diagnostic detail remain domain policy.

The migration changes no stored scene/Prefab/resource identity or log format.
Existing public AB APIs are preserved; the old diagnostic implementation was an
internal helper, not a serialized type. Core does not introduce a package type
forwarder. No per-frame lookup, background worker or queue is added beyond the
existing startup log writer.

The local Unity 2022.3.62f2c1 CoreModule XML exposes
`Application.logMessageReceived` / `logMessageReceivedThreaded`. This supports
the distinction between engine-wide log capture and content-domain policy; it
does not establish Unity's native log writer implementation or file policy.

`Tests/PlayerLogOwnership` has shared behavioral checks for native and actual
browser hosts, plus an Editor initialization/reload ownership probe. The tests
cover capture, synchronous error flush, event retirement, append/reuse,
unavailable paths, fallback and retry. Native readers open files with
`FileShare.ReadWrite` while a writer is active. Verification output belongs under
the workspace Temp. This migration does not complete the separate AB metadata,
resource archive or all-package semantic reviews.

An actual self-extracting Desktop executable exposed a manifest lookup gap:
`AppContext.BaseDirectory` points at extraction, while `player.modules.json`
remains beside the executable. Core now falls back to the process directory
provided by the neutral application-path capability when the original location
has no manifest. Original-location precedence, optional missing manifests and
module identity validation are retained; no package identity is hardcoded.
The shared native/WASM probes cover both locations, precedence, absent process
capabilities and relative-path rejection. The Desktop controlled-failure test
uses the actual bundled executable, not only its managed entry assembly.
