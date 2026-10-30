# Compact Web font boundary

`WebFont.c` exposes synchronous font metrics, coverage and positioned glyph
rasterization. HarfBuzz remains responsible for shaping. The Player's explicit
`webFontBackend: FreeType` setting selects this backend only for Web; Skia is
the default. Outline, embedded bitmap and COLRv0 fonts are supported. The
content builder rejects admitted COLRv1/SVG fonts before packaging; select Skia
when those formats are required.

`libBEngineWebFont.a` contains the shim and the FreeType, PNG, zlib and Brotli
objects from the existing pinned SkiaSharp.NativeAssets.WebAssembly 4.153.1
Emscripten 3.1.56 `st,simd` archive. No Skia drawing objects are included. The
archive uses the same SIMD, exception and LTO ABI as the Web host. Its managed
P/Invoke library name must be `libBEngineWebFont`, matching the static archive
name used by the .NET WebAssembly P/Invoke table generator.

To regenerate, configure the matching installed wasm-tools Emscripten Python,
LLVM, Binaryen, Node and cache environment, then run `BuildWebFont.ps1` with
the FreeType 2.14.3 public source headers, the pinned Skia archive, the tool
directory and a fresh intermediate directory. The script fails on compiler or
archive errors and reports the resulting SHA-256. The shim is authored here;
upstream dependency notices are preserved in `FreeType.LICENSE.txt` and are
included in the combined Player payload.
