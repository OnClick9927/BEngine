# Rendering Hardware Interface

## Dependency direction

The rendering boundary is intentionally one-way:

```text
BEngine.UIElements
  layout + UIRenderCommandList (fixed-point, backend neutral)
            |
            v
BEngine.Rendering
  UIElementsRenderer + BEngine.Rendering.Rhi
            |
            v
backend provider (OpenGL / Direct3D 12 / Vulkan / WebGPU)
```

`BEngine.UIElements` must not reference `BEngine.Rendering`. A UI tree can therefore be
serialized, laid out, hit-tested and tested without a GPU. `UIElementsRenderer` consumes the
paint list and is the only layer that converts fixed-point rectangles to GPU float vertices.

## RHI contract

`IGraphicsDevice` is the backend-independent entry point. It owns creation of shader programs,
dynamic/static meshes, sampled textures and render targets. It also defines viewport, top-left
scissor, blend, depth and rasterizer state plus draw submission. Every `IGraphicsResource`
records its owning device; providers must reject resources created by another device.

`GraphicsDeviceFactory` accepts `IGraphicsDeviceProvider` implementations at the composition
root. The Player and editor viewport currently register the built-in OpenGL provider around
their host-owned native context. Future providers should live in logical packages whose runtime
source is compiled into `BEngine.dll` and editor integration is compiled into `BEngine.Editor.dll`:

- namespace `BEngine.Rendering.Direct3D12` / package `com.bengine.rendering.direct3d12`
- namespace `BEngine.Rendering.Vulkan` / package `com.bengine.rendering.vulkan`
- namespace `BEngine.Rendering.WebGPU` / package `com.bengine.rendering.webgpu`

Each provider package depends on `com.bengine.rendering: runtime`; editor surface integration,
when required, belongs in its paired `.Editor` namespace inside `BEngine.Editor.dll`. A provider is registered by the host,
not enabled as a mutually-exclusive package. Project rendering settings choose the active
provider after it has reported `GraphicsBackendAvailability.Available`.

## GPU UI pipeline

`UIRenderListBuilder` emits solid rectangle, text and image commands with a top-left-origin clip
rectangle. `UIElementsRenderer` batches these commands by clip and texture, uploads dynamic
vertices, enables alpha blending, disables depth while drawing, and submits through
`IGraphicsDevice`. It selects GLSL, HLSL or WGSL shader source from device capabilities.

Text uses the built-in geometry font and therefore remains GPU geometry without a font runtime.
Images use `IUIRenderResourceResolver`; the default resolver reads common non-interlaced 8-bit
PNG files and square raw RGBA files. Resolved images are cached as `IGraphicsTexture2D` objects.
Missing images draw a visible GPU placeholder and increment `UIRenderStatistics.UnresolvedImages`.
Asset lifecycle code can call `InvalidateTexture` or `ClearTextureCache` after an import.

## Current backend status

OpenGL 3.3 is the implemented provider and is used by both Player and editor Scene/Game views.
The VisualElement pipeline is backend independent and has shader paths for GLSL, HLSL and WGSL.
The 3D scene, lighting, GI, skybox and terrain pipeline still uses the OpenGL implementation;
constructing `EngineRenderer` with another device fails explicitly instead of silently falling
back. Migrating that scene pipeline is a separate backend implementation milestone.

Game state remains fixed-point. Floating-point shader inputs and matrices exist only after the
render boundary and never write back into deterministic simulation state.
