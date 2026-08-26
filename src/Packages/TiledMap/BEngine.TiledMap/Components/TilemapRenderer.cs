namespace BEngine.TiledMap;

[AddComponentMenu("2D/Tilemap Renderer")]
[DisallowMultipleComponent]
[RequireComponent(typeof(Tilemap))]
public sealed class TilemapRenderer : Renderer2D
{
    private static readonly Material DefaultMaterial = new(Shader.Find("BEngine/TiledMap"))
    {
        name = "Default Tiled Map Material"
    };
    private Material _material = DefaultMaterial;

    public Color color { get; set; } = Color.white;
    public TilemapSortOrder sortOrder { get; set; } = TilemapSortOrder.BottomLeft;
    public bool cullOutsideCamera { get; set; } = true;

    public Material material
    {
        get => _material;
        set => _material = value ?? throw new ArgumentNullException(nameof(value));
    }

    internal RenderBatchKey2D BatchKey(string texture) => new(_material, texture);
}
