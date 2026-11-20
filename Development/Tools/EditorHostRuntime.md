# Authoring host runtime operations

`BEngine.Editor.EditorHostRuntime` is the Core Editor bridge to kernel application, clock and input mechanisms. It is independent of graphics, IMGUI, workspace windows and game frameworks. It does not pump tasks, own scenes, choose pause policy, broadcast editor events, or capture/restore workspace state.

- `ConfigureApplication` establishes edit mode and the caller-selected assets address. It validates before mutation, performs no I/O/path normalization, and preserves application identity and clock. It cannot replace an active editor Play Mode configuration.
- `PreparePlayClock` reuses the existing simulation reset between frames, retaining time scale/fixed-step settings and monotonic dispatch identity. The host explicitly captures/restores `Time.State` as part of its own Play Mode transaction.
- `EnterPlayMode` / `ReturnToEditMode` publish the host's prepared or stopped state. They do not create/start/stop scenes or emit transition events. Repeated publication is harmless. Bootstrap is required, and active runtime frames cannot be interrupted.
- `AdvanceEditFrame` uses the existing edit clock semantics, including scaling, maximum delta and negative-delta clamping. It neither begins a fixed-step frame nor ticks scenes. It rejects Play Mode and active runtime frames to prevent double advancement.
- `CompleteInputFrame` delegates transient input cleanup to the active platform provider, between runtime frames. Held keys, composition, axes, cursor position and connected devices retain their existing semantics; transient edges, text, deltas and touch phases follow that provider's policy.

EditorWorkbench retains transition sequencing, clock snapshots, rendering-state restoration, pause/step decisions and all UI policy. Normal input completion stays before late window/source work. Its outer failure path completes input if earlier work, including profiler setup or task pumping, fails. A failed completion attempt is not retried; profiling cleanup still runs. Manual steps retain their existing finally cleanup. The bridge adds no per-frame collections, delegates, reflection, registrations or scans.

No Core Runtime/Platform implementation, manifest, asset format, friend grant or public state setter is added. Player distribution does not acquire an editor dependency. Existing editor extensions can recompile against the new Core Editor API.

`Test-EditorHostRuntime.ps1` exercises an independent non-friend consumer: bootstrap validation, exact state/address ownership, mode/frame guards, scaling and resets, explicit clock restoration, keyboard/mouse/touch/gamepad cleanup, paused/normal/manual/repeated simulation, task-pump separation and warmed allocation measurement. The temporary real Editor fixture invokes actual native-update and Step paths, including an invalid-elapsed-time failure, without replacing production services. It also verifies repeated Play Mode shutdown and original-scene restoration.
