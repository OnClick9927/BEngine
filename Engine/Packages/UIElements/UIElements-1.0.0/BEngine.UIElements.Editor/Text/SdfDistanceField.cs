namespace BEngine.Editor;

internal static class SdfDistanceField
{
    // Separable squared Euclidean transform, evaluated as lower envelopes of parabolas.
    // Algorithm: Felzenszwalb/Huttenlocher, Distance Transforms of Sampled Functions (2012).
    internal static byte[] Generate(byte[] alpha, int width, int height, int range)
    {
        if (width <= 0 || height <= 0 || alpha.Length != checked(width * height) || range <= 0)
            throw new ArgumentException("Invalid distance field dimensions.");
        var inside = Transform(alpha, width, height, true);
        var outside = Transform(alpha, width, height, false);
        var result = new byte[alpha.Length];
        for (var i = 0; i < result.Length; i++)
        {
            var distance = alpha[i] is > 0 and < 255 ? alpha[i] / 255f - .5f :
                alpha[i] >= 128 ? MathF.Sqrt(outside[i]) - .5f : .5f - MathF.Sqrt(inside[i]);
            result[i] = (byte)Math.Clamp((int)MathF.Round(255 * (.5f + distance / (2 * range))), 0, 255);
        }
        return result;
    }
    private static float[] Transform(byte[] alpha, int width, int height, bool foreground)
    {
        var values = new float[alpha.Length];
        for (var i = 0; i < alpha.Length; i++) values[i] = (alpha[i] >= 128) == foreground ? 0 : 1e12f;
        var n = Math.Max(width, height); var f = new float[n]; var d = new float[n]; var v = new int[n]; var z = new float[n + 1];
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++) f[y] = values[y * width + x];
            Line(f, d, v, z, height);
            for (var y = 0; y < height; y++) values[y * width + x] = d[y];
        }
        for (var y = 0; y < height; y++)
        {
            Array.Copy(values, y * width, f, 0, width); Line(f, d, v, z, width); Array.Copy(d, 0, values, y * width, width);
        }
        return values;
    }
    private static void Line(float[] f, float[] d, int[] v, float[] z, int count)
    {
        var k = 0; v[0] = 0; z[0] = float.NegativeInfinity; z[1] = float.PositiveInfinity;
        for (var q = 1; q < count; q++)
        {
            float crossing;
            do
            {
                var p = v[k]; crossing = (float)(((f[q] + (double)q * q) - (f[p] + (double)p * p)) / (2 * (q - p)));
                if (crossing > z[k]) break;
                k--;
            } while (k >= 0);
            k++; v[k] = q; z[k] = crossing; z[k + 1] = float.PositiveInfinity;
        }
        k = 0;
        for (var q = 0; q < count; q++)
        {
            while (z[k + 1] < q) k++;
            var offset = q - v[k]; d[q] = offset * offset + f[v[k]];
        }
    }
}
