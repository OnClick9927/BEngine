using BEngine.Editor;

namespace BEngine.ExampleTests.GameObjectLayerTag;

internal static class SceneHandleTests
{
    public static void Run()
    {
        var scene = new Scene("Scene handles");
        try
        {
            var plain = scene.CreateGameObject("Plain");
            plain.transform.position = new Vector2(3, 4);
            TestAssert.Require(SceneHandleUtility.GetHandlePosition(plain) == new Vector2(3, 4),
                "A non-rendered object's handle was not placed at its Transform center.");

            var spriteObject = scene.CreateGameObject("Offset Sprite");
            spriteObject.transform.position = new Vector2(10, 20);
            spriteObject.transform.rotation = 90;
            spriteObject.transform.localScale = new Vector2(2, 3);
            var sprite = spriteObject.AddComponent<SpriteRenderer>();
            sprite.size = new Vector2(4, 2);
            sprite.pivot = Vector2.zero;
            sprite.useSpritePivot = false;
            var actual = SceneHandleUtility.GetHandlePosition(spriteObject);
            var expected = spriteObject.transform.TransformPoint(new Vector2(2, 1));
            TestAssert.Require((actual - expected).sqrMagnitude <= Fix64.Parse("0.00000001"),
                $"Scene handles did not follow the Sprite's visible center after pivot, rotation, and scale. " +
                $"Expected={expected.x},{expected.y}; Actual={actual.x},{actual.y}.");
        }
        finally
        {
            if (scene.isCreated) scene.Dispose();
        }
    }
}
