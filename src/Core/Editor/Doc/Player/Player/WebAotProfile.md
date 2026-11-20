# Optional Web AOT hot-type selection

The default Web build keeps the engine's existing AOT selection. A project can opt in by adding `ProjectSettings/WebAotProfile.json`; the build provider passes that file to the metadata profile generator. This controls native compilation only. Unselected managed methods retain their IL and the hybrid interpreter fallback.

For individually validated exclusions with conservative defaults for new or changed methods, see [WebAotColdMethods.md](WebAotColdMethods.md). The two optional selection modes cannot be combined.

```json
{
  "Format": "BEngine.WebAotProfile",
  "Version": 1,
  "Assemblies": ["Game.Runtime"],
  "HotTypes": [
    { "Assembly": "Game.Runtime", "Type": "Game.Runtime.Simulation" },
    { "Assembly": "Game.Runtime", "Type": "Game.Runtime.Outer`1/Nested" }
  ]
}
```

Names must match linked metadata exactly. Nested types use `/`, generic arity uses a backtick, and compiler-generated names such as `Game.Runtime.Loader/<LoadAsync>d__3` are supported. This is a type selection file, not a runtime trace/token list. Invalid format/version, empty records, duplicate fields/entries, unknown fields, BCL assembly selections and hot types outside the covered assembly list are rejected.

Names are literal metadata keys, including punctuation in generated local-function names such as `Loader/<<Load>g__Read|0_0>d`. They are not parsed as file paths or reflection type expressions. A spelling that is not present in the linked metadata is reported as unmatched.

Only methods already admitted by the engine's current type policy are considered. Within listed non-BCL assemblies, all methods of a selected type stay native. Virtual/interface implementations, methods on generic owners, generic methods and `Dispose`/`DisposeAsync` also retain the existing native selection. Unlisted assemblies, including newly added modules, use the previous policy. BCL type selection is unchanged.

Normal managed trimming can remove configured types or whole optional assemblies. The generated `.aotprofile.json` reports those as `UnmatchedTypes` and `UnmatchedCovered` without treating them as hot; if no hot type matches at all, generation fails. The report also records the input profile's SHA-256, matched type count, per-assembly selected method counts and additional interpreted method counts.

**A missing sample is not evidence that a method is cold.** Sampling misses inlined callees and may miss rare gameplay, serialization, initialization or generic paths. A trace converted directly into a sparse type list can make the build much slower: helpers formerly inlined into native methods can then execute through the interpreter. The interpreter fallback preserves supported behavior; it does not guarantee the same performance.

Before enabling a project profile, inspect the static call closure of the selected workload, include arithmetic/dispatch helpers and validate actual startup, scene loads, representative gameplay, generic/interface paths and frame times against the default build. Cover every supported workload and use a conservative selection when coverage is uncertain. Keep the file absent when no validated project policy is available. An explicit `WasmAotProfilePath` continues to override generated profiles through the existing build contract.

The generated `balanced` and `full` policies both keep ``System.Collections.Generic.SortedSet`1``, its nested types and ``TreeWalkPredicate`1`` in IL. The installed Mono 10.0.12 hybrid runtime fails the generic delegate closure in `SortedSet<T>.CopyTo` when called from interpreted code; retaining that family together in the interpreter passes the reference, primitive value and user struct cases. Other collection families keep their existing AOT policy. This is a specific compatibility guard, not a guarantee that every .NET generic bridge is supported. A fully custom `WasmAotProfilePath` must preserve equivalent runtime compatibility protections and undergo the same semantic tests.
