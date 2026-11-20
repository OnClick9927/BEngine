namespace BEngine.Rendering;

public sealed partial class PortableSceneRenderer
{
    // Internal reference switch for pixel regressions. This never changes sort
    // order, extension batches, texture sampling, masks, or depth state.
    internal bool opaqueSolidBlendingEnabled { get; set; } = true;
    private long _opaqueSolidDraws;

    private static bool HasOpaqueCorners(SceneQuad2D quad) =>
        quad.ColorA.a == Fix64.One && quad.ColorB.a == Fix64.One &&
        quad.ColorC.a == Fix64.One && quad.ColorD.a == Fix64.One;
}
