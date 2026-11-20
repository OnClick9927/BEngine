# Managed Player deployment and sessions

Core Player owns `BuildTargetManifest`, its unchanged YAML/binary codec,
`LegacyProjectWorkspace`, `HostPresentationConfiguration`, the CoreCLR/interpreter
strategies and `PlayerManagedCodeRuntimeFactory`. Strategies call the neutral
`IAssemblyDomainService`; the actual system assembly loader remains in Platform.
The eight previously public types retain their namespaces and have compatibility
forwarders in DotNet. Core does not reference or forward to a package.

`ManagedCodePlayerSession` owns a prepared/active domain, its type preference and
an explicitly transferred runtime-factory service owner. It uses BTask and waits
for activation to settle before shutdown releases services. Shutdown publishes its
operation before invoking module/user cleanup, so repeated or reentrant calls
observe the same operation. This is engine-thread coordination, not an additional
background-thread service.

AssetBundles selects content/release inputs and AOT/hot-update stage policy, then
composes the Core session with diagnostic callbacks. Its Runtime and Editor
metadata no longer directly reference DotNet. Default host composition has since
moved to Core Player (see `PlayerHostOwnership.md`). Startup-log capture/lifetime
also belongs to Core; AB retains content path selection (`PlayerLogOwnership.md`).
Runtime metadata and resource archive adapters remain separate audit items.

Regression projects under `Tests/ManagedSessionOwnership` cover a Core-only
consumer with injected capabilities, data parity and an old precompiled consumer,
real native PE-module loading, actual browser interpreter loading and Editor
initialization/reload. Cross-platform compile checks do not imply device tests.
The independent test payload and results live under the workspace Temp directory.
