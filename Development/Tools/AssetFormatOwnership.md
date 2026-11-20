# Source format ownership

EditorServices exposes `AssetTypeRegistry`, the asset facade and importer contracts.
The registry does not contain a catalogue of graphics or UI formats. AssetPipeline
registers its default source documents; Graphics registers images, fonts, shaders
and materials; UIElements registers its own documents; EditorWorkbench registers
the asset archive category. Managed `.asset.yaml` declarations retain precedence
over generic YAML registration, and asset/folder identity remains unchanged.

The existing registration overloads keep their signatures and behavior. A new
six-argument typed overload accepts an explicit fallback loader. The primary
`Load` method still propagates errors. Consumers may request `LoadFallback`
after handling the error; the registry does not silently retry, suppress errors,
or infer recovery from a display name. Shader/UI text fallback is declared by
those owners. Fallback receives the same source/artifact context and may return
a different BAsset kind or decline recovery. Its exceptions propagate.
The existing editor TextAsset representation adds a public text constructor for
independent providers; its original parameterless constructor and get-only public
text property remain available. No private setter access is needed across packages.

Registration replacement, removal and assembly cleanup release the associated
fallback and invalidate facade weak caches outside the registry lock. Already
returned objects remain owned by their callers; invalidation does not destroy
them. No new work, scans or allocations run every frame.
Publication also checks the cache revision under the existing lock, so a load
which replaces its own provider cannot reinsert an old result after invalidation.
The in-flight caller retains its object; subsequent callers use the new provider.
Late format refresh can
also remove a feature classification after its provider unloads. It preserves
GUIDs/settings and skips declared managed types instead of reimporting them as
generic YAML. Unknown formats use DefaultAsset; an absent optional package no
longer creates a misleading category for a feature that is unavailable.

The local Unity editor separates public AssetImporter/AssetPostprocessor extension
APIs from the default editor workbench. That is the public extension principle
used here, not a claim about Unity native import implementation ownership.

Test-AssetFormatOwnership.ps1 uses a consumer without graphics/workbench for the
registry contract, plus the real source database and an internal fake-host test
harness for facade integration. Providers themselves use public APIs without
friend grants. Tests cover longest suffixes, exact context, explicit recovery,
replacement/unload/cache lifetime, default documents, late optional modules,
metadata identity and managed-type precedence. Actual Editor smoke separately
covers installed host composition. All output belongs under workspace Temp.
