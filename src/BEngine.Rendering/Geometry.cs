namespace BEngine.Rendering;

internal static class Geometry
{
    public static readonly float[] Cube =
    [
        // back
        -0.5f,-0.5f,-0.5f, 0,0,-1,  0.5f, 0.5f,-0.5f, 0,0,-1,  0.5f,-0.5f,-0.5f, 0,0,-1,
        -0.5f,-0.5f,-0.5f, 0,0,-1, -0.5f, 0.5f,-0.5f, 0,0,-1,  0.5f, 0.5f,-0.5f, 0,0,-1,
        // front
        -0.5f,-0.5f, 0.5f, 0,0,1,   0.5f,-0.5f, 0.5f, 0,0,1,   0.5f, 0.5f, 0.5f, 0,0,1,
        -0.5f,-0.5f, 0.5f, 0,0,1,   0.5f, 0.5f, 0.5f, 0,0,1,  -0.5f, 0.5f, 0.5f, 0,0,1,
        // left
        -0.5f, 0.5f, 0.5f,-1,0,0, -0.5f, 0.5f,-0.5f,-1,0,0, -0.5f,-0.5f,-0.5f,-1,0,0,
        -0.5f,-0.5f,-0.5f,-1,0,0, -0.5f,-0.5f, 0.5f,-1,0,0, -0.5f, 0.5f, 0.5f,-1,0,0,
        // right
         0.5f, 0.5f, 0.5f,1,0,0,  0.5f,-0.5f,-0.5f,1,0,0,  0.5f, 0.5f,-0.5f,1,0,0,
         0.5f,-0.5f,-0.5f,1,0,0,  0.5f, 0.5f, 0.5f,1,0,0,  0.5f,-0.5f, 0.5f,1,0,0,
        // bottom
        -0.5f,-0.5f,-0.5f,0,-1,0,  0.5f,-0.5f,-0.5f,0,-1,0,  0.5f,-0.5f, 0.5f,0,-1,0,
        -0.5f,-0.5f,-0.5f,0,-1,0,  0.5f,-0.5f, 0.5f,0,-1,0, -0.5f,-0.5f, 0.5f,0,-1,0,
        // top
        -0.5f, 0.5f,-0.5f,0,1,0,  0.5f, 0.5f, 0.5f,0,1,0,  0.5f, 0.5f,-0.5f,0,1,0,
        -0.5f, 0.5f,-0.5f,0,1,0, -0.5f, 0.5f, 0.5f,0,1,0,  0.5f, 0.5f, 0.5f,0,1,0
    ];

    public static readonly float[] Plane =
    [
        -0.5f,0,-0.5f,0,1,0,  0.5f,0, 0.5f,0,1,0,  0.5f,0,-0.5f,0,1,0,
        -0.5f,0,-0.5f,0,1,0, -0.5f,0, 0.5f,0,1,0,  0.5f,0, 0.5f,0,1,0
    ];

    public static float[] CreateGrid(int radius)
    {
        var values = new List<float>();
        for (var index = -radius; index <= radius; index++)
        {
            Add(values, index, 0, -radius, 0, 1, 0);
            Add(values, index, 0, radius, 0, 1, 0);
            Add(values, -radius, 0, index, 0, 1, 0);
            Add(values, radius, 0, index, 0, 1, 0);
        }

        return values.ToArray();
    }

    public static float[] CreateCameraGizmo()
    {
        var values = new List<float>();
        var farTopLeft = new System.Numerics.Vector3(-0.42f, 0.25f, 0.75f);
        var farTopRight = new System.Numerics.Vector3(0.42f, 0.25f, 0.75f);
        var farBottomRight = new System.Numerics.Vector3(0.42f, -0.25f, 0.75f);
        var farBottomLeft = new System.Numerics.Vector3(-0.42f, -0.25f, 0.75f);
        var origin = System.Numerics.Vector3.Zero;

        AddLine(values, origin, farTopLeft);
        AddLine(values, origin, farTopRight);
        AddLine(values, origin, farBottomRight);
        AddLine(values, origin, farBottomLeft);
        AddLine(values, farTopLeft, farTopRight);
        AddLine(values, farTopRight, farBottomRight);
        AddLine(values, farBottomRight, farBottomLeft);
        AddLine(values, farBottomLeft, farTopLeft);

        var arrowTip = new System.Numerics.Vector3(0, 0.48f, 0.75f);
        AddLine(values, farTopLeft, arrowTip);
        AddLine(values, arrowTip, farTopRight);
        return values.ToArray();
    }

    private static void AddLine(List<float> target, System.Numerics.Vector3 start,
        System.Numerics.Vector3 end)
    {
        Add(target, start.X, start.Y, start.Z, 0, 1, 0);
        Add(target, end.X, end.Y, end.Z, 0, 1, 0);
    }

    private static void Add(List<float> target, float x, float y, float z, float nx, float ny, float nz)
    {
        target.Add(x);
        target.Add(y);
        target.Add(z);
        target.Add(nx);
        target.Add(ny);
        target.Add(nz);
    }
}
