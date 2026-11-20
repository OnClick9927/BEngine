using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;

namespace BEngine.Platform;

[SupportedOSPlatform("browser")]
public sealed partial class WebAccessibilityAdapter : IPlatformAccessibilityAdapter
{
    private readonly Dictionary<long, Func<int, PlatformAccessibilityAction, bool>> _actions = [];

    public bool IsScreenReaderActive => HasAccessibilitySupport();
    public void AttachWindow(nint nativeHandle) { }

    public void UpdateHierarchy(long documentId, PlatformAccessibilitySnapshot snapshot,
        Func<int, PlatformAccessibilityAction, bool> performAction)
    {
        _actions[documentId] = performAction;
        var document = new WebAccessibilityDocument
        {
            DocumentId = documentId,
            Revision = snapshot.Revision,
            Nodes = Gather912(snapshot.Nodes)
        };
        [global::BEngine.SkipLoopGuard]
        global::BEngine.Platform.WebAccessibilityNode[] Gather912(global::System.Collections.Generic.IEnumerable<global::BEngine.Platform.PlatformAccessibilityNode> items912)
        {
            ArgumentNullException.ThrowIfNull(items912, "source");
            global::BEngine.Platform.WebAccessibilityNode Evaluate912(global::BEngine.Platform.PlatformAccessibilityNode node) => new WebAccessibilityNode
            {
                Id = node.Id,
                ParentId = node.ParentId,
                Role = (int)node.Role,
                Name = node.Name,
                Description = node.Description,
                Value = node.Value,
                State = (int)node.State,
                X = node.Bounds.X,
                Y = node.Bounds.Y,
                Width = node.Bounds.Width,
                Height = node.Bounds.Height,
                MinimumValue = node.MinimumValue,
                MaximumValue = node.MaximumValue,
                LiveRegion = (int)node.LiveRegion
            };
            global::System.ReadOnlySpan<global::BEngine.Platform.PlatformAccessibilityNode> values;
            if (items912.GetType() == typeof(global::BEngine.Platform.PlatformAccessibilityNode[]))
                values = (global::BEngine.Platform.PlatformAccessibilityNode[])items912;
            else if (items912.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Platform.PlatformAccessibilityNode>))
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Platform.PlatformAccessibilityNode>)items912);
            else
            {
                return global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Select(items912, Evaluate912));
            }

            if (values.IsEmpty)
                return global::System.Array.Empty<global::BEngine.Platform.WebAccessibilityNode>();
            var result = new global::BEngine.Platform.WebAccessibilityNode[values.Length];
            for (var index = 0; index < values.Length; index++)
                result[index] = Evaluate912(values[index]);
            return result;
        }
        UpdateAccessibility(JsonSerializer.Serialize(document, WebPlatformJsonContext.Default.WebAccessibilityDocument));
    }

    public void RemoveHierarchy(long documentId)
    {
        _actions.Remove(documentId);
        RemoveAccessibility(documentId);
    }

    public void Notify(PlatformAccessibilityEvent accessibilityEvent)
    {
        if (accessibilityEvent.Kind == PlatformAccessibilityEventKind.Announcement &&
            !string.IsNullOrWhiteSpace(accessibilityEvent.Message))
            AnnounceAccessibility(accessibilityEvent.Message, (int)accessibilityEvent.LiveRegion);
    }

    public bool Perform(long documentId, int nodeId, PlatformAccessibilityAction action) =>
        _actions.TryGetValue(documentId, out var callback) && callback(nodeId, action);

    public void Dispose()
    {
        foreach (var documentId in _actions.Keys.ToArray()) RemoveAccessibility(documentId);
        _actions.Clear();
    }

    [JSImport("globalThis.BEngineAccessibility.hasSupport")]
    private static partial bool HasAccessibilitySupport();
    [JSImport("globalThis.BEngineAccessibility.update")]
    private static partial void UpdateAccessibility(string json);
    [JSImport("globalThis.BEngineAccessibility.remove")]
    private static partial void RemoveAccessibility(double documentId);
    [JSImport("globalThis.BEngineAccessibility.announce")]
    private static partial void AnnounceAccessibility(string message, int liveRegion);
}

internal sealed class WebAccessibilityDocument
{
    public long DocumentId { get; set; }
    public long Revision { get; set; }
    public WebAccessibilityNode[] Nodes { get; set; } = [];
}

internal sealed class WebAccessibilityNode
{
    public int Id { get; set; }
    public int? ParentId { get; set; }
    public int Role { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public int State { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float? MinimumValue { get; set; }
    public float? MaximumValue { get; set; }
    public int LiveRegion { get; set; }
}

