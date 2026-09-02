using BEngine.Serialization;
using Matrix3x2 = System.Numerics.Matrix3x2;

namespace BEngine.ExampleTests.SceneRuntimeArchitecture;

internal static class TwoDimensionalRenderingTests
{
    internal static void Run()
    {
        VerifyRuntimeTextureAndMaterial();
        VerifyLineAndMaskSerialization();
    }

    private static void VerifyRuntimeTextureAndMaterial()
    {
        var texture = new Texture(2, 2);
        texture.SetPixels([Color.red, Color.green, Color.blue, Color.white]);
        texture.Apply();
        var png = texture.EncodeToPNG();
        Require(texture.GetPixel(1, 0).Equals(Color.green) && png.AsSpan(0, 8).SequenceEqual(
                    new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "Runtime Texture pixel access or PNG encoding failed.");

        var material = new Material();
        var matrix = Matrix3x2.CreateScale(2, 3) * Matrix3x2.CreateTranslation(4, 5);
        material.SetTexture("_MainTex", texture);
        material.SetMatrix("_Transform2D", matrix);
        material.EnableKeyword("PIXEL_SNAP");
        Require(ReferenceEquals(material.GetTexture("_MainTex"), texture) &&
                material.GetMatrix("_Transform2D") == matrix &&
                material.IsKeywordEnabled("PIXEL_SNAP") && material.SetPass(0),
            "Material Texture, 2D matrix, keyword, or pass APIs did not preserve state.");
    }

    private static void VerifyLineAndMaskSerialization()
    {
        RuntimeTypeCache.Warmup();
        using var source = new Scene("2D render components");
        var lineObject = source.CreateGameObject("Line");
        var line = lineObject.AddComponent<LineRenderer2D>();
        line.SetPositions([new Vector2(0, 0), new Vector2(2, 0), new Vector2(2, 1)]);
        line.startWidth = Fix64.Parse("0.2");
        line.endWidth = Fix64.Parse("0.4");
        var segments = new List<LineSegment2D>();
        line.FillSegments(segments);
        Require(segments.Count == 2 && segments[0].Size.x == 2,
            "LineRenderer2D did not build visible segment geometry.");

        var maskObject = source.CreateGameObject("Mask");
        var mask = maskObject.AddComponent<SpriteMask>();
        mask.size = new Vector2(2, 2);
        Require(mask.Contains(mask.transform.position) &&
                !mask.Contains(mask.transform.position + new Vector2(2, 0)),
            "SpriteMask did not apply its transformed 2D bounds.");

        using var restored = SceneAssetSerialization.Deserialize(SceneAssetSerialization.Serialize(source));
        var restoredLine = restored.Find("Line")?.GetComponent<LineRenderer2D>() ??
                           throw new InvalidOperationException("LineRenderer2D was not restored.");
        Require(restoredLine.positionCount == 3 && restoredLine.GetPosition(2) == new Vector2(2, 1) &&
                ReferenceEquals(restoredLine.material, line.material),
            "LineRenderer2D positions or shared built-in Material were lost during scene serialization.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
