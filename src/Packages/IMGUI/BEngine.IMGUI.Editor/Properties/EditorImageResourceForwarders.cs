// Preserve identities used by extensions compiled before image sources moved to EditorServices.
using System.Runtime.CompilerServices;
using BEngine.Editor.Rendering;

[assembly: TypeForwardedTo(typeof(IGpuCanvasResourceResolver))]
[assembly: TypeForwardedTo(typeof(CanvasResourceUtility))]
[assembly: TypeForwardedTo(typeof(FileGpuCanvasResourceResolver))]
