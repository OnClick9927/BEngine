# Visual Scripting

## Runtime

`VisualScriptRunner` executes graph events synchronously in connection order. Each runner owns an independent blackboard initialized from graph defaults and optional component overrides. `Trigger(name)` invokes named custom events.

The graph validates node, connection and variable identities and compatible variable types. `maxStepsPerEvent` terminates cyclic or unexpectedly large event flows with a visible fault state.

## Debugging

Set a breakpoint on any node. The runner pauses before executing it. Continue ignores that breakpoint once, Step executes one node, and Stop clears the pending flow. `VisualScriptDebugHub` exposes live runners, immutable variable snapshots, state changes and ordered node traces to editor tools.

## Asset format

`.visualscript.yaml` version 1 stores typed variables, nodes, ports, connections, positions and breakpoints. Fix64 values use raw signed integers. Unsupported versions, missing references and invalid type combinations are rejected when loading.
