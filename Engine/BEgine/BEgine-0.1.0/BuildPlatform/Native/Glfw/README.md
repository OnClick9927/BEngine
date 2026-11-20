# GLFW Windows ownership fix

These are altered GLFW 3.4 Windows libraries, built from the official release
with one Win32 event-polling fix. The upstream license is in `LICENSE.md`.

`AttachThreadInput` can share input state with another thread or process. During
focus changes `GetActiveWindow` can then return a foreign GLFW window. Its `GLFW`
HWND property contains a pointer valid only in its owning process/library instance.
Upstream's modifier-key release workaround dereferences this foreign pointer.

`win32-window-ownership.inc` replaces that single property lookup with a lookup
in the current GLFW instance's window list. Foreign windows are skipped while
the normal Shift/Windows-key release workaround remains active for local windows.
No managed callbacks, frame timing, rendering, or input features are removed.

Rebuild with Visual Studio C++ tools, CMake, and PowerShell 7:

```powershell
./Build-Glfw.ps1 -IntermediateDirectory <repository>/Temp/Engine/Build/Glfw
```

The script downloads the pinned official `3.4` archive and verifies SHA256
`A133DDC3D3C66143EBA9035621DB8E0BCF34DBA1EE9514A9E23E96AFD39FD57A` before applying
the exact patch. It builds x64 and x86 Release DLLs with the static MSVC
runtime; no additional VC runtime installer is required. All source and CMake
intermediates remain in the supplied directory. Use `-RuntimeIdentifiers` to
rebuild one architecture. ARM64 is an optional build target requiring the Visual
Studio ARM64 C++ tools; no patched ARM64 DLL is currently supplied or tested.
Its existing SDK asset remains unchanged. No NuGet cache files are modified.

`BEngine.GlfwSafety.targets` substitutes these assets in both normal build outputs
and the SDK's resolved publish items before bundling. It is imported by source
builds and exported Player BuildHosts; exported BuildPlatform and BuildRuntime
therefore carry the same corrected Windows libraries.

Validation reproduces the original access violation with two isolated GLFW
processes sharing an input queue, then verifies 1,000 foreign-window polls,
ordinary keyboard input and synthetic modifier release using the corrected DLL.
