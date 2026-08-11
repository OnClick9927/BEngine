namespace BEngine.Terrain;

public readonly record struct TerrainVertex(Vector3 position, Vector3 normal);

public sealed class TerrainMeshData
{
    public required TerrainVertex[] vertices { get; init; }
    public required int[] indices { get; init; }
}

public static class TerrainMeshGenerator
{
    public static TerrainMeshData Generate(TerrainData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var resolution = data.heightmapResolution;
        var vertices = new TerrainVertex[resolution * resolution];
        for (var z = 0; z < resolution; z++)
        for (var x = 0; x < resolution; x++)
        {
            var u = (Fix64)x / (resolution - 1);
            var v = (Fix64)z / (resolution - 1);
            vertices[z * resolution + x] = new TerrainVertex(
                new Vector3(u * data.size.x, data.GetInterpolatedHeight(u, v), v * data.size.z),
                data.GetInterpolatedNormal(u, v));
        }
        var indices = new int[(resolution - 1) * (resolution - 1) * 6];
        var index = 0;
        for (var z = 0; z < resolution - 1; z++)
        for (var x = 0; x < resolution - 1; x++)
        {
            var a = z * resolution + x;
            var b = a + 1;
            var c = a + resolution;
            var d = c + 1;
            indices[index++] = a; indices[index++] = c; indices[index++] = b;
            indices[index++] = b; indices[index++] = c; indices[index++] = d;
        }
        return new TerrainMeshData { vertices = vertices, indices = indices };
    }
}
