namespace BEngine.UIElements;

[EditorIcon("Icons/Components/UIDocument.png")]
[AddComponentMenu("UI Toolkit/UI Document")]
[DisallowMultipleComponent]
public sealed partial class UIDocument : MonoBehaviour
{
    private static readonly Material DefaultMaterial = Material.GetBuiltIn("BEngine/UIElements/Solid", "Default UIElements Material");
    private VisualElement? _root;
    private VisualTreeAsset? _loadedSource;
    private ulong _sortingLayer = SortingLayer.Ui;
    private Material _material = DefaultMaterial;
    private TextField? _focusedText;
    private int _textCaret { get => _focusedText?.editing.Caret ?? 0; set => _focusedText?.editing.Move(value); }
    private Fix64 _caretResetTime;
    private VisualElement? _hovered;
    private VisualElement? _focused;
    private long _accessibilityRevision;
    private bool _buildingAccessibilitySnapshot;

    [FormerlySerializedAs("sourceAsset")]
    public VisualTreeAsset? visualTreeAsset { get; set; }
    [YamlDotNet.Serialization.YamlIgnore, HideInInspector]
    public string sourceAsset { get => visualTreeAsset?.assetPath ?? string.Empty; set => visualTreeAsset = BAsset.Load<VisualTreeAsset>(value); }
    [YamlDotNet.Serialization.YamlIgnore, HideInInspector]
    public bool isVisible => enabled && !isDestroyed && (GameObjectOrNull?.activeInHierarchy ?? true);
    public event Action<UIDocument>? visualTreeChanged;
    public event Action<AccessibilityEvent>? accessibilityEvent;
    /// <summary>Raised after the mouse hover target changes, including changes to no target.
    /// The target and its ancestor hover states have already been updated.</summary>
    public event Action<VisualElement?>? hoveredElementChanged;
    public int sortingOrder { get; set; }
    public ulong sortingLayer
    {
        get => _sortingLayer;
        set
        {
            SortingLayer.Validate(value);
            if (!SortingLayer.IsUi(value))
                throw new ArgumentOutOfRangeException(nameof(value), "UI documents must use a configured UI sorting layer.");
            _sortingLayer = value;
        }
    }
    public Material material
    {
        get => _material;
        set => _material = value ?? throw new ArgumentNullException(nameof(value));
    }
    public BAsset? atlas { get; set; }
    public bool interactable { get; set; } = true;
    public PanelScaleMode scaleMode { get; set; } = PanelScaleMode.ScaleWithScreenSize;
    public int referenceWidth { get; set; } = 1280;
    public int referenceHeight { get; set; } = 720;
    public VisualElement rootVisualElement => EnsureRoot();

    public override void OnEnable()
    {
        Reload();
    }
    public override void OnDisable()
    {
        CancelInteraction();
        UIAccessibility.RemoveHierarchy(this);
    }
    public override void OnApplicationFocus(bool focus) { if (!focus) CancelInteraction(); }
    public override void OnDestroy()
    {
        UIAccessibility.RemoveHierarchy(this);
        ReleaseVisualTree();
        visualTreeChanged = null;
        accessibilityEvent = null;
        hoveredElementChanged = null;
    }

    public override void Update()
    {
        UIAccessibility.PumpActions(this);
        if (gameObject.scene is { } scene) UIDocumentInputRouter.ForScene(scene).ProcessInput();
        UpdateTextCaret();
    }

    internal void ClearHover() => SetHovered(null);
    internal void ReleaseVisualTree()
    {
        CancelInteraction();
        DetachAccessibilityRoot();
        if (_root is not null) _root.runtimeDocument = null;
        _root = null;
    }

    internal bool HitTest(Vector2 point, int width, int height)
    {
        using var list = BuildInputList(width, height);
        var picked = list.Pick(point);
        return picked is not null && (!ReferenceEquals(picked, _root) || picked.style.backgroundColor is { A: > 0 });
    }

    public void Reload()
    {
        CancelInteraction();
        DetachAccessibilityRoot();
        if (_root is not null) _root.runtimeDocument = null;
        _loadedSource = visualTreeAsset;
        if (visualTreeAsset is null or { isDestroyed: true })
        {
            _root = CreateFallbackRoot(); _root.runtimeDocument = this; AttachAccessibilityRoot();
            visualTreeChanged?.Invoke(this);
            PublishAccessibility(AccessibilityEventKind.HierarchyChanged, _root);
            return;
        }

        try
        {
            _root = visualTreeAsset.Instantiate();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Debug.LogError($"Could not load UI document '{sourceAsset}': " +
                           $"{exception.GetType().FullName}: {exception}");
            _root = CreateFallbackRoot();
        }
        _root.runtimeDocument = this;
        AttachAccessibilityRoot();
        visualTreeChanged?.Invoke(this);
        PublishAccessibility(AccessibilityEventKind.HierarchyChanged, _root);
    }

    public void SetVisualTree(VisualElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (root.parent is not null || root.runtimeDocument is { } owner && !ReferenceEquals(owner, this))
            throw new InvalidOperationException("A UI document requires its own detached root.");
        CancelInteraction();
        DetachAccessibilityRoot();
        if (_root is not null) _root.runtimeDocument = null;
        _root = root; _root.runtimeDocument = this; AttachAccessibilityRoot();
        _loadedSource = visualTreeAsset;
        visualTreeChanged?.Invoke(this);
        PublishAccessibility(AccessibilityEventKind.HierarchyChanged, _root);
    }

    public AccessibilitySnapshot GetAccessibilitySnapshot(int viewportWidth, int viewportHeight)
    {
        var root = EnsureRoot();
        AccessibilitySnapshot snapshot;
        _buildingAccessibilitySnapshot = true;
        try
        {
            snapshot = AccessibilityTreeBuilder.Build(
                root, BuildRenderList(viewportWidth, viewportHeight), _accessibilityRevision);
        }
        finally { _buildingAccessibilitySnapshot = false; }
        UIAccessibility.PublishHierarchy(this, snapshot);
        return snapshot;
    }

    public void Announce(
        string message,
        AccessibilityLiveRegion priority = AccessibilityLiveRegion.Polite)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (priority == AccessibilityLiveRegion.Off) priority = AccessibilityLiveRegion.Polite;
        PublishAccessibility(AccessibilityEventKind.Announcement, null, message.Trim(), priority);
    }

    public bool PerformAccessibilityAction(int nodeId, AccessibilityAction action)
    {
        var element = EnsureRoot().DescendantsAndSelf()
            .FirstOrDefault(candidate => candidate.accessibilityId == nodeId);
        if (element is null || !IsAvailable(element)) return false;
        switch (action)
        {
            case AccessibilityAction.Focus:
                SetFocused(element);
                FocusText(element as TextField);
                return true;
            case AccessibilityAction.Activate when element is Button button:
                button.Click();
                return true;
            case AccessibilityAction.Activate when element is Toggle { isReadOnly: false } toggle:
                toggle.value = toggle is RadioButton || !toggle.value;
                return true;
            case AccessibilityAction.Increment when element is Slider { isReadOnly: false } slider:
                slider.value = Math.Clamp(slider.value + (slider.highValue - slider.lowValue) / 100,
                    slider.lowValue, slider.highValue);
                return true;
            case AccessibilityAction.Decrement when element is Slider { isReadOnly: false } slider:
                slider.value = Math.Clamp(slider.value - (slider.highValue - slider.lowValue) / 100,
                    slider.lowValue, slider.highValue);
                return true;
            default:
                return false;
        }
    }

    private void FocusFromPointer(VisualElement? control, Vector2 position, UIRenderListBuilder.RendererCommandLease renderList, int viewportWidth, int viewportHeight, bool extendSelection = false)
    {
        SetFocused(control);
        FocusText(control as TextField);
        if (control is TextField textField && renderList.TryGetRect(textField, out var fieldRect))
        {
            var anchor = textField.editing.Anchor;
            var scale = ResolveScale(viewportWidth, viewportHeight);
            var fontSize = UIElementLayoutEngine.EffectiveFontSize(textField) * scale;
            var labelWidth = string.IsNullOrEmpty(textField.label) ? Fix64.Zero : fieldRect.Width * (Fix64)0.4;
            var localX = position.x - fieldRect.X - labelWidth - UIRenderListBuilder.TextInset(textField, fontSize);
            if (localX >= 0 || extendSelection)
            {
                localX = Fix64.Max(0, localX);
                var text = textField.value;
                var start = 0; var end = text.Length;
                if (textField.multiline)
                {
                    var lineHeight = UIRenderListBuilder.TextLineHeight(textField, fontSize);
                    var line = Math.Max(0, (int)((position.y - fieldRect.Y - 2 * scale) / lineHeight));
                    for (var i = 0; i < line; i++) { var next = text.IndexOf('\n', start); if (next < 0) break; start = next + 1; }
                    end = text.IndexOf('\n', start); if (end < 0) end = text.Length;
                }
                var lineText = text[start..end];
                var boundaries = System.Globalization.StringInfo.ParseCombiningCharacters(lineText).Append(lineText.Length).ToArray();
                Fix64? bestDistance = null;
                foreach (var boundary in boundaries)
                {
                    var display = textField.isPasswordField ? new string(textField.maskCharacter, boundary) : lineText[..boundary];
                    var advance = UIRenderListBuilder.MeasureTextAdvance(display, fontSize, textField);
                    var distance = Fix64.Abs(advance - localX);
                    if (bestDistance is { } best && distance >= best) continue;
                    bestDistance = distance;
                    if (extendSelection) textField.editing.Select(anchor, start + boundary);
                    else textField.editing.Move(start + boundary);
                }
                ResetTextCaret();
            }
        }
    }

    public void CancelInteraction()
    {
        SetHovered(null);
        CancelPointers();
        SetFocused(null);
        FocusText(null);
    }

    public bool DispatchTextInput(string text)
    {
        if (!interactable || !isVisible || _focusedText is not { isReadOnly: false } field || !IsAvailable(field)) return false;
        if (text is { Length: 0 }) return false;
        return DispatchPrintableTextInput(field, text);
    }

    // Keep the filtering closure off the per-frame path when no editable field
    // has focus or no text was entered. The editing/filtering semantics are the
    // same as for the original public entry point.
    private bool DispatchPrintableTextInput(TextField field, string text)
    {
        var printable = new string(text.Where(character => !char.IsControl(character) ||
            (field.multiline && character == '\n')).ToArray());
        if (printable.Length == 0) return false;
        var edit = field.editing;
        edit.ReplaceSelection(printable, field.maxLength);
        field.value = edit.Text;
        ResetTextCaret();
        return true;
    }

    public bool DispatchKeyDown(KeyCode key, bool shift = false, bool control = false)
    {
        if (!interactable || !isVisible) return false;
        if (key == KeyCode.Tab)
        {
            var controls = EnsureRoot().DescendantsAndSelf().Where(element =>
                    (element.focusable || element is Button or Toggle or Slider or TextField) && IsAvailable(element))
                .Select((element, order) => (element, order))
                .OrderBy(item => item.element.tabIndex < 0 ? int.MaxValue : item.element.tabIndex)
                .ThenBy(item => item.order)
                .Select(item => item.element)
                .ToList();
            if (controls.Count == 0) return false;
            var index = _focused is null ? (shift ? 0 : -1) : controls.IndexOf(_focused);
            var next = controls[(index + (shift ? -1 : 1) + controls.Count) % controls.Count];
            SetFocused(next);
            FocusText(next as TextField);
            return true;
        }
        if (_focused is null || !IsAvailable(_focused)) return false;
        if (key == KeyCode.Escape) { CancelInteraction(); return true; }
        if (_focusedText is { } field)
        {
            var edit = field.editing;
            if (control && key is KeyCode.A or KeyCode.C or KeyCode.X or KeyCode.V or KeyCode.Z or KeyCode.Y)
            {
                switch (key)
                {
                    case KeyCode.A: edit.SelectAll(); break;
                    case KeyCode.C: if (!field.isPasswordField) Input.TrySetClipboard(edit.SelectedText); break;
                    case KeyCode.X:
                        if (field.isReadOnly) break;
                        if (!field.isPasswordField && Input.TrySetClipboard(edit.SelectedText)) edit.ReplaceSelection(string.Empty);
                        break;
                    case KeyCode.V:
                        if (field.isReadOnly) break;
                        if (Input.TryGetClipboard(out var clipboard))
                            edit.ReplaceSelection(new string(clipboard.Replace("\r\n", "\n").Where(ch => !char.IsControl(ch) || field.multiline && ch == '\n').ToArray()), field.maxLength);
                        break;
                    case KeyCode.Z: if (!field.isReadOnly) { if (shift) edit.Redo(); else edit.Undo(); } break;
                    case KeyCode.Y: if (!field.isReadOnly) edit.Redo(); break;
                }
                field.value = edit.Text;
                ResetTextCaret();
                return true;
            }
            if (key is KeyCode.Backspace or KeyCode.Delete)
            {
                if (field.isReadOnly) return true;
                edit.Delete(key == KeyCode.Backspace, control); field.value = edit.Text; ResetTextCaret(); return true;
            }
            if (key is KeyCode.LeftArrow or KeyCode.RightArrow or KeyCode.Home or KeyCode.End or KeyCode.UpArrow or KeyCode.DownArrow)
            {
                if (key is KeyCode.LeftArrow or KeyCode.RightArrow) edit.MoveHorizontal(key == KeyCode.LeftArrow, shift, control);
                else if (key is KeyCode.Home or KeyCode.End) edit.MoveLine(key == KeyCode.End, shift, control);
                else edit.MoveVertical(key == KeyCode.DownArrow, shift);
                ResetTextCaret(); return true;
            }
            if (key == KeyCode.Return)
            {
                if (field.multiline) DispatchTextInput("\n");
                else { SetFocused(null); FocusText(null); }
                return true;
            }
        }
        if (key is KeyCode.Space or KeyCode.Return)
        {
            if (_focused is Button button) { button.Click(); return true; }
            if (_focused is Toggle { isReadOnly: false } toggle) { toggle.value = toggle is RadioButton || !toggle.value; return true; }
        }
        if (_focused is Slider { isReadOnly: false } slider && key is KeyCode.LeftArrow or KeyCode.RightArrow)
        {
            var step = (slider.highValue - slider.lowValue) / 100;
            slider.value = Math.Clamp(slider.value + (key == KeyCode.LeftArrow ? -step : step), slider.lowValue, slider.highValue);
            return true;
        }
        return false;
    }

    private bool IsAvailable(VisualElement element)
    {
        for (var current = element; current is not null; current = current.parent)
        {
            if (!current.enabledSelf || !current.visible || current.style.display == DisplayStyle.None) return false;
            if (ReferenceEquals(current, _root)) return true;
        }
        return false;
    }

    private static VisualElement? FindControl(VisualElement? picked)
    {
        for (var current = picked; current is not null; current = current.parent)
            if (current.focusable || current is Button or Toggle or Slider or TextField) return current;
        return null;
    }

    private void SetHovered(VisualElement? element)
    {
        if (ReferenceEquals(_hovered, element)) return;
        for (var current = _hovered; current is not null; current = current.parent)
            current.SetInteractionState(isHovered: false);
        _hovered = element;
        for (var current = element; current is not null; current = current.parent)
            current.SetInteractionState(isHovered: true);
        hoveredElementChanged?.Invoke(element);
    }

    private void SetFocused(VisualElement? element)
    {
        if (ReferenceEquals(_focused, element)) return;
        _focused?.SetInteractionState(isFocused: false);
        _focused = element;
        _focused?.SetInteractionState(isFocused: true);
        PublishAccessibility(AccessibilityEventKind.FocusChanged, element);
    }

    private void AttachAccessibilityRoot()
    {
        if (_root is null) return;
        _root.changed += OnAccessibilityNodeChanged;
        _root.hierarchyChanged += OnAccessibilityHierarchyChanged;
        _accessibilityRevision++;
    }

    private void DetachAccessibilityRoot()
    {
        if (_root is null) return;
        _root.changed -= OnAccessibilityNodeChanged;
        _root.hierarchyChanged -= OnAccessibilityHierarchyChanged;
    }

    private void OnAccessibilityNodeChanged(VisualElement element)
    {
        if (_buildingAccessibilitySnapshot) return;
        _accessibilityRevision++;
        PublishAccessibility(AccessibilityEventKind.NodeChanged, element);
        if (element.accessibilityLiveRegion != AccessibilityLiveRegion.Off)
        {
            var message = AccessibilityTreeBuilder.ResolveAnnouncement(element);
            if (message.Length > 0)
                PublishAccessibility(AccessibilityEventKind.Announcement, element,
                    message, element.accessibilityLiveRegion);
        }
    }

    private void OnAccessibilityHierarchyChanged(VisualElement element)
    {
        if (_buildingAccessibilitySnapshot) return;
        _accessibilityRevision++;
        PublishAccessibility(AccessibilityEventKind.HierarchyChanged, element);
    }

    private void PublishAccessibility(
        AccessibilityEventKind kind,
        VisualElement? element,
        string message = "",
        AccessibilityLiveRegion liveRegion = AccessibilityLiveRegion.Off)
    {
        // Do not construct a record or dispatch into the platform adapter for
        // ordinary desktop frames when accessibility is not in use. The next
        // explicit GetAccessibilitySnapshot still observes the current tree.
        if (!UIAccessibility.liveUpdatesEnabled && accessibilityEvent is null) return;
        // Preserve every event for managed observers and custom bridges. Only
        // the default platform bridge may reject events its adapter would drop.
        if (accessibilityEvent is null && !UIAccessibility.ShouldPublish(this, kind, element?.accessibilityId)) return;
        var value = new AccessibilityEvent(this, kind, element?.accessibilityId, message, liveRegion);
        accessibilityEvent?.Invoke(value);
        UIAccessibility.Publish(value);
    }

    public bool DispatchScroll(Vector2 position, float delta, int viewportWidth, int viewportHeight)
    {
        if (!interactable || !isVisible) return false;
        using var list = BuildInputList(viewportWidth, viewportHeight);
        var scale = ResolveScale(viewportWidth, viewportHeight);
        for (var target = list.Pick(position); target is not null; target = target.parent)
        {
            if (target is not ScrollView scroll) continue;
            var maximum = list.GetMaximumScrollOffset(scroll, scale);
            var next = Math.Clamp(scroll.scrollOffset + delta, 0, maximum);
            if (next == scroll.scrollOffset) continue;
            scroll.scrollOffset = next;
            return true;
        }
        return false;
    }

    private UIRenderCommandList BuildRenderList(int width, int height) =>
        UIRenderListBuilder.Build(EnsureRoot(), width, height, ResolveScale(width, height));

    private UIRenderListBuilder.RendererCommandLease BuildInputList(int width, int height) =>
        UIRenderListBuilder.BuildForRenderer(EnsureRoot(), width, height, ResolveScale(width, height));

    private void FocusText(TextField? field)
    {
        if (ReferenceEquals(_focusedText, field)) { if (field is not null) ResetTextCaret(); return; }
        _focusedText?.SetTextEditingState(null, 0, false);
        if (_focusedText is not null) Input.textInputRect = null;
        if (_focusedText is not null) { Input.textInputRequest = null; Input.SetComposition(string.Empty); }
        _focusedText?.SetInteractionState(isFocused: false);
        _focusedText = field;
        _focusedText?.SetInteractionState(isFocused: true);
        _textCaret = field?.value.Length ?? 0;
        ResetTextCaret();
    }

    private void ResetTextCaret() { _caretResetTime = Time.unscaledTime; UpdateTextCaret(); }

    private void UpdateTextCaret()
    {
        if (_focusedText is not { } field) return;
        if (!interactable || !isVisible || !IsAvailable(field))
        {
            field.SetTextEditingState(null, 0, false);
            Input.textInputRect = null;
            Input.textInputRequest = null;
            return;
        }
        var visible = (int)((Time.unscaledTime - _caretResetTime) * 2) % 2 == 0;
        var edit = field.editing;
        if (field.isReadOnly)
        {
            Input.textInputRequest = null;
            field.SetTextEditingState(field.value, edit.Caret, false, edit.Anchor, edit.Caret);
            return;
        }
        var sessionId = unchecked(((long)GetInstanceID() << 32) | (uint)System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(field));
        if (Input.TryTakeTextEdit(out var nativeEdit) && nativeEdit is { } changed && changed.Id == sessionId)
        {
            if (changed.Text != edit.Text) { edit.SelectAll(); edit.ReplaceSelection(changed.Text); field.value = edit.Text; }
            edit.Select(changed.Anchor, changed.Caret);
        }
        Input.textInputRequest = new(sessionId, field.value, edit.Anchor, edit.Caret, field.isPasswordField, field.multiline);
        var composition = Input.compositionString;
        var display = composition.Length == 0 ? field.value : field.value[..edit.SelectionStart] + composition + field.value[edit.SelectionEnd..];
        field.SetTextEditingState(display, composition.Length == 0 ? edit.Caret : edit.SelectionStart + composition.Length,
            visible, edit.Anchor, edit.Caret);
    }

    private static int PreviousTextBoundary(string text, int index) =>
        TextElementUtility.PreviousBoundary(text, index);

    private static int NextTextBoundary(string text, int index) =>
        TextElementUtility.NextBoundary(text, index);

    private static void UpdateSlider(Slider slider, Vector2 position, UIRenderListBuilder.RendererCommandLease list)
    {
        if (!list.TryGetRect(slider, out var rect)) return;
        var labelWidth = string.IsNullOrWhiteSpace(slider.label) ? Fix64.Zero : rect.Width * (Fix64)0.4;
        var ratio = Math.Clamp((float)((position.x - rect.X - labelWidth) /
            Fix64.Max(1, rect.Width - labelWidth)), 0, 1);
        slider.value = slider.lowValue + ratio * (slider.highValue - slider.lowValue);
    }

    public Fix64 ResolveScale(int width, int height)
    {
        if (scaleMode == PanelScaleMode.ConstantPixelSize) return Fix64.One;
        var widthScale = (Fix64)Math.Max(1, width) / Math.Max(1, referenceWidth);
        var heightScale = (Fix64)Math.Max(1, height) / Math.Max(1, referenceHeight);
        if (scaleMode == PanelScaleMode.ScaleToFit) return Fix64.Min(widthScale, heightScale);
        return (widthScale + heightScale) / 2;
    }

    private VisualElement EnsureRoot()
    {
        if (_root is null || !ReferenceEquals(_loadedSource, visualTreeAsset) || _loadedSource is { isDestroyed: true }) Reload();
        return _root!;
    }

    private static VisualElement CreateFallbackRoot()
    {
        var root = new VisualElement { name = "Root" };
        root.style.flexGrow = 1;
        return root;
    }

}
