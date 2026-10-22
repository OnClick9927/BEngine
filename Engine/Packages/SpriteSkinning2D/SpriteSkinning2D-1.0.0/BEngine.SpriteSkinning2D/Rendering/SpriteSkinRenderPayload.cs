namespace BEngine;

internal sealed record SpriteSkinRenderPayload(
    Vector2[] Vertices,
    Vector2[] Uv,
    int[] Triangles,
    Color Color,
    string Texture)
{
    internal SpriteSkinBoneTransform[]? Bones { get; init; }
    internal SpriteSkinWeight[][] Weights { get; init; } = [];
    internal bool AllowCpuFallback { get; init; }
}
