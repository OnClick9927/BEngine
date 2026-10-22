# Basis Universal encoder

`win-x64/basisu.exe` is built from the official v2_50 commit
`9bebe16726b3a61c8c213eeee3b7cffb462ef34e`. It is used only by the editor to
encode ETC2 RGBA8 and ASTC 4x4 texture artifacts; no executable is required by Player decoding.

Build provenance and binary SHA-256 are in `build-manifest.json`. The Apache-2.0
license and upstream third-party notices are distributed alongside the binary.
No network download occurs during texture import.

To reproduce on a machine with Visual Studio 2022 C++ and CMake:

```powershell
git clone --branch v2_50 --depth 1 https://github.com/BinomialLLC/basis_universal.git basis-source
cmake -S basis-source -B basis-build -G "Visual Studio 17 2022" -A x64 -DBASISU_EXAMPLES=OFF -DBASISU_SSE=OFF -DBASISU_STATIC=ON -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded -DCMAKE_CXX_FLAGS=/utf-8 -DCMAKE_C_FLAGS=/utf-8
cmake --build basis-build --config Release --target basisu --parallel 4
```

MSVC 19.44.35228 and Windows SDK 10.0.26100.0 were used for this build.
The CRT is linked statically; SSE4.1 and OpenCL are disabled. The tool is invoked
through the engine process service with separate arguments, cancellation and a timeout.
Other editor hosts may set `BENGINE_BASISU_PATH` to their own compatible basisu executable.
