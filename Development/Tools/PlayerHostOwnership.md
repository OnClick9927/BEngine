# Player host and content ownership

`BEngine.Player.PlayerHost` owns service composition, platform lifetime, window
execution, validation, runtime-session construction and ordered startup cleanup.
It depends on neutral Core/Platform contracts and never names a feature package.
`AddBEnginePlayerRuntimeServices` registers the shared managed-code strategies.

`IPlayerContentWorkflow` supplies content services, a workspace, resource/log
leases and asynchronous content preparation. Factories own partial work until
they return; the host owns returned leases. It releases all startup owners even
if an earlier release throws, reporting combined errors after cleanup. The normal
shutdown path adds no exception-list allocation. No new per-frame scan is added.

Runtime service composition also settles contributions registered during that
composition pass. Each contribution method executes once per pass, and same-
collection reentry is ignored. The stable case retains its single snapshot;
only lazy registration needs a visited set. A failed pass releases its guard so
the caller can retry. This avoids relying on repeated Add-platform calls to make
late dependency modules visible and does not add package identities to Core.

`PlayerHostRegistry.RegisterContentWorkflow` uses the shared host and records the
contributor's type as registration owner. Retiring that assembly clears the Core
reference. Retiring Core must not clear a different contributor. Legacy custom
`IPlayerHost` registrations remain supported.

AssetBundles implements the content adapter. `AssetBundlePlayerHost` retains its
public namespace, overloads, constants and concrete session return type, and
delegates to Core. Its public managed-code DI extension delegates to the shared
Core registration method. Player/Editor platform code does not name this adapter.
Collectible editor package modules still avoid global Player registration.

AssetBundles explicitly composes its networking prerequisite through
`NetworkingServices.Configure`, which preserves host overrides. Registering an AB
factory must not depend on the networking module being incidentally initialized
later by reflection or a second platform-registration call. The wiring belongs to
these packages; Core does not name either service or factory.

Tests in `Tests/PlayerHostOwnership` cover an independent native consumer, failure
cleanup, contributor retirement, actual browser execution and actual Editor
initialization/reload. Existing Player content transaction tests remain required.
Verification output belongs under the workspace `Temp`, including source-build
logs, API metadata comparisons and publication manifests.

Startup-log capture and session lifetime now belong to Core Player through
`PlayerStartupLog`; AB only selects content-specific paths. See `PlayerLogOwnership.md`.
This is one completed migration within an ongoing audit. AssetBundles runtime
metadata, built-in archive adapters and content-specific path policy still require review.
Cross-platform compilation does not imply mobile device or full Example/Snake
Player regression, and these checks alone do not demonstrate a CPU/memory gain.
