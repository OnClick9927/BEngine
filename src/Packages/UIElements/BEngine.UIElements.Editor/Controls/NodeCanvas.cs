using BEngine.UIElements;

namespace BEngine.Editor.UIElements;

/// <summary>A retained UIElement graph surface. Graph data and Undo remain with its owner.</summary>
public sealed class NodeCanvas : VisualElement
{
    private static readonly UIColor Background = new(31, 31, 34);
    private static readonly UIColor GridColor = new(43, 43, 47);
    private static readonly UIColor SelectionColor = new(61, 156, 237);
    private readonly VisualElement _grid = Layer("grid");
    private readonly VisualElement _connections = Layer("connections");
    private readonly VisualElement _nodes = Layer("nodes");
    private readonly VisualElement _overlay = Layer("overlay");
    private readonly BEngine.UIElements.Label _caption = new() { name = "caption" };
    private readonly Dictionary<string, NodeVisual> _nodeVisuals = new(StringComparer.Ordinal);
    private readonly HashSet<string> _selection = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Vector2> _dragOrigins = new(StringComparer.Ordinal);
    private readonly HashSet<string> _marqueePrevious = new(StringComparer.Ordinal);
    private NodeCanvasNode[] _nodeData = [];
    private NodeCanvasConnection[] _connectionData = [];
    private string? _selectedConnection;
    private Vector2 _viewport = new(1, 1);
    private Vector2 _pan = new(20, 40);
    private Fix64 _zoom = Fix64.One;
    private Vector2 _pointerStart, _pointerLast, _marqueeStart, _marqueeEnd;
    private bool _pointerActive, _panning, _marquee, _dragging;
    private (string Node, string Port, bool Output)? _connectingPort;

    public bool allowEditing { get; set; } = true;
    public event Action<string, string, string, string>? PortConnectionRequested;

    public event Action<Vector2, Fix64>? ViewChanged;
    public event Action<IReadOnlyCollection<string>, string?>? SelectionChanged;
    public event Action? NodeDragStarted;
    public event Action<IReadOnlyDictionary<string, Vector2>>? NodesMoved;
    public event Action? NodeDragEnded;
    public event Action<string>? NodeDoubleClicked;
    public event Action<string?, string?, Vector2>? ContextRequested;
    public event Action<string>? NodeClicked;

    public Vector2 pan => _pan;
    public Fix64 zoom => _zoom;
    public Vector2 viewport => _viewport;
    public IReadOnlyCollection<string> selectedNodes => _selection;
    public string? selectedConnection => _selectedConnection;
    public bool hasFocus { get; private set; }

    public NodeCanvas()
    {
        name = "NodeCanvas";
        style.position = Position.Absolute;
        style.left = 0;
        style.top = 0;
        style.width = 1;
        style.height = 1;
        style.overflow = Overflow.Hidden;
        style.backgroundColor = Background;
        Add(_grid);
        Add(_connections);
        Add(_nodes);
        Add(_overlay);
        _caption.style.position = Position.Absolute;
        _caption.style.left = 8; _caption.style.top = 5;
        _caption.style.right = 8; _caption.style.height = 20;
        _caption.style.color = new UIColor(226, 226, 226);
        _caption.style.fontSize = 13;
        Add(_caption);
        RegisterCallback<PointerDownEvent>(OnPointerDown);
        RegisterCallback<PointerMoveEvent>(OnPointerMove);
        RegisterCallback<PointerUpEvent>(OnPointerUp);
        RegisterCallback<WheelEvent>(OnWheel);
        RegisterCallback<ContextClickEvent>(OnContextClick);
    }

    public void SetViewport(Vector2 size)
    {
        size = new Vector2(Fix64.Max(1, size.x), Fix64.Max(1, size.y));
        if (_viewport == size) return;
        _viewport = size;
        style.width = (float)size.x;
        style.height = (float)size.y;
        Refresh();
    }

    public void SetCaption(string text) => _caption.text = text ?? string.Empty;

    public void CancelInteraction()
    {
        _pointerActive = _panning = _marquee = _dragging = false;
        _connectingPort = null;
        _dragOrigins.Clear();
        DrawMarquee();
    }

    public void SetView(Vector2 pan, Fix64 zoom)
    {
        zoom = Mathf.Clamp(zoom, (Fix64).1, 4);
        if (_pan == pan && _zoom == zoom) return;
        _pan = pan;
        _zoom = zoom;
        Refresh();
        ViewChanged?.Invoke(_pan, _zoom);
    }

    public void SetGraph(IEnumerable<NodeCanvasNode> nodes, IEnumerable<NodeCanvasConnection> connections)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(connections);
        var nextNodes = nodes.ToArray();
        var nextConnections = connections.ToArray();
        if (nextNodes.Select(node => node.Id).Distinct(StringComparer.Ordinal).Count() != nextNodes.Length)
            throw new ArgumentException("Node IDs must be unique.", nameof(nodes));
        if (nextConnections.Select(edge => edge.Id).Distinct(StringComparer.Ordinal).Count() != nextConnections.Length)
            throw new ArgumentException("Connection IDs must be unique.", nameof(connections));
        foreach (var node in nextNodes)
            if (!ValidPorts(node.Inputs) || !ValidPorts(node.Outputs))
                throw new ArgumentException("Port IDs must be non-empty and unique within each node direction.", nameof(nodes));
        if (_nodeData.Length == nextNodes.Length && _nodeData.Zip(nextNodes).All(pair => SameNode(pair.First, pair.Second)) &&
            _connectionData.SequenceEqual(nextConnections)) return;
        _nodeData = nextNodes;
        _connectionData = nextConnections;
        _selection.RemoveWhere(id => nextNodes.All(node => node.Id != id));
        if (_selectedConnection is not null && nextConnections.All(edge => edge.Id != _selectedConnection))
            _selectedConnection = null;
        Refresh();
    }

    private static bool SameNode(NodeCanvasNode a, NodeCanvasNode b) =>
        a.Id == b.Id && a.Title == b.Title && a.Position == b.Position && a.Size == b.Size &&
        a.Color.Equals(b.Color) && a.Detail == b.Detail && a.Progress == b.Progress && a.Selected == b.Selected &&
        a.Inputs.SequenceEqual(b.Inputs) && a.Outputs.SequenceEqual(b.Outputs);

    private static bool ValidPorts(IReadOnlyList<NodeCanvasPort> ports) => ports is not null &&
        ports.All(port => port is not null && !string.IsNullOrWhiteSpace(port.Id)) &&
        ports.Select(port => port.Id).Distinct(StringComparer.Ordinal).Count() == ports.Count;

    public void SetSelection(IEnumerable<string> nodeIds, string? connectionId = null)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);
        var next = nodeIds.Where(id => _nodeData.Any(node => node.Id == id)).ToHashSet(StringComparer.Ordinal);
        if (_selection.SetEquals(next) && _selectedConnection == connectionId) return;
        _selection.Clear();
        _selection.UnionWith(next);
        _selectedConnection = connectionId;
        Refresh();
    }

    public Vector2 ScreenToWorld(Vector2 position) => (position - _pan) / _zoom;
    public Vector2 WorldToScreen(Vector2 position) => position * _zoom + _pan;

    public void FrameAll(Fix64 padding = default)
    {
        FrameNodes(_nodeData, padding);
    }

    public void FrameSelection(Fix64 padding = default)
    {
        var selected = _nodeData.Where(node => _selection.Contains(node.Id)).ToArray();
        FrameNodes(selected.Length > 0 ? selected : _nodeData, padding);
    }

    private void FrameNodes(IReadOnlyCollection<NodeCanvasNode> nodes, Fix64 padding)
    {
        if (nodes.Count == 0) return;
        if (padding <= 0) padding = 32;
        var minX = nodes.Min(node => node.Position.x);
        var minY = nodes.Min(node => node.Position.y);
        var maxX = nodes.Max(node => node.Position.x + node.Size.x);
        var maxY = nodes.Max(node => node.Position.y + node.Size.y);
        var zoom = Mathf.Clamp(Fix64.Min((_viewport.x - padding * 2) / Fix64.Max(1, maxX - minX),
            (_viewport.y - padding * 2) / Fix64.Max(1, maxY - minY)), (Fix64).1, 2);
        var center = new Vector2((minX + maxX) / 2, (minY + maxY) / 2);
        SetView(_viewport / 2 - center * zoom, zoom);
    }

    private void Refresh()
    {
        DrawGrid();
        DrawConnections();
        var ids = _nodeData.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var stale in _nodeVisuals.Keys.Where(id => !ids.Contains(id)).ToArray())
        {
            _nodes.Remove(_nodeVisuals[stale].Root);
            _nodeVisuals.Remove(stale);
        }
        foreach (var node in _nodeData)
        {
            if (!_nodeVisuals.TryGetValue(node.Id, out var view))
            {
                view = new NodeVisual(node.Id);
                _nodeVisuals.Add(node.Id, view);
                _nodes.Add(view.Root);
            }
            var rect = NodeRect(node);
            SetRect(view.Root, rect);
            var inset = Math.Clamp((float)(8 * _zoom), 3f, 8f);
            view.Root.style.borderWidth = Math.Clamp((float)(2 * _zoom), 1f, 2f);
            view.Root.style.backgroundColor = node.Color;
            view.Root.style.borderColor = _selection.Contains(node.Id) || node.Selected
                ? SelectionColor : new UIColor(18, 18, 20);
            view.Title.style.left = inset;
            view.Title.style.right = inset;
            view.Title.style.top = Math.Clamp((float)(3 * _zoom), 1f, 3f);
            view.Title.style.height = Math.Min(22f, Math.Max(10f, (float)rect.height - 2));
            view.Title.style.fontSize = Math.Clamp((float)(13 * _zoom), 8f, 13f);
            view.Title.text = node.Title;
            view.Detail.text = rect.height >= 46 && rect.width >= 140 ? node.Detail : string.Empty;
            view.Detail.visible = view.Detail.text.Length > 0;
            view.Progress.style.width = (float)Fix64.Max(0, (rect.width - 4) * Mathf.Clamp(node.Progress, 0, 1));
            view.Progress.visible = node.Progress > 0;
            DrawPorts(view.Ports, node);
        }
        DrawMarquee();
    }

    private void DrawGrid()
    {
        _grid.Clear();
        var spacing = 25 * _zoom;
        if (spacing < 8) return;
        for (var x = Mathf.Repeat(_pan.x, spacing); x < _viewport.x; x += spacing)
            AddRect(_grid, new Rect(x, 0, 1, _viewport.y), GridColor);
        for (var y = Mathf.Repeat(_pan.y, spacing); y < _viewport.y; y += spacing)
            AddRect(_grid, new Rect(0, y, _viewport.x, 1), GridColor);
    }

    private void DrawConnections()
    {
        _connections.Clear();
        foreach (var edge in _connectionData)
        {
            if (!TryConnection(edge, out var start, out var end)) continue;
            UIColor color = edge.Selected || edge.Id == _selectedConnection ? SelectionColor : edge.Color;
            foreach (var segment in Segments(start, end, edge.SourceId == edge.DestinationId))
            {
                var left = Fix64.Min(segment.Start.x, segment.End.x);
                var top = Fix64.Min(segment.Start.y, segment.End.y);
                var width = Fix64.Max(2, Mathf.Abs(segment.End.x - segment.Start.x) + 2);
                var height = Fix64.Max(2, Mathf.Abs(segment.End.y - segment.Start.y) + 2);
                AddRect(_connections, new Rect(left, top, width, height), color);
            }
            var arrow = new Vector2(end.x - 8, end.y - 3);
            AddRect(_connections, new Rect(arrow.x, arrow.y, 8, 6), color);
        }
    }

    private bool TryConnection(NodeCanvasConnection edge, out Vector2 start, out Vector2 end)
    {
        var source = _nodeData.FirstOrDefault(node => node.Id == edge.SourceId);
        var destination = _nodeData.FirstOrDefault(node => node.Id == edge.DestinationId);
        if (source is null || destination is null) { start = end = default; return false; }
        (start, end) = NodeCanvasGeometry.Connection(NodeRect(source), NodeRect(destination));
        if (edge.SourcePort is not null && !TryGetPortPosition(source.Id, edge.SourcePort, true, out start)) return false;
        if (edge.DestinationPort is not null && !TryGetPortPosition(destination.Id, edge.DestinationPort, false, out end)) return false;
        return true;
    }

    public bool TryGetPortPosition(string nodeId, string portId, bool output, out Vector2 position)
    {
        var node = _nodeData.FirstOrDefault(item => item.Id == nodeId);
        if (node is not null)
        {
            var ports = output ? node.Outputs : node.Inputs;
            for (var index = 0; index < ports.Count; index++)
                if (ports[index].Id == portId)
                {
                    position = WorldToScreen(node.Position + new Vector2(output ? node.Size.x - 6 : 6, 36 + index * 22));
                    return true;
                }
        }
        position = default;
        return false;
    }

    private void DrawPorts(VisualElement parent, NodeCanvasNode node)
    {
        parent.Clear();
        for (var direction = 0; direction < 2; direction++)
        {
            var output = direction == 1;
            var ports = output ? node.Outputs : node.Inputs;
            for (var index = 0; index < ports.Count; index++)
            {
                var port = ports[index];
                var x = (output ? node.Size.x - 10 : 2) * _zoom;
                var y = (32 + index * 22) * _zoom;
                AddRect(parent, new Rect(x, y, 8 * _zoom, 8 * _zoom), port.Color);
                var label = new BEngine.UIElements.Label { text = port.Label };
                label.style.position = Position.Absolute;
                label.style.left = (float)((output ? node.Size.x / 2 : 14) * _zoom);
                label.style.top = (float)((27 + index * 22) * _zoom);
                label.style.width = (float)((node.Size.x / 2 - 16) * _zoom);
                label.style.height = (float)(20 * _zoom);
                label.style.fontSize = Math.Clamp((float)(11 * _zoom), 8, 13);
                label.style.color = port.Color;
                parent.Add(label);
            }
        }
    }

    private (string Node, string Port, bool Output)? HitPort(Vector2 point)
    {
        for (var nodeIndex = _nodeData.Length - 1; nodeIndex >= 0; nodeIndex--)
        {
            var node = _nodeData[nodeIndex];
            for (var direction = 0; direction < 2; direction++)
            {
                var output = direction == 1;
                var ports = output ? node.Outputs : node.Inputs;
                for (var index = 0; index < ports.Count; index++)
                {
                    var center = WorldToScreen(node.Position + new Vector2(output ? node.Size.x - 6 : 6, 36 + index * 22));
                    if ((center - point).sqrMagnitude <= 100) return (node.Id, ports[index].Id, output);
                }
            }
        }
        return null;
    }

    private static IEnumerable<(Vector2 Start, Vector2 End)> Segments(Vector2 start, Vector2 end, bool self)
    {
        if (self)
        {
            var right = start.x + 30;
            var top = Fix64.Min(start.y, end.y) - 38;
            yield return (start, new Vector2(right, start.y));
            yield return (new Vector2(right, start.y), new Vector2(right, top));
            yield return (new Vector2(right, top), new Vector2(end.x, top));
            yield return (new Vector2(end.x, top), end);
            yield break;
        }
        var middle = (start.x + end.x) / 2;
        yield return (start, new Vector2(middle, start.y));
        yield return (new Vector2(middle, start.y), new Vector2(middle, end.y));
        yield return (new Vector2(middle, end.y), end);
    }

    private Rect NodeRect(NodeCanvasNode node)
    {
        var position = WorldToScreen(node.Position);
        return new Rect(position.x, position.y, node.Size.x * _zoom, node.Size.y * _zoom);
    }

    private string? HitNode(Vector2 point)
    {
        for (var index = _nodeData.Length - 1; index >= 0; index--)
            if (NodeRect(_nodeData[index]).Contains(point)) return _nodeData[index].Id;
        return null;
    }

    private string? HitConnection(Vector2 point)
    {
        foreach (var edge in _connectionData.Reverse())
        {
            if (!TryConnection(edge, out var start, out var end)) continue;
            if (Segments(start, end, edge.SourceId == edge.DestinationId)
                .Any(segment => NodeCanvasGeometry.DistanceToSegment(point, segment.Start, segment.End) <= 7))
                return edge.Id;
        }
        return null;
    }

    private void OnPointerDown(PointerDownEvent evt)
    {
        if (evt.button is not (0 or 2)) return;
        hasFocus = true;
        _pointerActive = true;
        _pointerStart = _pointerLast = evt.position;
        _panning = evt.button == 2 || evt.altKey;
        _dragging = false;
        _marquee = false;
        _dragOrigins.Clear();
        _connectingPort = null;
        if (_panning) return;
        _connectingPort = allowEditing ? HitPort(evt.position) : null;
        if (_connectingPort is not null) { DrawMarquee(); return; }
        var node = HitNode(evt.position);
        if (node is not null)
        {
            if (evt.shiftKey || evt.ctrlKey || evt.commandKey)
            {
                if (!_selection.Add(node)) _selection.Remove(node);
            }
            else if (!_selection.Contains(node))
            {
                _selection.Clear();
                _selection.Add(node);
            }
            _selectedConnection = null;
            SelectionChanged?.Invoke(_selection.ToArray(), null);
            NodeClicked?.Invoke(node);
            if (!_pointerActive) return;
            if (evt.clickCount >= 2) NodeDoubleClicked?.Invoke(node);
            foreach (var item in _nodeData.Where(item => allowEditing && _selection.Contains(item.Id)))
                _dragOrigins[item.Id] = item.Position;
        }
        else
        {
            var edge = HitConnection(evt.position);
            _selectedConnection = edge;
            if (!evt.shiftKey && !evt.ctrlKey && !evt.commandKey) _selection.Clear();
            _marquee = edge is null;
            _marqueeStart = _marqueeEnd = evt.position;
            _marqueePrevious.Clear();
            _marqueePrevious.UnionWith(_selection);
            SelectionChanged?.Invoke(_selection.ToArray(), edge);
        }
        Refresh();
    }

    private void OnPointerMove(PointerMoveEvent evt)
    {
        if (!_pointerActive) return;
        if (_connectingPort is not null)
        {
            _pointerLast = evt.position;
            DrawMarquee();
        }
        else if (_panning)
        {
            SetView(_pan + evt.position - _pointerLast, _zoom);
        }
        else if (_marquee)
        {
            _marqueeEnd = evt.position;
            _selection.Clear();
            _selection.UnionWith(_marqueePrevious);
            var rect = MarqueeRect(evt.position);
            foreach (var node in _nodeData)
                if (NodeRect(node).Overlaps(rect)) _selection.Add(node.Id);
            _selectedConnection = null;
            SelectionChanged?.Invoke(_selection.ToArray(), null);
            Refresh();
        }
        else if (allowEditing && _dragOrigins.Count > 0)
        {
            var delta = ScreenToWorld(evt.position) - ScreenToWorld(_pointerStart);
            if (delta != Vector2.zero)
            {
                if (!_dragging) { _dragging = true; NodeDragStarted?.Invoke(); }
                NodesMoved?.Invoke(_dragOrigins.ToDictionary(pair => pair.Key,
                    pair => pair.Value + delta, StringComparer.Ordinal));
            }
        }
        _pointerLast = evt.position;
    }

    private void OnPointerUp(PointerUpEvent evt)
    {
        if (!_pointerActive) return;
        if (allowEditing && _connectingPort is { } source && HitPort(evt.position) is { } destination &&
            source.Output != destination.Output)
        {
            var from = source.Output ? source : destination;
            var to = source.Output ? destination : source;
            PortConnectionRequested?.Invoke(from.Node, from.Port, to.Node, to.Port);
        }
        _connectingPort = null;
        if (_dragging) NodeDragEnded?.Invoke();
        _pointerActive = _panning = _marquee = _dragging = false;
        _dragOrigins.Clear();
        DrawMarquee();
    }

    private void OnWheel(WheelEvent evt)
    {
        var world = ScreenToWorld(evt.position);
        var zoom = Mathf.Clamp(_zoom * (evt.delta.y > 0 ? (Fix64).9 : (Fix64)1.1), (Fix64).35, 2);
        SetView(evt.position - world * zoom, zoom);
    }

    private void OnContextClick(ContextClickEvent evt) =>
        ContextRequested?.Invoke(HitNode(evt.position), HitConnection(evt.position), ScreenToWorld(evt.position));

    private Rect MarqueeRect(Vector2 end) => new(
        Fix64.Min(_marqueeStart.x, end.x), Fix64.Min(_marqueeStart.y, end.y),
        Mathf.Abs(end.x - _marqueeStart.x), Mathf.Abs(end.y - _marqueeStart.y));

    private void DrawMarquee()
    {
        _overlay.Clear();
        if (_connectingPort is { } port && TryGetPortPosition(port.Node, port.Port, port.Output, out var start))
        {
            foreach (var segment in Segments(start, _pointerLast, false))
                AddRect(_overlay, new Rect(Fix64.Min(segment.Start.x, segment.End.x), Fix64.Min(segment.Start.y, segment.End.y),
                    Fix64.Max(2, Mathf.Abs(segment.End.x - segment.Start.x)), Fix64.Max(2, Mathf.Abs(segment.End.y - segment.Start.y))), SelectionColor);
        }
        if (!_marquee) return;
        var rect = MarqueeRect(_marqueeEnd);
        AddRect(_overlay, rect, new UIColor(42, 113, 172, 54), SelectionColor);
    }

    private static VisualElement Layer(string name)
    {
        var layer = new VisualElement { name = name };
        layer.style.position = Position.Absolute;
        layer.style.left = layer.style.top = layer.style.right = layer.style.bottom = 0;
        return layer;
    }

    private static void SetRect(VisualElement element, Rect rect)
    {
        element.style.left = (float)rect.x;
        element.style.top = (float)rect.y;
        element.style.width = (float)Fix64.Max(1, rect.width);
        element.style.height = (float)Fix64.Max(1, rect.height);
    }

    private static VisualElement AddRect(VisualElement parent, Rect rect, UIColor color, UIColor? border = null)
    {
        var element = new VisualElement();
        element.style.position = Position.Absolute;
        SetRect(element, rect);
        element.style.backgroundColor = color;
        if (border is { } value) { element.style.borderColor = value; element.style.borderWidth = 1; }
        parent.Add(element);
        return element;
    }

    private sealed class NodeVisual
    {
        public VisualElement Root { get; }
        public BEngine.UIElements.Label Title { get; }
        public BEngine.UIElements.Label Detail { get; }
        public VisualElement Progress { get; }
        public VisualElement Ports { get; }

        public NodeVisual(string id)
        {
            Root = new VisualElement { name = "node-" + id, userData = id };
            Root.style.position = Position.Absolute;
            Root.style.borderWidth = 2;
            Root.style.overflow = Overflow.Hidden;
            Title = new BEngine.UIElements.Label { name = "title" };
            Title.style.position = Position.Absolute;
            Title.style.left = 8; Title.style.top = 3; Title.style.right = 8; Title.style.height = 22;
            Title.style.fontSize = 13; Title.style.color = new UIColor(245, 245, 245);
            Detail = new BEngine.UIElements.Label { name = "detail" };
            Detail.style.position = Position.Absolute;
            Detail.style.left = 8; Detail.style.bottom = 3; Detail.style.right = 8; Detail.style.height = 18;
            Detail.style.fontSize = 11; Detail.style.color = new UIColor(215, 220, 224);
            Progress = new VisualElement { name = "progress" };
            Progress.style.position = Position.Absolute;
            Progress.style.left = 2; Progress.style.bottom = 1; Progress.style.height = 2;
            Progress.style.backgroundColor = SelectionColor;
            Ports = Layer("ports");
            Root.Add(Title); Root.Add(Detail); Root.Add(Progress); Root.Add(Ports);
        }
    }
}

public static class NodeCanvasGeometry
{
    public static (Vector2 Start, Vector2 End) Connection(Rect source, Rect destination)
    {
        var start = new Vector2(source.x + source.width / 2, source.y + source.height / 2);
        var end = new Vector2(destination.x + destination.width / 2,
            destination.y + destination.height / 2);
        var dx = (double)end.x - (double)start.x;
        var dy = (double)end.y - (double)start.y;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9)
            return (new Vector2(source.xMax - source.width / 4, source.y),
                new Vector2(destination.xMax, destination.y + destination.height / 4));
        static Vector2 Edge(Rect rect, Vector2 center, double x, double y)
        {
            var halfWidth = (double)rect.width / 2;
            var halfHeight = (double)rect.height / 2;
            double offsetX, offsetY;
            if (Math.Abs(x) * halfHeight >= Math.Abs(y) * halfWidth && Math.Abs(x) > 0)
            { offsetX = Math.CopySign(halfWidth, x); offsetY = offsetX * (y / x); }
            else
            { offsetY = Math.CopySign(halfHeight, y); offsetX = offsetY * (x / y); }
            return new Vector2((Fix64)((double)center.x + offsetX),
                (Fix64)((double)center.y + offsetY));
        }
        return (Edge(source, start, dx, dy), Edge(destination, end, -dx, -dy));
    }

    public static double DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        var dx = (double)end.x - (double)start.x;
        var dy = (double)end.y - (double)start.y;
        var x = (double)point.x - (double)start.x;
        var y = (double)point.y - (double)start.y;
        var length = dx * dx + dy * dy;
        var t = length > 1e-18 ? Math.Clamp((x * dx + y * dy) / length, 0, 1) : 0;
        return Math.Sqrt(Math.Pow(x - dx * t, 2) + Math.Pow(y - dy * t, 2));
    }
}
