namespace BEngine.TiledMap;

/// <summary>Authoring identity for a prefab painted under a Tilemap. Persists through scene reloads.</summary>
[AddComponentMenu("2D/Tilemap Prefab Instance")]
[DisallowMultipleComponent]
public sealed class TilemapPrefabInstance : Component
{
    public int cellX { get; set; }
    public int cellY { get; set; }
    public PrefabAsset? source { get; set; }
    public TileTransformFlags transformFlags { get; set; }
}
