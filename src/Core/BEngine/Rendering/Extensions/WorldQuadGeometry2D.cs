namespace BEngine.Rendering;

/// <summary>Prepares individual sprite quads without owning a renderer's live instance collection.</summary>
public static class WorldQuadGeometry2D
{
    /// <summary>
    /// Applies an instance's pose, pivot and opacity before the camera's center alignment.
    /// The caller selects the size scale and retains control of submission and collection iteration.
    /// Camera alignment runs after the signed size is computed and before its absolute value.
    /// </summary>
    public static SceneQuad2D CreateSpriteQuad(
        Vector2 position, Fix64 rotation, Vector2 size, Color color, Fix64 opacity,
        bool worldSpace, Vector2 origin, Fix64 originRotation, Vector2 originScale,
        Vector2 sizeScale, SpriteRenderData2D visual, RenderCamera camera)
    {
        var worldPosition = worldSpace
            ? position
            : origin + Transform.RotateVector(Vector2.Scale(originScale, position), originRotation);
        color = new Color(color.r, color.g, color.b, color.a * opacity);
        rotation = worldSpace ? rotation : originRotation + rotation;
        var offset = new Vector2(
            (Fix64.Half - visual.Pivot.x) * size.x,
            (Fix64.Half - visual.Pivot.y) * size.y);
        worldPosition += Transform.RotateVector(Vector2.Scale(offset, sizeScale), rotation);
        var renderedSize = Vector2.Scale(size, sizeScale);
        return SceneQuad2D.Create(camera.SnapWorldPoint(worldPosition), rotation,
            new Vector2(Fix64.Abs(renderedSize.x), Fix64.Abs(renderedSize.y)), color,
            visual.Texture, visual.Uv, renderedSize.x < 0, renderedSize.y < 0,
            visual.IsTextured);
    }
}
