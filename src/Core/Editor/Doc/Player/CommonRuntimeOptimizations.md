# Shared runtime work and allocation

These paths live in Core or the UIElements package, so the same implementation
is used by desktop, Android, iOS and Web. They do not depend on worker threads or
native platform APIs.

- Cooperative work retains one linked-list node per registration and one frame
  handler per scheduler. Continuing, blocking, or changing priority reuses the
  node. Weighted FIFO ordering, cancellation and owning-thread rules remain the
  same; the detached node never means that executing work is still queued.
- Canceled delays are filtered from the heap's unordered view, then the retained
  `(due time, sequence)` priorities are heapified together. This avoids removing
  and reinserting every live delay separately. Scratch storage is reused up to
  4,096 entries, cleared of source/continuation references after each pass, and
  dropped after larger passes. The delay heap's existing capacity is unchanged.
- Cancellation snapshots use a separate intrusive link instead of allocating a
  temporary callback list. The ordinary registration links remain mutable, so
  one callback may still dispose any later registration. Callback order,
  immediate registration on canceled tokens and exception reporting are kept.
  The snapshot link adds one reference field to each registration node.
- AsyncLoadBatch workers yield between items when an active shared frame budget
  is exhausted, including when loaders hit a synchronous cache. Outside a frame,
  synchronous batches can still finish immediately. This is a soft guard: a
  running loader is indivisible and each newly started worker may process its
  first item before yielding. Result order, progress polling and ownership do
  not change; cancellation/failure waits for sibling loaders to settle.
- Wrapped advanced text slices the original paragraph using grapheme offsets
  rather than building a temporary list for each shaped prefix. It still shapes
  the same prefixes, respects the existing bidi/grapheme rules and shares the
  existing bounded layout cache. This reduces temporary allocation; it does not
  eliminate prefix shaping or change line-breaking policy.
- Resource extension fallback finds the ordinal-ignore-case minimum directly,
  preserving exact-file priority and first-match behavior for equivalent names
  without creating an ordering pipeline.

Regression checks should compare due-time/FIFO order, callback disposal during
cancellation, blocked/reprioritized work, batch cancellation/failure ownership,
and text metrics plus rendered pixels against the prior runtime. Allocation
measurements exclude request creation and warm retained storage first. Local
microbenchmarks describe these paths only, not whole-game frame rate or loading
time on every device.
