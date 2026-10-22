using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using BEngine.UIElements;

namespace BEngine.Editor;

/// <summary>
/// Hosts a retained-mode UIElement tree inside an EditorWindow graphics surface.
/// </summary>
public sealed class EditorWindowUIElementContainer : IEditorWindowContent
{
    private readonly EditorWindow _owner;
    private readonly Dictionary<IGraphicsDevice, UIElementsRenderer> _renderers =
        new(ReferenceEqualityComparer.Instance);
    private VisualElement _rootVisualElement;
    private Rect _position;
    private Vector2 _contentSize;
    private bool _stretchToParentSize = true;
    private bool _visible = true;
    private bool _interactable = true;
    private UIColor? _backgroundColor;
    private VisualElement? _hovered;
    private VisualElement? _pressed;
    private VisualElement? _focused;
    private VisualElement? _pointerCaptureFallback;
    private VisualElement? _focusFallback;

    internal EditorWindowUIElementContainer(EditorWindow owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _rootVisualElement = CreateDefaultRoot();
        Subscribe(_rootVisualElement);
    }

    /// <summary>The UIElement tree rendered by this container.</summary>
    public VisualElement rootVisualElement => _rootVisualElement;

    /// <summary>
    /// Local EditorWindow rectangle occupied by the container. Assigning it disables automatic
    /// full-window layout; call <see cref="StretchToParentSize"/> to restore automatic layout.
    /// </summary>
    public Rect position
    {
        get => _position;
        set
        {
            _stretchToParentSize = false;
            SetPosition(value);
        }
    }

    /// <summary>
    /// Logical UI layout resolution. A zero component follows the displayed container size.
    /// </summary>
    public Vector2 contentSize
    {
        get => _contentSize;
        set
        {
            var normalized = new Vector2(Fix64.Max(0, value.x), Fix64.Max(0, value.y));
            if (_contentSize == normalized) return;
            _contentSize = normalized;
            _owner.Repaint();
        }
    }

    public bool stretchToParentSize => _stretchToParentSize;

    public bool visible
    {
        get => _visible;
        set
        {
            if (_visible == value) return;
            _visible = value;
            if (!value) ClearInteractionState();
            _owner.Repaint();
        }
    }

    public bool interactable
    {
        get => _interactable;
        set
        {
            if (_interactable == value) return;
            _interactable = value;
            if (!value) ClearInteractionState();
            _owner.Repaint();
        }
    }

    /// <summary>Optional color drawn behind the visual tree without modifying its styles.</summary>
    public UIColor? backgroundColor
    {
        get => _backgroundColor;
        set
        {
            if (_backgroundColor == value) return;
            _backgroundColor = value;
            _owner.Repaint();
        }
    }

    /// <summary>Replaces the tree hosted by this container.</summary>
    public void SetRootVisualElement(VisualElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (ReferenceEquals(_rootVisualElement, root)) return;
        Unsubscribe(_rootVisualElement);
        ClearInteractionState();
        _rootVisualElement = root;
        Subscribe(_rootVisualElement);
        _owner.Repaint();
    }

    /// <summary>Restores automatic full-window positioning.</summary>
    public void StretchToParentSize()
    {
        if (_stretchToParentSize) return;
        _stretchToParentSize = true;
        _owner.Repaint();
    }

    public void SynchronizeLayout(Fix64 width, Fix64 height)
    {
        if (!_stretchToParentSize) return;
        SetPosition(new Rect(0, 0, Fix64.Max(0, width), Fix64.Max(0, height)));
    }

    public bool TryGetGraphicsViewport(out EditorGraphicsViewport viewport)
    {
        var width = ResolveContentDimension(_contentSize.x, _position.width);
        var height = ResolveContentDimension(_contentSize.y, _position.height);
        viewport = new EditorGraphicsViewport(_position, width, height);
        return _visible && HasVisualContent() && viewport.IsValid;
    }

    public void RenderGraphicsViewport(
        IGraphicsDevice device,
        GraphicsRect viewport,
        int targetWidth,
        int targetHeight)
    {
        if (!_renderers.TryGetValue(device, out var renderer))
        {
            renderer = new UIElementsRenderer(device, UIElementsEditorResourceResolver.Shared);
            _renderers.Add(device, renderer);
        }

        var renderList = UIRenderListBuilder.Build(
            _rootVisualElement, targetWidth, targetHeight);
        IEnumerable<UIRenderCommand> commands = renderList.Commands;
        if (_backgroundColor is { } background)
        {
            var fullRect = new UIElementRect(0, 0, targetWidth, targetHeight);
            commands = renderList.Commands.Prepend(new UIRenderCommand(
                UIRenderCommandType.SolidRect,
                _rootVisualElement,
                fullRect,
                fullRect,
                background));
        }
        renderer.RenderCommands(commands, targetWidth, targetHeight, viewport);
    }

    public void ReleaseGraphicsDevice(IGraphicsDevice device)
    {
        if (_renderers.Remove(device, out var renderer)) renderer.Dispose();
    }

    public void ReleaseGraphicsDevices()
    {
        foreach (var renderer in _renderers.Values) renderer.Dispose();
        _renderers.Clear();
    }

    public void HandleEvent(Event current)
    {
        if (!_visible || !_interactable || !HasVisualContent()) return;
        switch (current.type)
        {
            case EventType.MouseMove:
            case EventType.MouseDrag:
            {
                var target = Pick(current.mousePosition);
                SetHovered(target);
                var recipient = current.type == EventType.MouseDrag && _pressed is not null
                    ? _rootVisualElement.Contains(_pressed) ? _pressed
                        : _pointerCaptureFallback is not null && _rootVisualElement.Contains(_pointerCaptureFallback)
                            ? _pointerCaptureFallback : _rootVisualElement
                    : target;
                if (recipient is not null)
                {
                    recipient.SendEvent(new PointerMoveEvent
                    {
                        position = ToContentPoint(current.mousePosition),
                        delta = current.delta,
                        shiftKey = current.shift, ctrlKey = current.control,
                        altKey = current.alt, commandKey = current.command
                    });
                    if (current.type == EventType.MouseDrag && _pressed is not null) current.Use();
                }
                break;
            }
            case EventType.MouseDown when _position.Contains(current.mousePosition):
            {
                var target = Pick(current.mousePosition);
                SetHovered(target);
                SetFocused(target);
                SetPressed(target);
                _pointerCaptureFallback = FindStableChild(target);
                _focusFallback = _pointerCaptureFallback;
                if (target is not null)
                {
                    var point = ToContentPoint(current.mousePosition);
                    _owner.Focus();
                    target.SendEvent(new PointerDownEvent
                    {
                        position = point,
                        button = current.button,
                        clickCount = current.clickCount,
                        shiftKey = current.shift, ctrlKey = current.control,
                        altKey = current.alt, commandKey = current.command
                    });
                    if (current.button == 0 && target is BEngine.UIElements.Button button) button.Click();
                    current.Use();
                }
                break;
            }
            case EventType.MouseDown:
                SetFocused(null);
                SetPressed(null);
                break;
            case EventType.MouseUp:
                if (_pressed is { } pressed)
                {
                    var recipient = _rootVisualElement.Contains(pressed) ? pressed
                        : _pointerCaptureFallback is not null && _rootVisualElement.Contains(_pointerCaptureFallback)
                            ? _pointerCaptureFallback : _rootVisualElement;
                    recipient.SendEvent(new PointerUpEvent
                    {
                        position = ToContentPoint(current.mousePosition), button = current.button
                    });
                    current.Use();
                }
                SetPressed(null);
                _pointerCaptureFallback = null;
                break;
            case EventType.ScrollWheel when _position.Contains(current.mousePosition):
                if (Pick(current.mousePosition) is { } wheelTarget)
                {
                    wheelTarget.SendEvent(new WheelEvent
                    {
                        position = ToContentPoint(current.mousePosition), delta = current.delta
                    });
                    current.Use();
                }
                break;
            case EventType.ContextClick when _position.Contains(current.mousePosition):
                if (Pick(current.mousePosition) is { } contextTarget)
                {
                    contextTarget.SendEvent(new ContextClickEvent
                    {
                        position = ToContentPoint(current.mousePosition)
                    });
                    current.Use();
                }
                break;
            case EventType.KeyDown when _focused is not null:
                (_rootVisualElement.Contains(_focused) ? _focused
                    : _focusFallback is not null && _rootVisualElement.Contains(_focusFallback)
                        ? _focusFallback : _rootVisualElement)
                    .SendEvent(new KeyDownEvent
                {
                    key = current.keyCode.ToString(), shiftKey = current.shift,
                    ctrlKey = current.control, altKey = current.alt,
                    commandKey = current.command
                });
                break;
            case EventType.MouseLeaveWindow:
                SetHovered(null);
                SetPressed(null);
                break;
        }
    }

    private VisualElement? Pick(Vector2 windowPoint)
    {
        if (!_position.Contains(windowPoint)) return null;
        var size = ResolveContentSize();
        var renderList = UIRenderListBuilder.Build(_rootVisualElement, size.Width, size.Height);
        return renderList.Pick(ToContentPoint(windowPoint));
    }

    private Vector2 ToContentPoint(Vector2 windowPoint)
    {
        var size = ResolveContentSize();
        return new Vector2(
            (windowPoint.x - _position.x) * size.Width / Fix64.Max(1, _position.width),
            (windowPoint.y - _position.y) * size.Height / Fix64.Max(1, _position.height));
    }

    private (int Width, int Height) ResolveContentSize() =>
        (ResolveContentDimension(_contentSize.x, _position.width),
            ResolveContentDimension(_contentSize.y, _position.height));

    private static int ResolveContentDimension(Fix64 requested, Fix64 displayed) =>
        Math.Max(1, (int)Math.Ceiling((double)(requested > 0 ? requested : displayed)));

    private bool HasVisualContent() =>
        _backgroundColor is { A: > 0 } ||
        _rootVisualElement.GetType() != typeof(VisualElement) ||
        _rootVisualElement.Children.Count > 0 ||
        _rootVisualElement.style.backgroundColor is { A: > 0 };

    private void SetPosition(Rect value)
    {
        var normalized = new Rect(value.x, value.y,
            Fix64.Max(0, value.width), Fix64.Max(0, value.height));
        if (_position.Equals(normalized)) return;
        _position = normalized;
        _owner.Repaint();
    }

    private void SetHovered(VisualElement? target)
    {
        if (ReferenceEquals(_hovered, target)) return;
        _hovered?.SetInteractionState(isHovered: false);
        _hovered = target;
        _hovered?.SetInteractionState(isHovered: true);
    }

    private void SetPressed(VisualElement? target)
    {
        if (ReferenceEquals(_pressed, target)) return;
        _pressed?.SetInteractionState(isPressed: false);
        _pressed = target;
        _pressed?.SetInteractionState(isPressed: true);
    }

    private void SetFocused(VisualElement? target)
    {
        if (ReferenceEquals(_focused, target)) return;
        _focused?.SetInteractionState(isFocused: false);
        _focused = target;
        _focused?.SetInteractionState(isFocused: true);
    }

    private void ClearInteractionState()
    {
        SetHovered(null);
        SetPressed(null);
        SetFocused(null);
        _pointerCaptureFallback = null;
        _focusFallback = null;
    }

    private VisualElement? FindStableChild(VisualElement? target)
    {
        if (target is null) return null;
        var child = target;
        while (child.parent is not null && !ReferenceEquals(child.parent, _rootVisualElement))
            child = child.parent;
        return _rootVisualElement.Contains(child) ? child : null;
    }

    private void Subscribe(VisualElement root)
    {
        root.changed += OnVisualTreeChanged;
        root.hierarchyChanged += OnVisualTreeChanged;
    }

    private void Unsubscribe(VisualElement root)
    {
        root.changed -= OnVisualTreeChanged;
        root.hierarchyChanged -= OnVisualTreeChanged;
    }

    private void OnVisualTreeChanged(VisualElement _) => _owner.Repaint();

    private static VisualElement CreateDefaultRoot()
    {
        var root = new VisualElement { name = "Root" };
        root.style.flexGrow = 1;
        return root;
    }
}
