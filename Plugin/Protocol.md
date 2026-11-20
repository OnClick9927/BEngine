# IDE integration protocol v1

The optional editor-only `com.bengine.ide-integration` package owns this protocol. Core exposes code-model and editor-extension mechanisms without knowing the package or either IDE. There is no debug server, network listener, global filesystem scan or custom CLR debug adapter.

## Project discovery

Locate the closest ancestor containing a valid `Project.yaml` for the current workspace folder, solution or active file. Do not recursively search drives. All paths below are relative to that project root.

`Library/IDE/project.json`:

```json
{
  "schemaVersion": 1,
  "projectPath": "E:/Games/MyGame",
  "projectName": "My Game",
  "solutionPath": "E:/Games/MyGame/BEngine.Project.sln",
  "engineVersion": "0.1.0"
}
```

Paths are absolute and must resolve inside the discovered project where required. Generated C# project files are under `Library/IDE`; the solution is `BEngine.Project.sln` at the project root. They describe actual script assembly boundaries, source files, references and defines. They must not shadow the engine's script compiler or copy project sources.

## Debug targets

`Library/IDE/Targets/<processId>.json`:

```json
{
  "schemaVersion": 1,
  "processId": 12345,
  "processStartTimeUtc": "2026-11-04T07:00:00.0000000Z",
  "projectPath": "E:/Games/MyGame",
  "projectName": "My Game",
  "kind": "editor",
  "displayName": "My Game — BEngine Editor",
  "executablePath": "E:/BEngine/BEgine/BEgine-0.1.0/BEngine.Editor.exe",
  "solutionPath": "E:/Games/MyGame/BEngine.Project.sln",
  "runtime": "coreclr"
}
```

Publish atomically when the package starts; remove this process's own record when it stops. Readers reject unsupported schemas, invalid PIDs, paths belonging to another project, dead processes and reused PIDs with a different creation time. A leftover record after a crash is not permission to attach to an unrelated process. Revalidate immediately before invoking the IDE debugger. One target attaches directly; multiple targets are selectable. Cancellation does not attach anything.

The target intentionally contains no authentication token or debugger command. An IDE invokes its installed .NET debugger against the validated local process. Exact-source matching should remain enabled; the engine emits PDB mappings to the original project source even when its loop guard instruments compilation snapshots.

## Synchronization requests

Write `Library/IDE/Requests/<requestId>.json` using an unpredictable GUID request ID, a temporary file and an atomic rename:

```json
{"schemaVersion":1,"requestId":"GUID","command":"sync"}
```

The editor processes a bounded number of requests at intervals, not per frame. It accepts only the documented command and safe request identifiers; request content cannot select an executable or an arbitrary output path.

The response, atomically published at `Library/IDE/Responses/<requestId>.json`, is:

```json
{"schemaVersion":1,"requestId":"GUID","success":true,"solutionPath":"E:/Games/MyGame/BEngine.Project.sln"}
```

On failure, `success` is false and `error` explains the failure. Readers validate the schema and matching request ID, enforce timeout/cancellation, and clean up their request/response files. The editor must be running with IDE Integration installed. No IDE should silently fall back to an obsolete solution after synchronization failed.
