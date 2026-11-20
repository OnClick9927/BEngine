# Core architecture

`Core` is built into every engine installation. Its modules are source folders,
not installable packages: they have no `package.yaml` and cannot be disabled in a
project. Core compiles into exactly three assemblies:

| Assembly | Responsibility |
| --- | --- |
| `BEngine.Core` | Runtime objects, scenes, values, scheduling, coroutines, serialization, assets and platform interfaces. |
| `BEngine.Core.Editor` | Package management, compilation, shared editor services, asset authoring and Player build tools. |
| `BEngine.Player` | Player startup and content session composition. |

Previously compiled extensions must be rebuilt against these assembly names.

Source lives directly under its owning assembly directory. Runtime modules such as
`Math`, `Serialization`, `Foundation` and `Coroutines` are under `BEngine.Core`;
`Compilation`, `EditorServices`, asset authoring and Player build workflows are
under `BEngine.Core.Editor`. Player runtime source is under `BEngine.Player`.
The shared `Editor` directory contains engine documentation and examples.

| Layer | Module | Responsibility |
| --- | --- | --- |
| Kernel | `BEngine.Core` | DI contracts, service module registration and bootstrap lifetime. |
| Kernel | `BEngine.Core.Editor` | Package definition rules, version selection and assembly plans. |
| Values | `Attributes` | Shared runtime, authoring, identity and AOT annotations. |
| Values | `Math` | Deterministic arithmetic, geometry, color, random and layer values. |
| Values | `BuildCore` | Build targets, backend identity and stable artifact keys. |
| Values | `Collections` | Bounded caches and reusable value buffers. |
| Values | `Serialization` | Format independent contracts and YAML, JSON and XML codecs. |
| Execution | `Async` | Frame driven tasks, cancellation and cooperative work. |
| Execution | `Instrumentation` | Runtime counters, profiling hooks and allocation tracking. |
| Execution | `Foundation` | Objects, components, scenes, resources and their runtime lifecycle. |
| Execution | `Pooling` | Generic object and collection rentals; no game domain policy. |
| Execution | `Coroutines` | Coroutine scheduling and yield instructions. |
| Integration | `AssetPipeline` | Source asset import/index backend, with authoring operations in Core Editor. |
| Integration | `Player` | Player startup, content session composition and common build/publish mechanisms. |
| Authoring | `Compilation` | Project/package discovery, source compilation, activation and reload. |
| Authoring | `EditorServices` | Shared asset editing, Undo/Selection, scene authoring and editor capability contracts. |

The kernel has no references to feature packages or platform implementations.
Core modules depend downward on contracts and values; feature packages depend on
Core through `hostReferences` in `package.yaml` and matching asmdef references.
Platform contracts and implementations remain under `src/Platform`. The platform
layer supplies OS, window, input, graphics and publishing services through Host
interfaces; Core does not choose a concrete platform.

`BEngine.Core.Editor` retains explicit friend access for existing editor extensions
that use its internal authoring APIs. This compatibility surface is recorded by
the package boundary check; new feature code should use public contracts.

`BEngine.Core.Editor` is excluded from Player runtime distribution. Feature packages retain domain-specific
rendering, media, UI, gameplay and tooling. Their source and resource exports
remain in `Engine/Packages`; Core assemblies and common Editor resources ship
with the engine host.

`src/Packages/ResponsibilityMap.md` records the remaining feature package
boundaries and cross-layer ownership decisions.
