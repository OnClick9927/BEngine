using BEngine.Physics3D;

namespace BEngine.Terrain;

[DisallowMultipleComponent]
[AddComponentMenu("Terrain/Terrain")]
public sealed class Terrain : Behaviour
{
    private string? _loadedPath;
    private DateTime _loadedWriteTime;
    private TerrainData? _loadedData;

    public string terrainDataPath { get; set; } = string.Empty;
    public Color materialColor { get; set; } = new(Fix64.Parse("0.28"), Fix64.Parse("0.48"), Fix64.Parse("0.22"));
    public bool drawHeightmap { get; set; } = true;
    public bool castShadows { get; set; } = true;
    public bool receiveShadows { get; set; } = true;
    [HideInInspector]
    public TerrainData? terrainData { get; set; }

    public Fix64 SampleHeight(Vector3 worldPosition)
    {
        var data = GetTerrainData();
        if (data is null) return transform.position.y;
        var local = transform.InverseTransformPoint(worldPosition);
        var x = data.size.x == 0 ? Fix64.Zero : local.x / data.size.x;
        var z = data.size.z == 0 ? Fix64.Zero : local.z / data.size.z;
        return transform.TransformPoint(new Vector3(local.x, data.GetInterpolatedHeight(x, z), local.z)).y;
    }

    public Vector3 GetPosition() => transform.position;
    public TerrainData? GetTerrainData()
    {
        if (terrainData is not null) return terrainData;
        if (string.IsNullOrWhiteSpace(terrainDataPath)) return null;
        var path = Path.IsPathRooted(terrainDataPath) ? terrainDataPath :
            Path.GetFullPath(Path.Combine(Application.dataPath, terrainDataPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(path)) return null;
        var writeTime = File.GetLastWriteTimeUtc(path);
        if (_loadedData is null || !string.Equals(path, _loadedPath, StringComparison.OrdinalIgnoreCase) || writeTime != _loadedWriteTime)
        {
            _loadedData = TerrainData.Load(path);
            _loadedPath = path;
            _loadedWriteTime = writeTime;
        }
        return _loadedData;
    }
}

[RequireComponent(typeof(Terrain))]
[DisallowMultipleComponent]
[AddComponentMenu("Terrain/Terrain Collider")]
public sealed class TerrainCollider : Collider, ITerrainCollisionSource
{
    private Terrain terrain => GetComponent<Terrain>()!;
    public Vector3 boundsMin => terrain.transform.position;
    public Vector3 boundsMax
    {
        get
        {
            var data = terrain.GetTerrainData();
            return data is null ? terrain.transform.position : terrain.transform.TransformPoint(data.size);
        }
    }

    public bool SampleSurface(Vector3 worldPosition, out Fix64 height, out Vector3 normal)
    {
        var data = terrain.GetTerrainData();
        if (data is null) { height = default; normal = Vector3.up; return false; }
        var local = terrain.transform.InverseTransformPoint(worldPosition);
        if (local.x < 0 || local.z < 0 || local.x > data.size.x || local.z > data.size.z)
        { height = default; normal = Vector3.up; return false; }
        var x = data.size.x == 0 ? Fix64.Zero : local.x / data.size.x;
        var z = data.size.z == 0 ? Fix64.Zero : local.z / data.size.z;
        height = terrain.SampleHeight(worldPosition);
        normal = terrain.transform.rotation * data.GetInterpolatedNormal(x, z);
        return true;
    }
}
