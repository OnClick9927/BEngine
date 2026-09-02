---
name: bengine-audio
description: Develop, author, debug, or extend BEngine's 2D Audio package, including AudioClip import, AudioSource, AudioListener, WAV decoding, mixing, output backends, Resources, AssetBundle, Inspector workflows, and Audio package examples.
---

# BEngine Audio

## Establish the package state

- Open `Window/General/Package Manager`, select `Audio`, and press Import. The package ID is `com.bengine.audio`; it is disabled by default and its runtime assembly depends only on Core.
- Confirm both `BEngine.Audio` and `BEngine.Audio.Editor` compile. Runtime code belongs under `src/Packages/Audio/BEngine.Audio`; importer, Inspector, icon, menu, and authoring code belongs under `BEngine.Audio.Editor`.
- The package is deliberately 2D. Do not add or promise Transform-distance attenuation, 3D spatial blend, doppler, volumetric zones, or 3D listener orientation unless the user explicitly changes the engine's 2D-only scope.
- Open the Package Manager Examples section and import `AudioGettingStarted.bpackage`. It installs to `Assets/Examples/AudioGettingStarted`; import/reimport must preserve locally modified files and repair missing owned files.
- Import `OneShotMixer.bpackage` independently. It installs to `Assets/Examples/OneShotMixer` and demonstrates two real WAV clips, overlapping one-shot voices, per-call gain, stereo pan, source mute, and listener pause in its own runnable scene.

## Run the complete example

- In Project, expand `Assets/Examples/AudioGettingStarted/res` and open `Audio.scene.yaml`. The package example must always include this runtime scene, `AudioDemo.cs`, an asmdef, a real PCM WAV, meta files, and a detailed Readme. Never replace the scene with a code-only sample.
- Keep Scene, Game, Inspector, Hierarchy, Project, and Console visible. Select `Music Source` in Hierarchy. Inspector should show an `AudioSource` with a real `AudioClip` ObjectField reference plus Play On Awake, Loop, Mute, Volume, Pitch, Stereo Pan, Priority, Playing, and Time.
- Enter Play. Space calls Pause/UnPause, R restarts, Up/Down adjusts volume, and Left/Right adjusts stereo pan. Watch Inspector time and Game feedback. Use Pause/Step when examining one update. Stop restores the authored scene state.
- If startup or compilation fails, surface the failure in an Editor dialog and Console. Do not leave a click that silently does nothing.

## Import and inspect audio assets

- Drag a `.wav` file into Project or place it below Assets and Refresh. `AudioImporter` reads settings from the meta document and creates an `AudioClip`; the runtime codec handles the same bytes when loaded through Resources or AssetBundle.
- Select the WAV in Project. Inspector preview and metadata must show sample frames, channels, frequency, duration, and load state. The resizable preview belongs to the asset Inspector; preserve its height per window.
- Edit Force To Mono to average all source channels. Edit Normalize to scale a nonzero peak below 1 to full scale. Load In Background and Preload Audio Data are persisted importer settings; do not claim streaming behavior that is not implemented.
- Supported WAV payloads are PCM 8/16/24/32 bit and IEEE float 32 bit. Reject malformed RIFF length, absent fmt/data chunks, unsupported compression, invalid channel count, invalid sample rate, and misaligned sample data with actionable errors.
- Project displays the asset name without its file suffix. The Audio icon comes from the package Editor icon registration and must be available after package reload.

## Author scene components

- Add a source with Inspector `Add Component/Audio/Audio Source`, top menu `Component/Audio/Audio Source`, or the equivalent Hierarchy component workflow. `AudioSource` is a `MonoBehaviour` and serializes its authored properties.
- Add one listener with `Add Component/Audio/Audio Listener`. The current listener component is a scene marker; global control uses `AudioListener.volume` and `AudioListener.pause`.
- Assign `AudioSource.clip` by dragging the AudioClip from Project into its ObjectField. A valid drag shows the accepted cursor only while the pointer is inside the field and the dragged type is assignable. Releasing outside or with a wrong type must not assign.
- Starting a drag must not change Selection on mouse-down. Selection changes only after mouse-down and mouse-up complete in the original row without a drag threshold being crossed, so the Inspector does not need to be locked before assigning an object.
- The ObjectField selector button opens the dedicated searchable object picker. Assets and Scene are separate TreeView tabs. Assets begins with children of Assets and does not draw the Assets root. Scene shows compatible BObjects only.
- Single-clicking an ObjectField reference pings the target. Project or Hierarchy expands every parent in its chain and draws the yellow ping frame around the real row. Double-clicking changes Selection. The field itself does not pulse.
- Inspector edits call `Undo.RecordObject(source, "Edit Audio Source")` before assignment, then `EditorUtility.SetDirty(source)`. Locked inspectors retain their current BObject; unlocking immediately creates an Editor for the current Selection.

## Use runtime APIs correctly

- Use `AudioClip.Create(name, lengthSamples, channels, frequency)` for generated PCM. Channels must be 1 through 8, frequency 8000 through 384000, and lengthSamples positive. Streaming creation currently throws because no built-in streaming decoder exists.
- `AudioClip.SetData(data, offsetSamples)` and `GetData(data, offsetSamples)` use a frame offset and interleaved sample arrays. Both return false for ranges outside the clip. Input samples are clamped to `[-1, 1]`.
- `AudioClip.Load(path)` reads a WAV. Use `Load(path, forceToMono, normalize)` to apply import-equivalent conversion outside AssetDatabase.
- `AudioSource.Play` starts from sample zero. `PlayDelayed` starts the source and delays mixing by a nonnegative Fix64 duration. `Pause` keeps the sample cursor; `UnPause` continues; `Stop` resets the source and clears one-shot voices.
- `PlayOneShot(clip)` uses full volume. `PlayOneShot(clip, scale)` clamps scale to `[0,1]`; an explicit zero stays silent and is not treated as an omitted default.
- `timeSamples` is clamped to a valid frame. `time` converts seconds through the clip frequency. `isPlaying` is false while paused.
- Source gain is `source.volume * AudioListener.volume`; stereo pan attenuates the opposite channel. Pitch controls source-frame advance. Loop wraps the sample cursor. Mix order is stable by priority and instance identity.
- `AudioRuntimeSystem` obtains sources from each loaded scene through the nonallocating `Scene.GetComponents<T>(List<T>)` API, reuses its list and pooled PCM buffer, and returns early when no output/source work exists. Do not introduce LINQ or per-frame arrays in this path.

## Load through Resources and AssetBundle

- `RuntimeAssetCodecRegistry` associates `.wav` with an AudioClip byte decoder. `ResourceLoader.Decode` consults registered codecs before falling back to general BAsset loading.
- Load asynchronously with `Resources.LoadAsync<AudioClip>(path)` and inspect `ResourceRequest.progress`, `isDone`, and `asset`. Keep the returned resource handle/lease alive while consumers need deterministic residency, then release it.
- AssetBundle requests should use the generic async load APIs and return an AudioClip decoded from bundle bytes. Runtime never writes BAssets back to disk.
- Registrations are package-owned. Editor package unload must call `RuntimeAssetCodecRegistry.UnregisterAssembly(runtimeAssembly)` along with importer, icon, component menu, custom editor, gizmo, and other static cleanup.

## Replace or debug the audio backend

- Implement `IAudioOutput` for another platform or audio API. Expose SampleRate and Channels, implement Start, Submit, Stop, and Dispose, and accept interleaved float PCM blocks from the mixer.
- Install it with `AudioOutput.SetBackend(customOutput)`. `AudioOutput.ResetToDefault()` disposes the custom backend and retries the built-in OpenAL selection.
- The OpenAL backend dynamically resolves Windows `openal32.dll`, Linux `libopenal.so.1`/`libopenal.so`, or the macOS OpenAL framework. It maintains queued streaming buffers and deletes source, buffers, context, device, and library on Dispose.
- If OpenAL cannot initialize, the factory logs a warning and selects the Null backend. This fallback keeps deterministic scene logic/test execution but produces no sound; diagnose it in Console rather than assuming the mixer is broken.
- Use the Profiler Runtime domain to inspect Audio Mix method samples and memory counters. Verify the mix hot path does not allocate in steady state. Use Console multi-line entries to inspect native load failures and WAV decode errors.

## Extend importing and authoring

- For a new format, create an Editor `AssetImporter`, persist each setting using `nameof`, and register the extension with `AssetTypeRegistry.Register<TAsset>`. Provide an Editor icon under package `Editor`, not an `EditorResources` directory.
- Register a matching runtime byte decoder so Resources and AssetBundle can reconstruct the BAsset without referencing Editor. Keep format parsing bounded, validate lengths before slicing, and reject unsupported variants clearly.
- For a custom AudioClip Inspector, derive from `BEngine.Editor.Editor`, use `EditorGUILayout` APIs with the current `GUIStyle` when no style is passed, keep runtime metadata read-only, and make any preview height draggable.
- For new source fields, ensure scene object-graph serialization supports the type, Inspector nested fields indent by property depth, prefab clone/instantiate round-trips it, and Undo restores it.
- New package UI uses existing `EditorStyles`, GUISkin, GenericMenu wrapper, TreeView, ObjectField, DragAndDrop, and EditorUtility APIs. Do not create a parallel widget system.
- Add external profiler areas through `EditorProfilerModuleRegistry`, clearly marking samples as Editor or Runtime and providing class, method, call stack, allocation, and rendering/memory counters where relevant.

## Validate changes

- Build `src/Packages/Audio/BEngine.Audio/BEngine.Audio.csproj`, then the Editor owner project with `--disable-build-servers -m:1` so its source package export is deterministic.
- Run `Example/Tests/SceneRuntimeArchitecture`. Cover PCM/float WAV decoding, malformed files, mono conversion, normalization, Resources codec dispatch, playback, looping, one-shot scaling, pan, pitch, pause, source lifetime, output fallback, and zero steady-state allocations.
- Run `Example/Tests/PackageExamples` to import and compile the actual `.bpackage`, validate the runtime scene, repair/reimport behavior, asmdef references, and enabled package dependency.
- Run `Example/Tests/PackageDocumentation` to validate Readme sections, real rendered overview image, HTML figure/figcaption, complete editor-operation Skill, and exported copy equality.
- Inspect Project and Inspector manually: import WAV, change settings, Undo/Redo, drag assign without Inspector lock, open the selector TreeView, ping an asset inside collapsed parents, Play/Pause/Step/Stop, and unload/reload the package.
- Verify Output/Packages/Audio contains source `.cs`, both asmdef files, package.yaml, Resources, Editor/Readme.md, Editor/Doc/images/overview.png, Editor/Skills/bengine-audio/SKILL.md, and only `.bpackage` files under Editor/Examples. It must contain no csproj, DLL, PDB, bin, or obj.

## Troubleshoot without guessing

- No Audio menu or Inspector means the package is disabled, compilation failed, or registration was not re-run after reload. Check Package Manager and Console first.
- A null clip after scene load means the asset path/GUID could not resolve, the runtime codec was not registered, or the package was not enabled before asset loading.
- A valid source with no output means check `AudioListener.pause`, listener volume, source mute/volume, sample data, backend initialization, and whether the app is paused or unfocused with runInBackground disabled.
- Distortion means inspect source gain summation and output clipping. Wrong speed means inspect source frequency, output sample rate, pitch, and cursor step. Wrong stereo means inspect channel count and pan gains.
- Example import disabled means the package is disabled or Editor is in Play mode. Stop, enable/import the package, then import or reimport the example.
- Never add 3D rendering, Transform z-distance, 3D physics, 3D audio, meshes, or perspective camera concepts as an attempted fix. Keep the package aligned with the engine's 2D architecture.
