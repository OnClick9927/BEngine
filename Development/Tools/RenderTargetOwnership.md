# RenderTexture binding ownership

Graphics owns RenderTexture descriptors, native resource creation/reuse/retirement,
and the logical identity associated with a resource binding. Rendering2D selects
camera targets and viewports; it consumes the public binding API rather than
creating asset-owned targets or assigning RenderTexture.active.

```csharp
using (var binding = RenderTextureScope.Push(device, cameraTarget))
{
    // binding.Width/Height describe the explicitly requested target.
    // A null cameraTarget inherits the caller surface and returns zero dimensions.
    DrawFrame();
}
```

The scope belongs to its render thread. Nested scopes restore native and logical
state in reverse order, including exceptions and skipped viewports. Completed
copies are idempotent; incorrect order/thread is rejected. For an intermediate
RHI target use `PushTransient`: it hides RenderTexture.active until restoration.
Mixing direct native target changes requires restoring them before scope exit.
The asset and device remain single-owner objects, not concurrent mutation APIs.

Effective color/depth/filter configuration, size and device determine reuse.
Equivalent native filtering does not recreate resources. Replacement is created
before retiring the old resource, preserving the previous target on allocation
failure. Release/Resize/destruction detach the asset immediately; native disposal
waits until the last scope unbinds. A retired target is not reported as active.
`Create` is still required after `Release`. Mip generation is not introduced here.

Warm scopes reuse a thread-local frame stack and a device's existing token scope.
There is one owner record per native target; IDisposable-only devices retain their
existing fallback allocation. Completed frames clear all resource references.
No Core/Platform reference to Graphics or a package name is added.

`Test-RenderTargetOwnership.ps1` supports a drawing-only consumer (`-Headless`),
real PortableSceneRenderer nesting, configuration changes, fault injection,
retirement/GC and zero-allocation warm scopes. `-Legacy` reproduces the earlier
descriptor and nested-identity defects against its original installed assemblies.
