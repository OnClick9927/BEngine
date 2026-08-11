using BEngine.Serialization;

namespace BEngine.Terrain;

[CreateAssetMenu(fileName = "New Terrain", menuName = "Terrain/Terrain Data", order = 100)]
public sealed class TerrainData : ScriptableObject
{
    private Fix64[] _heights = new Fix64[33 * 33];

    public int heightmapResolution { get; private set; } = 33;
    public Vector3 size { get; set; } = new(20, 5, 20);
    public int version { get; private set; }

    public void SetHeightmapResolution(int resolution)
    {
        if (resolution < 2 || resolution > 1025)
            throw new ArgumentOutOfRangeException(nameof(resolution), "Resolution must be between 2 and 1025.");
        if (resolution == heightmapResolution) return;
        var previous = _heights;
        var previousResolution = heightmapResolution;
        heightmapResolution = resolution;
        _heights = new Fix64[resolution * resolution];
        for (var y = 0; y < resolution; y++)
        for (var x = 0; x < resolution; x++)
        {
            var oldX = x * (previousResolution - 1) / (resolution - 1);
            var oldY = y * (previousResolution - 1) / (resolution - 1);
            _heights[y * resolution + x] = previous[oldY * previousResolution + oldX];
        }
        version++;
    }

    public Fix64 GetHeight(int x, int y)
    {
        ValidateCoordinates(x, y);
        return _heights[y * heightmapResolution + x] * size.y;
    }

    public Fix64[,] GetHeights(int xBase, int yBase, int width, int height)
    {
        ValidateRegion(xBase, yBase, width, height);
        var result = new Fix64[height, width];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++) result[y, x] = _heights[(yBase + y) * heightmapResolution + xBase + x];
        return result;
    }

    public void SetHeights(int xBase, int yBase, Fix64[,] heights)
    {
        ArgumentNullException.ThrowIfNull(heights);
        var height = heights.GetLength(0);
        var width = heights.GetLength(1);
        ValidateRegion(xBase, yBase, width, height);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            _heights[(yBase + y) * heightmapResolution + xBase + x] = Mathf.Clamp01(heights[y, x]);
        version++;
    }

    public Fix64 GetInterpolatedHeight(Fix64 x, Fix64 y)
    {
        x = Mathf.Clamp01(x) * (heightmapResolution - 1);
        y = Mathf.Clamp01(y) * (heightmapResolution - 1);
        var x0 = Math.Clamp((int)x, 0, heightmapResolution - 1);
        var y0 = Math.Clamp((int)y, 0, heightmapResolution - 1);
        var x1 = Math.Min(x0 + 1, heightmapResolution - 1);
        var y1 = Math.Min(y0 + 1, heightmapResolution - 1);
        var tx = x - x0;
        var ty = y - y0;
        var bottom = Mathf.Lerp(_heights[y0 * heightmapResolution + x0], _heights[y0 * heightmapResolution + x1], tx);
        var top = Mathf.Lerp(_heights[y1 * heightmapResolution + x0], _heights[y1 * heightmapResolution + x1], tx);
        return Mathf.Lerp(bottom, top, ty) * size.y;
    }

    public Vector3 GetInterpolatedNormal(Fix64 x, Fix64 y)
    {
        var step = Fix64.One / (heightmapResolution - 1);
        var left = GetInterpolatedHeight(x - step, y);
        var right = GetInterpolatedHeight(x + step, y);
        var down = GetInterpolatedHeight(x, y - step);
        var up = GetInterpolatedHeight(x, y + step);
        return new Vector3(-(right - left) / (size.x * step * 2), 1,
            -(up - down) / (size.z * step * 2)).normalized;
    }

    public Fix64 GetSteepness(Fix64 x, Fix64 y)
    {
        var normal = GetInterpolatedNormal(x, y);
        var cosine = Mathf.Clamp(normal.y, -Fix64.One, Fix64.One);
        return Fix64.FromDecimal((decimal)(Math.Acos((double)cosine) * 180.0 / Math.PI));
    }

    public void Save(string path) => YamlUtility.Save(ToDocument(), path);

    public static TerrainData Load(string path)
    {
        var document = YamlUtility.Load<TerrainDataDocument>(path);
        if (document.Format != "BEngine.TerrainData" || document.Version != 1)
            throw new InvalidDataException($"Unsupported terrain data '{document.Format}' v{document.Version}.");
        var terrain = CreateInstance<TerrainData>();
        terrain.name = document.Name;
        terrain.size = new Vector3(Fix64.FromRaw(document.SizeX), Fix64.FromRaw(document.SizeY), Fix64.FromRaw(document.SizeZ));
        terrain.heightmapResolution = document.HeightmapResolution;
        if (document.Heights.Count != terrain.heightmapResolution * terrain.heightmapResolution)
            throw new InvalidDataException("Terrain height count does not match its resolution.");
        terrain._heights = [.. document.Heights.Select(Fix64.FromRaw)];
        terrain.version = 1;
        return terrain;
    }

    internal TerrainDataDocument ToDocument() => new()
    {
        Name = name,
        HeightmapResolution = heightmapResolution,
        SizeX = size.x.RawValue,
        SizeY = size.y.RawValue,
        SizeZ = size.z.RawValue,
        Heights = [.. _heights.Select(value => value.RawValue)]
    };

    private void ValidateCoordinates(int x, int y)
    {
        if (x < 0 || y < 0 || x >= heightmapResolution || y >= heightmapResolution)
            throw new ArgumentOutOfRangeException($"Height coordinate ({x}, {y}) is outside the heightmap.");
    }

    private void ValidateRegion(int x, int y, int width, int height)
    {
        if (width < 0 || height < 0 || x < 0 || y < 0 || x + width > heightmapResolution || y + height > heightmapResolution)
            throw new ArgumentOutOfRangeException(nameof(width), "Height region is outside the heightmap.");
    }
}

public sealed class TerrainDataDocument
{
    public string Format { get; set; } = "BEngine.TerrainData";
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "Terrain Data";
    public int HeightmapResolution { get; set; } = 33;
    public long SizeX { get; set; } = 20L << Fix64.FractionalBits;
    public long SizeY { get; set; } = 5L << Fix64.FractionalBits;
    public long SizeZ { get; set; } = 20L << Fix64.FractionalBits;
    public List<long> Heights { get; set; } = [];
}
