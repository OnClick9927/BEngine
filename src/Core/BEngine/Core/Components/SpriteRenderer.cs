namespace BEngine;

[AddComponentMenu("Rendering/Sprite Renderer")]
public sealed class SpriteRenderer : Renderer2D
{
    private static readonly Material DefaultMaterial = new(Shader.Find("BEngine/Sprite"))
    {
        name = "Default Sprite Material"
    };

    public Sprite? sprite { get; set; }
    public Vector2 size { get; set; } = Vector2.one;
    public Vector2 pivot { get; set; } = new(Fix64.Half, Fix64.Half);
    [FormerlySerializedAs("useAtlasPivot")]
    public bool useSpritePivot { get; set; } = true;
    public Color color { get; set; } = Color.white;
    public bool flipX { get; set; }
    public bool flipY { get; set; }

    public Material material
    {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    } = DefaultMaterial;

    internal SpriteRenderData2D ResolveSpriteUnchecked() => TextureAtlasResolver.Resolve(sprite);
    internal RenderBatchKey2D BatchKeyUnchecked => BatchKey(ResolveSpriteUnchecked());
    internal RenderBatchKey2D BatchKey(SpriteRenderData2D visual) => new(material, visual.BatchIdentity);
}
