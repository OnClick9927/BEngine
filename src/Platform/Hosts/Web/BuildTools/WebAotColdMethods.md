# Optional Web native-code exclusions

Web builds use the existing balanced native AOT selection by default. A project can add `ProjectSettings/WebAotColdMethods.json` to omit individually validated native methods while retaining their original managed IL and interpreter fallback. The build provider discovers this file automatically. It is a build input and is not copied into the Web distribution. This mode cannot be combined with the older `WebAotProfile.json` type selection.

The schema is strict:

```json
{
  "Format": "BEngine.WebAotColdMethods",
  "Version": 1,
  "Methods": [
    {
      "Assembly": "MyGame",
      "Type": "MyGame.OptionalPanel",
      "Name": "BuildDetails",
      "GenericArity": 0,
      "Signature": "<exact build-tool scoped signature>",
      "BodySha256": "<64 lowercase hexadecimal characters>"
    }
  ]
}
```

Entries describe method definitions, not a runtime token, module MVID or a closed generic instantiation. The signature encodes declaring assembly/type, name and generic arity through the entry key, plus calling convention, return and parameter types. Type identities retain assembly scopes, positional generic parameters, modifiers, function pointers and array shapes. The method-body SHA-256 covers instruction operands and branch targets, locals, exception handlers, implementation/method flags and generic constraints. These are generated metadata keys, not hand-written Mono display signatures.

An exclusion applies only when the current linked method has an unambiguous exact signature and the recorded body hash. Unknown assemblies, new methods, changed signatures or bodies and ambiguous definitions retain the existing baseline native selection. Invalid JSON, unknown/duplicate fields, duplicate method entries and invalid hashes fail the build. The policy never broadens the baseline AOT set: decoder and SortedSet compatibility boundaries keep priority.

After exclusions, the build restores the complete existing native Core/math/rendering, FreeType/HarfBuzz adapter, Host and JavaScript bridges. It follows direct method calls and delegates, declaring type initializers and static field owner initializers inside the baseline native set. If the SDK profile cannot distinguish signatures, the whole group remains native whenever any member is retained. An unresolved closure or managed indirect `calli` restores the complete original native selection and reports why; it does not guess that the target is cold. Virtual override dispatch keeps interpreter fallback and still requires gameplay and performance verification.

The generated `engine.aotprofile.cold-methods.json` records the policy hash, actual input PE SHA/MVID, every configured match/nonmatch/body change, all selected definitions, reenabled exclusions and reasons, and unresolved or CLR-synthesized array references. The adjacent normal profile summary describes the original baseline selection; the cold-method audit records the final selection. Existing frozen image and actual compiler-token verification remains mandatory and unchanged. The selector reads PE files and never rewrites IL or metadata.

To prepare or refresh a policy, profile representative real gameplay and all scenes, resolve observations against the exact source metadata, preserve hot native paths and validate the resulting exclusions against the current linked image graph. The build-only `WebAotColdMethods.Describe` helper produces the scoped signature and body hash for an exact Cecil `MethodDefinition`. A profiler resolution record is not an invocation count, and an unrecorded method is not proven unused. Keep raw captures and before/after frame, loading and memory evidence. Revalidate a policy after gameplay or SDK changes; stale entries conservatively return to native compilation.

This is a size/performance tradeoff, independent of managed metadata trimming. It cannot guarantee a fixed Web size or frame rate. Without the policy file the generated AOT profile remains byte-identical to the existing default.
