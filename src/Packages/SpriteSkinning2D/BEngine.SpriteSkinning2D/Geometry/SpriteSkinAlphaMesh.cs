namespace BEngine;

/// <summary>Builds a watertight alpha silhouette including holes and disconnected islands.
/// Equal spans in adjacent rows are merged; no transparent pixels are filled.</summary>
public static class SpriteSkinAlphaMesh
{
    public static (List<SpriteSkinVertex> Vertices, List<int> Triangles) Build(
        int width, int height, ReadOnlySpan<byte> rgba, Vector2 pivot, Fix64 pixelsPerUnit,
        byte alphaThreshold = 1, int boneIndex = 0)
    {
        if (width <= 0 || height <= 0 || (long)width * height > 16_777_216 || rgba.Length != checked(width * height * 4))
            throw new ArgumentException("Alpha mesh requires valid RGBA8 pixels up to 16 million pixels.");
        if (pixelsPerUnit <= 0 || boneIndex < 0 || alphaThreshold == 0) throw new ArgumentOutOfRangeException(nameof(pixelsPerUnit));
        var active = new Dictionary<(int Left, int Right), (int Top, int Bottom)>();
        var rectangles = new List<(int Left, int Right, int Top, int Bottom)>();
        for (var y = 0; y < height; y++)
        {
            var spans = new Dictionary<(int Left, int Right), (int Top, int Bottom)>();
            for (var x = 0; x < width;)
            {
                if (rgba[(y * width + x) * 4 + 3] < alphaThreshold) { x++; continue; }
                var left = x++;
                while (x < width && rgba[(y * width + x) * 4 + 3] >= alphaThreshold) x++;
                var key = (left, x);
                spans[key] = active.Remove(key, out var previous) ? (previous.Top, y + 1) : (y, y + 1);
            }
            foreach (var entry in active) rectangles.Add((entry.Key.Left, entry.Key.Right, entry.Value.Top, entry.Value.Bottom));
            if (rectangles.Count > 32767) throw new InvalidDataException("Alpha outline is too complex; reduce image resolution or noise.");
            active = spans;
        }
        foreach (var entry in active) rectangles.Add((entry.Key.Left, entry.Key.Right, entry.Value.Top, entry.Value.Bottom));
        if (rectangles.Count == 0) throw new InvalidDataException("Texture has no pixels above the alpha threshold.");
        var vertices = new List<SpriteSkinVertex>(); var triangles = new List<int>();
        var indices = new Dictionary<(int X, int Y), int>();
        // Split rectangle edges at all shared corners to prevent T junction cracks during deformation.
        var horizontal = new Dictionary<int, SortedSet<int>>(); var vertical = new Dictionary<int, SortedSet<int>>();
        foreach (var rect in rectangles)
        {
            Corner(rect.Left, rect.Top); Corner(rect.Right, rect.Top);
            Corner(rect.Left, rect.Bottom); Corner(rect.Right, rect.Bottom);
        }
        foreach (var rect in rectangles)
        {
            var outline = new List<(int X, int Y)>();
            outline.AddRange(vertical[rect.Left].Where(y => y >= rect.Top && y < rect.Bottom).Select(y => (rect.Left, y)));
            outline.AddRange(horizontal[rect.Bottom].Where(x => x >= rect.Left && x < rect.Right).Select(x => (x, rect.Bottom)));
            outline.AddRange(vertical[rect.Right].Reverse().Where(y => y > rect.Top && y <= rect.Bottom).Select(y => (rect.Right, y)));
            outline.AddRange(horizontal[rect.Top].Reverse().Where(x => x > rect.Left && x <= rect.Right).Select(x => (x, rect.Top)));
            var center = Add((Fix64)(rect.Left + rect.Right) / 2, (Fix64)(rect.Top + rect.Bottom) / 2);
            for (var i = 0; i < outline.Count; i++)
            {
                triangles.Add(center); triangles.Add(Index(outline[i])); triangles.Add(Index(outline[(i + 1) % outline.Count]));
            }
        }
        return (vertices, triangles);

        void Corner(int x, int y)
        {
            if (!horizontal.TryGetValue(y, out var xs)) horizontal[y] = xs = [];
            if (!vertical.TryGetValue(x, out var ys)) vertical[x] = ys = [];
            xs.Add(x); ys.Add(y);
        }
        int Index((int X, int Y) point)
        {
            if (!indices.TryGetValue(point, out var index)) indices[point] = index = Add(point.X, point.Y);
            return index;
        }
        int Add(Fix64 x, Fix64 y)
        {
            if (vertices.Count >= 65535) throw new InvalidDataException("Alpha mesh exceeds 65535 vertices; simplify the texture.");
            vertices.Add(new SpriteSkinVertex
            {
                Position = new((x - pivot.x * width) / pixelsPerUnit, (height - y - pivot.y * height) / pixelsPerUnit),
                Uv = new(x / width, y / height), Weights = [new() { BoneIndex = boneIndex, Weight = Fix64.One }]
            });
            return vertices.Count - 1;
        }
    }
}
