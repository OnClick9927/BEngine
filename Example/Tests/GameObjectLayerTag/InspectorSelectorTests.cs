using BEngine.Editor;
using BEngine.Editor.Rendering;

namespace BEngine.ExampleTests.GameObjectLayerTag;

internal static class InspectorSelectorTests
{
    public static void Run()
    {
        var gameObject = new GameObject("Inspector Target")
        {
            tag = "Player",
            layer = LayerMask.NameToLayer("Gameplay")
        };
        using var inspector = new InspectorHarness(gameObject);
        GenericMenuCapture.Install();

        inspector.Render(new Event(EventType.Layout));
        var commands = inspector.Render(new Event(EventType.Repaint));
        var tagValue = Text(commands, "Player");
        var layerValue = Text(commands, "2^8  Gameplay");
        Text(commands, "Tag");
        Text(commands, "Layer");

        GenericMenuCapture.Reset();
        inspector.Click(Center(tagValue.Rect));
        var tagItems = GenericMenuCapture.Items;
        TestAssert.Require(tagItems.Any(item => item.Path == "Untagged" && item.Enabled) &&
                           tagItems.Any(item => item.Path == "Player" && item.On),
            "The GameObject Tag field is not a dropdown with Untagged and the selected Player tag.");
        GenericMenuCapture.Invoke("Untagged");
        inspector.Render(new Event(EventType.Repaint));
        TestAssert.Require(gameObject.tag == "Untagged",
            "Selecting an Inspector Tag dropdown item did not update GameObject.tag.");

        commands = inspector.Render(new Event(EventType.Repaint));
        layerValue = Text(commands, "2^8  Gameplay");
        GenericMenuCapture.Reset();
        inspector.Click(Center(layerValue.Rect));
        var layerItems = GenericMenuCapture.Items;
        TestAssert.Require(layerItems.Any(item => item.Path == "2^1  Default" && item.Enabled) &&
                           layerItems.Any(item => item.Path == "2^8  Gameplay" && item.On) &&
                           layerItems.Any(item => item.Path == "2^9  Enemies" && item.Enabled),
            "The GameObject Layer field is not a named dropdown backed by the configured project Layers.");
        GenericMenuCapture.Invoke("2^1  Default");
        inspector.Render(new Event(EventType.Repaint));
        TestAssert.Require(gameObject.layer == LayerMask.NameToLayer("Default"),
            "Selecting an Inspector Layer dropdown item did not update GameObject.layer.");
    }

    private static GpuCanvasCommand Text(IEnumerable<GpuCanvasCommand> commands, string content) =>
        commands.FirstOrDefault(command => command.Type == GpuCanvasCommandType.Text &&
                                            command.Content == content) is { Type: GpuCanvasCommandType.Text } match
            ? match
            : throw new InvalidOperationException($"Inspector did not draw '{content}'.");

    private static Vector2 Center(GpuCanvasRect rect) =>
        new((Fix64)(rect.X + rect.Width / 2), (Fix64)(rect.Y + rect.Height / 2));
}
