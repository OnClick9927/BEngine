# Diagnostics

Profiler, frame debugger, object-memory snapshots and remote Player diagnostics.

Use **Window > Analysis > Profiler** for local CPU/GPU samples, **Memory Profiler** for live
`BObject` snapshots and reference paths, and **Remote Player Profiler** to connect to a built Player.

Start a desktop Player with:

```text
Game.exe --diagnostics-port 57891 --diagnostics-token a-long-random-token
```

`--diagnostics-host` defaults to `127.0.0.1`. Binding a non-loopback address requires a token of at
least 16 characters. The protocol reports process CPU, managed/private/working-set memory, profiler
events and counters, GPU frame time, and an on-demand engine-object heap snapshot.

Heap snapshots contain live `BObject` instances, shallow size estimates and serialized field/list/map
references. They are not arbitrary CLR retention graphs. Vulkan uses timestamps owned by each frame
resource slot; reading timing results does not introduce an extra fence wait. OpenGL uses asynchronous
timer queries.

**Window > Analysis > CLR Heap** captures actual managed objects, references and GC roots through
ClrMD, or opens a managed dump. It calculates immediate dominators and retained bytes, caches shortest
root paths, groups by type and compares type counts/bytes against a baseline. Object addresses are
snapshot-local identities; comparisons do not treat addresses as persistent object IDs. Truncated
captures flag incomplete retained estimates. The editor builds indexes and filters asynchronously,
supports cancellation and draws at most 128 rows per page.

**Window > Analysis > Rendering Analysis** records up to 512 frames and shows collection, sorting,
core geometry, submission, Present CPU time, GPU time when available, draw counts, core upload bytes
and managed allocation. Freeze a capture, choose a historical frame, set a baseline and compare costs;
export the capture as JSON. Target FPS displays the CPU frame budget and the most expensive CPU rendering
stage. Package-owned upload buffers are not included in the core upload counter.

**Window > Analysis > GPU Passes** supports capture freeze, historical frame selection, pass-name search,
sorting by GPU duration and JSON export. Passes can be nested, so their durations must not be added together.

The remote server advances through the engine's BTask frame loop. Network I/O is asynchronous;
request parsing, scene-object traversal and response capture execute on the owning engine thread.
Custom hosts must advance `BTaskScheduler.Main`. The server processes at most one request per client
per frame and accepts at most 16 active clients. Shutdown closes clients without blocking the game loop.
