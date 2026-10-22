# Visual Scripting

Create a `.visualscript.yaml` asset, open it from **Window > Scripting > Visual Script**, and assign it to a `VisualScriptRunner`.

Graphs provide Start, Update and named custom events; typed blackboard variables; numeric comparisons and branches; variable, Transform and log actions; and explicit Stop flow. The editor uses the shared `NodeCanvas` for selection, connections, moving, panning and zooming.

Breakpoints pause before a node. The Runtime Debugger shows the current node and a copied variable snapshot, and provides Continue, Step and Stop controls. Each event has a configurable execution step limit so accidental cycles fault instead of hanging the game loop.

The version 1 YAML format stores fixed-point numbers and vectors as raw integers. Runtime loading, scene references and AssetBundle loading use the same codec.
