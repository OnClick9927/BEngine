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
        var layerValue = Text(commands, "6  Gameplay");
        Text(commands, "Tag");
        Text(commands, "Layer");

        GenericMenuCapture.Reset();
        inspector.Click(Center(tagValue.Rect));
        TestAssert.Require(GenericMenuCapture.IsAdvanced,
            "The GameObject Tag field did not open as an AdvancedDropdown.");
        var tagItems = GenericMenuCapture.Items;
        TestAssert.Require(tagItems.Any(item => item.Path == "Untagged" && item.Enabled) &&
                           tagItems.Any(item => item.Path == "Player" && item.On) &&
                           tagItems.Any(item => item.Path == "Add Tag..." && item.Enabled),
            "The GameObject Tag field is missing configured Tags or its Add Tag management entry.");
        GenericMenuCapture.Invoke("Untagged");
        inspector.Render(new Event(EventType.Repaint));
        TestAssert.Require(gameObject.tag == "Untagged",
            "Selecting an Inspector Tag dropdown item did not update GameObject.tag.");

        commands = inspector.Render(new Event(EventType.Repaint));
        layerValue = Text(commands, "6  Gameplay");
        GenericMenuCapture.Reset();
        inspector.Click(Center(layerValue.Rect));
        TestAssert.Require(GenericMenuCapture.IsAdvanced,
            "The GameObject Layer field did not open as an AdvancedDropdown.");
        var layerItems = GenericMenuCapture.Items;
        TestAssert.Require(layerItems.Any(item => item.Path == "1  Default" && item.Enabled) &&
                           layerItems.Any(item => item.Path == "6  Gameplay" && item.On) &&
                           layerItems.Any(item => item.Path == "7  Enemies" && item.Enabled) &&
                           layerItems.Any(item => item.Path == "Edit Layers..." && item.Enabled),
            "The GameObject Layer field is missing configured Layers or its Edit Layers management entry.");
        GenericMenuCapture.Invoke("1  Default");
        inspector.Render(new Event(EventType.Repaint));
        TestAssert.Require(gameObject.layer == LayerMask.NameToLayer("Default"),
            "Selecting an Inspector Layer dropdown item did not update GameObject.layer.");

        var cameraOwner = new GameObject("Camera Inspector Target");
        cameraOwner.AddComponent<Camera2D>();
        using var cameraInspector = new InspectorHarness(cameraOwner);
        cameraInspector.Render(new Event(EventType.Layout));
        var cameraCommands = cameraInspector.Render(new Event(EventType.Repaint));
        GenericMenuCapture.Reset();
        cameraInspector.Click(Center(Text(cameraCommands, "Everything").Rect));
        TestAssert.Require(!GenericMenuCapture.IsAdvanced &&
                           GenericMenuCapture.PresentationKind == "DropDown" &&
                           GenericMenuCapture.Items.Any(item => item.Path == "Nothing") &&
                           GenericMenuCapture.Items.Any(item => item.Path.StartsWith(
                               "World/", StringComparison.Ordinal)),
            "Camera Culling Mask did not open as a non-searchable native GenericMenu with project Layers.");

        var probeOwner = new GameObject("Layer Mask Inspector Target");
        var probe = probeOwner.AddComponent<LayerMaskInspectorProbe>();
        probe.queryLayerMask = LayerMask.GetMask("Default", "Gameplay");
        using var probeInspector = new InspectorHarness(probeOwner);
        probeInspector.Render(new Event(EventType.Layout));
        var probeCommands = probeInspector.Render(new Event(EventType.Repaint));
        GenericMenuCapture.Reset();
        probeInspector.Click(Center(Text(probeCommands, "2 Layers").Rect));
        TestAssert.Require(!GenericMenuCapture.IsAdvanced &&
                           GenericMenuCapture.PresentationKind == "DropDown" &&
                           GenericMenuCapture.Items.Any(item => item.Path == "Everything") &&
                           GenericMenuCapture.Items.Any(item => item.Path == "Nothing"),
            "A component LayerMask field did not use the native non-searchable GenericMenu.");
        GenericMenuCapture.Invoke("Nothing");
        probeInspector.Render(new Event(EventType.Repaint));
        TestAssert.Require(probe.queryLayerMask == 0,
            "Selecting a native LayerMask menu item did not update the serialized component field.");
    }

    private static GpuCanvasCommand Text(IEnumerable<GpuCanvasCommand> commands, string content) =>
        commands.FirstOrDefault(command => command.Type == GpuCanvasCommandType.Text &&
                                            command.Content == content) is { Type: GpuCanvasCommandType.Text } match
            ? match
            : throw new InvalidOperationException($"Inspector did not draw '{content}'.");

    private static Vector2 Center(GpuCanvasRect rect) =>
        new((Fix64)(rect.X + rect.Width / 2), (Fix64)(rect.Y + rect.Height / 2));

    private sealed class LayerMaskInspectorProbe : Component
    {
        public LayerMaskInspectorProbe() { }

        public ulong queryLayerMask { get; set; }
    }
}
