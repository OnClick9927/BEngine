# Animation

Fixed-point animation package with animation curves, YAML clips, Animator controllers and state machines.

- Runtime assembly: `BEngine.Animation`
- Editor assembly: `BEngine.Animation.Editor`
- Package ID: `com.bengine.animation`
- Editor icon: `EditorResources/Animation.png`

The package keeps its runtime and editor assemblies beside this shared `package.yaml`. The empty `Resources`
directory is reserved for runtime assets that are actually loaded by the package.

Offline documentation is stored at `EditorResources/Doc/index.html` and opens from the package details page.

Import examples from `EditorResources/Examples` in Package Manager. `AnimationGettingStarted.bpackage` covers
curves, events and looping playback; `StateMachine.bpackage` covers parameters, transitions and state changes.
