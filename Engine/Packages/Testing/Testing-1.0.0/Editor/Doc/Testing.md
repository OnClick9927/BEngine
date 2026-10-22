# Testing workflow

## Authoring tests

Reference `BEngine.Testing` from the assembly definition that owns the tests. Test and lifecycle methods must be parameterless and may be public or private. Supported return types are `void`, `Task`, `ValueTask`, `BTask` and `IEnumerator`. A fixture needs a parameterless constructor.

Use `[SetUp]` and `[TearDown]` around each test, and `[OneTimeSetUp]` / `[OneTimeTearDown]` around a fixture run. `[Ignore("reason")]` works on a method or fixture. Every test has a timeout; set `TimeoutMilliseconds` on `[Test]` when the default 10 seconds is insufficient.

## Modes

Edit tests execute in the editor domain. Play tests own an isolated runtime scene and stop and dispose it after each test. `TestContext.Current.Tick(frames)` advances the complete BEngine lifecycle, including runtime systems, FixedUpdate, Update and LateUpdate.

Player tests use the project Build Settings, produce a new build below `Temp/TestRunner`, launch it as a child process and receive a JSON result file. The packaged `PlayerTestRuntimeSystem` is dormant unless the launcher supplies the test environment variable. The Windows editor currently launches Windows Player tests; other targets can still be built through the ordinary Player pipeline.

## Failure navigation

Discovery reads portable PDB sequence points for each test method. Selecting **Open** or double-clicking a result uses the configured external script editor and opens the first executable source line. Assertion failures retain the original exception and stack trace.

## Coverage

Coverage is opt-in to avoid changing shipping code that was not selected for measurement. Apply the fully qualified attribute:

```csharp
[assembly: BEngine.Testing.Coverage]
```

The project compiler registers executable statements and branch outcomes in the marked scope through a module initializer and inserts hit probes. Branch probes cover both `if` arms (including implicit `else`), evaluated `while`/`do`/`for` conditions, conditional expressions, and `switch` statement sections including the unmatched default path. Loop and conditional-expression conditions that declare pattern variables are left unchanged to preserve C# definite-assignment semantics. A run resets hit counts while preserving the manifest. The Test Runner reports covered/total lines and branches and exports standard LCOV (`DA`, `BRDA`, `BRF`, `BRH`) plus a detailed JSON snapshot. Player coverage is merged into the editor session after the result file is read; older line-only reports remain readable.

Coverage changes timing and should not be used for performance measurements. Generated code without a marked scope and third-party precompiled assemblies are not instrumented.
