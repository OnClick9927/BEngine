using BEngine.UIElements;
using System.ComponentModel;
using System.Runtime.InteropServices;
using DrawingColor = System.Drawing.Color;
using UiButton = BEngine.UIElements.Button;
using UiImage = BEngine.UIElements.Image;
using UiLabel = BEngine.UIElements.Label;
using UiListView = BEngine.UIElements.ListView;
using UiTreeView = BEngine.UIElements.TreeView;

namespace BEngine.UIElements.Editor;

internal sealed class NativeVisualElement : VisualElement
{
    public NativeVisualElement(Func<Control> factory) => Factory = factory;
    public Func<Control> Factory { get; }
}

internal sealed class WinFormsVisualElementHost : UserControl
{
    private VisualElement? _root;
    private readonly Dictionary<VisualElement, Control> _controls = [];
    private readonly Dictionary<VisualElement, ElementBinding> _bindings = [];
    private readonly Dictionary<VisualElement, IReadOnlyList<VisualElement>> _renderedChildren = [];
    private readonly Dictionary<VisualElement, LayoutState> _layoutStates = [];
    private readonly ToolTip _toolTip = new();
    private readonly int _uiThreadId = Environment.CurrentManagedThreadId;
    private int _reconcileQueued;
    private volatile bool _reconcilePendingBeforeHandle;
    private int _applyingModel;

    public WinFormsVisualElementHost()
    {
        Dock = DockStyle.Fill;
        AutoScroll = false;
        UIElementsTheme.Apply(this);
        HandleCreated += (_, _) =>
        {
            if (!_reconcilePendingBeforeHandle) return;
            _reconcilePendingBeforeHandle = false;
            RunQueuedReconcile();
        };
        HandleDestroyed += (_, _) =>
        {
            if (Volatile.Read(ref _reconcileQueued) != 0) _reconcilePendingBeforeHandle = true;
        };
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public VisualElement? Root
    {
        get => _root;
        set
        {
            if (ReferenceEquals(_root, value)) return;
            var previousRoot = _root;
            DetachRoot();
            _root = value;
            if (_root is not null)
            {
                _root.changed += OnElementChanged;
                _root.hierarchyChanged += OnHierarchyChanged;
            }
            if (previousRoot is not null && value is not null && TryReconcileRoot(previousRoot, value)) return;
            Rebuild();
        }
    }

    public void RefreshTree()
    {
        if (IsDisposed || Disposing) return;
        if (Environment.CurrentManagedThreadId == _uiThreadId) ReconcileCurrentRoot();
        else QueueReconcile();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DetachRoot();
            foreach (var binding in _bindings.Values) binding.DisposeResources();
            _toolTip.Dispose();
        }
        base.Dispose(disposing);
    }

    private void DetachRoot()
    {
        if (_root is null) return;
        _root.changed -= OnElementChanged;
        _root.hierarchyChanged -= OnHierarchyChanged;
    }

    private void OnElementChanged(VisualElement element)
    {
        if (IsDisposed || Disposing) return;
        if (Environment.CurrentManagedThreadId != _uiThreadId)
        {
            QueueReconcile();
            return;
        }
        if (_controls.TryGetValue(element, out var control))
        {
            ApplyElement(element, control, preserveInteraction: false);
            if (IsLayoutContainer(element))
            {
                var ownChildControls = element.Children.Select(child => _controls.GetValueOrDefault(child)).ToArray();
                if (ownChildControls.All(child => child is not null)) LayoutChildren(element, control, ownChildControls);
            }
            if (element.parent is { } parent && _controls.TryGetValue(parent, out var parentControl))
            {
                var childControls = parent.Children.Select(child => _controls.GetValueOrDefault(child)).ToArray();
                if (childControls.All(child => child is not null)) LayoutChildren(parent, parentControl, childControls);
            }
        }
        else QueueReconcile();
    }

    private void OnHierarchyChanged(VisualElement element) => QueueReconcile();

    private void QueueReconcile()
    {
        if (IsDisposed || Disposing || Interlocked.Exchange(ref _reconcileQueued, 1) != 0) return;
        if (Environment.CurrentManagedThreadId == _uiThreadId && !IsHandleCreated)
        {
            RunQueuedReconcile();
            return;
        }
        if (!IsHandleCreated)
        {
            _reconcilePendingBeforeHandle = true;
            return;
        }
        try { BeginInvoke(RunQueuedReconcile); }
        catch (ObjectDisposedException) { Interlocked.Exchange(ref _reconcileQueued, 0); }
        catch (InvalidOperationException)
        {
            if (IsHandleCreated) Interlocked.Exchange(ref _reconcileQueued, 0);
            else _reconcilePendingBeforeHandle = true;
        }
    }

    private void RunQueuedReconcile()
    {
        if (Environment.CurrentManagedThreadId != _uiThreadId)
        {
            _reconcilePendingBeforeHandle = true;
            return;
        }
        Interlocked.Exchange(ref _reconcileQueued, 0);
        if (IsDisposed || Disposing) return;
        ReconcileCurrentRoot();
    }

    private void ReconcileCurrentRoot()
    {
        if (IsDisposed || Disposing) return;
        if (Environment.CurrentManagedThreadId != _uiThreadId)
        {
            QueueReconcile();
            return;
        }
        if (_root is null)
        {
            Rebuild();
            return;
        }
        if (!_controls.TryGetValue(_root, out var rootControl))
        {
            Rebuild();
            return;
        }
        SuspendLayout();
        try
        {
            ReconcileElement(_root, _root, rootControl, root: true);
        }
        finally
        {
            ResumeLayout(true);
        }
    }

    private bool TryReconcileRoot(VisualElement previousRoot, VisualElement nextRoot)
    {
        if (!_controls.TryGetValue(previousRoot, out var control) || !CanReuse(previousRoot, nextRoot)) return false;
        SuspendLayout();
        try
        {
            ReconcileElement(previousRoot, nextRoot, control, root: true);
            if (!ReferenceEquals(control.Parent, this)) Controls.Add(control);
            control.Dock = DockStyle.Fill;
            return true;
        }
        finally
        {
            ResumeLayout(true);
        }
    }

    private void Rebuild()
    {
        if (InvokeRequired)
        {
            BeginInvoke(Rebuild);
            return;
        }
        SuspendLayout();
        var staleControls = Controls.Cast<Control>().ToArray();
        Controls.Clear();
        foreach (var binding in _bindings.Values) binding.DisposeResources();
        foreach (var stale in staleControls) stale.Dispose();
        _controls.Clear();
        _bindings.Clear();
        _renderedChildren.Clear();
        _layoutStates.Clear();
        if (_root is not null)
        {
            var control = CreateControl(_root, root: true);
            control.Dock = DockStyle.Fill;
            Controls.Add(control);
        }
        ResumeLayout(true);
    }

    private Control CreateControl(VisualElement element, bool root = false)
    {
        var binding = new ElementBinding(element);
        Control control = element switch
        {
            NativeVisualElement native => native.Factory(),
            UiTreeView tree => CreateTreeView(tree, binding),
            UiListView list => CreateListView(list, binding),
            TextField field => CreateTextField(field, binding),
            FloatField field => CreateNumericField(field, binding),
            IntegerField field => CreateIntegerField(field, binding),
            Toggle toggle => CreateToggle(toggle, binding),
            Slider slider => CreateSlider(slider, binding),
            DropdownField dropdown => CreateDropdown(dropdown, binding),
            UiButton button => CreateButton(button, binding),
            UiLabel label => CreateLabel(label, binding),
            UiImage image => CreateImage(image),
            ScrollView => CreateFlowContainer(element, scroll: true),
            Toolbar => CreateFlowContainer(element, horizontal: true),
            _ => CreateLayoutContainer(element)
        };
        if (root && control is TableLayoutPanel rootLayout) rootLayout.AutoSize = false;
        binding.Control = control;
        binding.CaptureStyleDefaults();
        _controls[element] = control;
        _bindings[element] = binding;
        ApplyElement(element, control, preserveInteraction: false);

        var childControls = new List<Control>(element.Children.Count);
        if (IsLayoutContainer(element))
        {
            foreach (var child in element.Children) childControls.Add(CreateControl(child));
            LayoutChildren(element, control, childControls);
        }
        _renderedChildren[element] = element.Children.ToArray();
        return control;
    }

    private Control ReconcileElement(VisualElement previous, VisualElement next, Control control, bool root = false)
    {
        var previousChildren = _renderedChildren.GetValueOrDefault(previous) ?? [];
        _renderedChildren.Remove(previous);

        if (!ReferenceEquals(previous, next))
        {
            _controls.Remove(previous);
            _bindings.Remove(previous, out var binding);
            _layoutStates.Remove(previous, out var layoutState);
            binding ??= new ElementBinding(next) { Control = control };
            if (binding.StyleDefaults is null) binding.CaptureStyleDefaults();
            binding.Element = next;
            _controls[next] = control;
            _bindings[next] = binding;
            if (layoutState is not null) _layoutStates[next] = layoutState;
        }
        if (root && control is TableLayoutPanel rootLayout) rootLayout.AutoSize = false;
        ApplyElement(next, control, preserveInteraction: true);

        if (next is NativeVisualElement)
        {
            _renderedChildren[next] = [];
            return control;
        }

        var remaining = previousChildren.ToList();
        var desiredControls = new List<Control>(next.Children.Count);
        for (var index = 0; index < next.Children.Count; index++)
        {
            var child = next.Children[index];
            var candidate = FindReusableChild(remaining, child, index);
            if (candidate is not null && _controls.TryGetValue(candidate, out var childControl))
            {
                remaining.Remove(candidate);
                desiredControls.Add(ReconcileElement(candidate, child, childControl));
            }
            else
            {
                desiredControls.Add(CreateControl(child));
            }
        }

        foreach (var orphan in remaining) DisposeRenderedSubtree(orphan);
        if (IsLayoutContainer(next)) LayoutChildren(next, control, desiredControls);
        _renderedChildren[next] = next.Children.ToArray();
        return control;
    }

    private VisualElement? FindReusableChild(IReadOnlyList<VisualElement> candidates, VisualElement next, int index)
    {
        var reference = candidates.FirstOrDefault(candidate => ReferenceEquals(candidate, next));
        if (reference is not null) return reference;

        if (!string.IsNullOrWhiteSpace(next.name))
        {
            var named = candidates.FirstOrDefault(candidate => candidate.name == next.name && CanReuse(candidate, next));
            if (named is not null) return named;
        }
        if (index < candidates.Count && CanReuse(candidates[index], next)) return candidates[index];

        var semanticKey = GetSemanticKey(next);
        if (semanticKey is not null)
        {
            var semantic = candidates.FirstOrDefault(candidate =>
                CanReuse(candidate, next) && GetSemanticKey(candidate) == semanticKey);
            if (semantic is not null) return semantic;
        }
        return candidates.FirstOrDefault(candidate => CanReuse(candidate, next));
    }

    private static bool CanReuse(VisualElement previous, VisualElement next) =>
        previous.GetType() == next.GetType() &&
        (previous is not NativeVisualElement || ReferenceEquals(previous, next));

    private static bool IsLayoutContainer(VisualElement element) => element switch
    {
        NativeVisualElement => false,
        UiTreeView => false,
        UiListView => false,
        TextField => false,
        FloatField => false,
        IntegerField => false,
        Toggle => false,
        Slider => false,
        DropdownField => false,
        TextElement => false,
        UiImage => false,
        _ => true
    };

    private static string? GetSemanticKey(VisualElement element) => element switch
    {
        TextField field => field.label,
        FloatField field => field.label,
        IntegerField field => field.label,
        Toggle field => field.label,
        Slider field => field.label,
        DropdownField field => field.label,
        UiButton button => button.text,
        UiLabel label => label.text,
        _ => null
    };

    private void DisposeRenderedSubtree(VisualElement element)
    {
        foreach (var child in _renderedChildren.GetValueOrDefault(element) ?? []) DisposeRenderedSubtree(child);
        _renderedChildren.Remove(element);
        if (_bindings.Remove(element, out var binding)) binding.DisposeResources();
        _layoutStates.Remove(element);
        if (!_controls.Remove(element, out var control)) return;
        control.Dispose();
    }

    private static FlowLayoutPanel CreateFlowContainer(VisualElement element, bool horizontal = false, bool scroll = false)
    {
        var isToolbar = element is Toolbar;
        var panel = new FlowLayoutPanel
        {
            AutoScroll = scroll,
            WrapContents = false,
            FlowDirection = horizontal ? FlowDirection.LeftToRight : FlowDirection.TopDown,
            AutoSize = !scroll && element.style.flexGrow <= 0,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
            Padding = isToolbar ? new Padding(3, 0, 3, 0) : Padding.Empty,
            BackColor = isToolbar ? UIElementsTheme.Toolbar : UIElementsTheme.Panel,
            ForeColor = UIElementsTheme.Text,
            Font = UIElementsTheme.Font()
        };
        if (isToolbar) panel.MinimumSize = new Size(0, UIElementsTheme.ToolbarHeight);
        if (scroll || element.style.flexGrow > 0) panel.Dock = DockStyle.Fill;
        return panel;
    }

    private static TableLayoutPanel CreateLayoutContainer(VisualElement element)
    {
        var horizontal = element.style.flexDirection == FlexDirection.Row;
        return new TableLayoutPanel
        {
            ColumnCount = horizontal ? Math.Max(1, element.Children.Count) : 1,
            RowCount = horizontal ? 1 : Math.Max(1, element.Children.Count),
            AutoSize = element.style.flexGrow <= 0,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = element.style.flexGrow > 0 ? DockStyle.Fill : DockStyle.Top,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = UIElementsTheme.Panel,
            ForeColor = UIElementsTheme.Text,
            Font = UIElementsTheme.Font(),
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize
        };
    }

    private void LayoutChildren(VisualElement element, Control container, IReadOnlyList<Control?> childControls)
    {
        if (!IsLayoutContainer(element)) return;
        var controls = childControls.Where(control => control is not null).Cast<Control>().ToArray();
        if (_layoutStates.TryGetValue(element, out var previousLayout) &&
            previousLayout.Matches(element, container, controls)) return;
        if (container is FlowLayoutPanel flow)
        {
            flow.SuspendLayout();
            try
            {
                flow.FlowDirection = element.style.flexDirection == FlexDirection.Row
                    ? FlowDirection.LeftToRight
                    : FlowDirection.TopDown;
                flow.AutoSize = element is not ScrollView && element.style.flexGrow <= 0;
                flow.Dock = element is ScrollView || element.style.flexGrow > 0 ? DockStyle.Fill : DockStyle.Top;
                foreach (Control existing in flow.Controls.Cast<Control>().Where(existing => !controls.Contains(existing)).ToArray())
                    flow.Controls.Remove(existing);
                for (var index = 0; index < controls.Length; index++)
                {
                    var childControl = controls[index];
                    childControl.Anchor = element.style.flexDirection == FlexDirection.Column
                        ? AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
                        : AnchorStyles.Left | AnchorStyles.Top;
                    if (!ReferenceEquals(childControl.Parent, flow)) flow.Controls.Add(childControl);
                    flow.Controls.SetChildIndex(childControl, index);
                }
            }
            finally
            {
                flow.ResumeLayout(true);
            }
            _layoutStates[element] = LayoutState.Capture(element, container, controls);
            return;
        }
        if (container is not TableLayoutPanel table) return;

        table.SuspendLayout();
        try
        {
            foreach (Control existing in table.Controls.Cast<Control>().Where(existing => !controls.Contains(existing)).ToArray())
                table.Controls.Remove(existing);
            table.ColumnStyles.Clear();
            table.RowStyles.Clear();

        var horizontal = element.style.flexDirection == FlexDirection.Row;
        var growTotal = element.Children.Sum(child => Math.Max(0, child.style.flexGrow));
            table.ColumnCount = horizontal ? Math.Max(1, element.Children.Count) : 1;
            table.RowCount = horizontal ? 1 : Math.Max(1, element.Children.Count);
            table.AutoSize = element.style.flexGrow <= 0;
            table.Dock = element.style.flexGrow > 0 ? DockStyle.Fill : DockStyle.Top;
        if (horizontal)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (var index = 0; index < element.Children.Count; index++)
            {
                var child = element.Children[index];
                table.ColumnStyles.Add(ToColumnStyle(child, growTotal));
                    var childControl = controls[index];
                    childControl.Dock = child.style.flexGrow > 0 ? DockStyle.Fill : DockStyle.Left;
                    if (!ReferenceEquals(childControl.Parent, table)) table.Controls.Add(childControl, index, 0);
                    else table.SetCellPosition(childControl, new TableLayoutPanelCellPosition(index, 0));
            }
        }
        else
        {
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var index = 0; index < element.Children.Count; index++)
            {
                var child = element.Children[index];
                table.RowStyles.Add(ToRowStyle(child, growTotal));
                    var childControl = controls[index];
                    childControl.Dock = child.style.flexGrow > 0 ? DockStyle.Fill : DockStyle.Top;
                    if (!ReferenceEquals(childControl.Parent, table)) table.Controls.Add(childControl, 0, index);
                    else table.SetCellPosition(childControl, new TableLayoutPanelCellPosition(0, index));
            }
        }
        }
        finally
        {
            table.ResumeLayout(true);
        }
        _layoutStates[element] = LayoutState.Capture(element, container, controls);
    }

    private static ColumnStyle ToColumnStyle(VisualElement child, float growTotal)
    {
        if (child.style.width > 0) return new ColumnStyle(SizeType.Absolute, child.style.width);
        if (child.style.flexGrow > 0 && growTotal > 0)
            return new ColumnStyle(SizeType.Percent, child.style.flexGrow / growTotal * 100);
        return new ColumnStyle(SizeType.AutoSize);
    }

    private static RowStyle ToRowStyle(VisualElement child, float growTotal)
    {
        if (child.style.height > 0) return new RowStyle(SizeType.Absolute, child.style.height);
        if (child.style.flexGrow > 0 && growTotal > 0)
            return new RowStyle(SizeType.Percent, child.style.flexGrow / growTotal * 100);
        return new RowStyle(SizeType.AutoSize);
    }

    private static Control CreateLabel(UiLabel element, ElementBinding binding)
    {
        var label = new System.Windows.Forms.Label
        {
            Text = element.text,
            AutoSize = true,
            Padding = new Padding(3, 3, 3, 2),
            Margin = Padding.Empty,
            ForeColor = UIElementsTheme.Text,
            Font = UIElementsTheme.Font()
        };
        label.DoubleClick += (_, _) => (binding.Element as UiLabel)?.RaiseDoubleClicked();
        return label;
    }

    private static Control CreateButton(UiButton element, ElementBinding binding)
    {
        var button = UIElementsTheme.Button(element.text);
        button.Click += (_, _) => (binding.Element as UiButton)?.Click();
        return button;
    }

    private Control CreateTextField(TextField field, ElementBinding binding)
    {
        var box = new TextBox
        {
            Text = field.value,
            Multiline = field.multiline,
            ReadOnly = field.isReadOnly,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = field.isReadOnly ? UIElementsTheme.FieldReadOnly : UIElementsTheme.Field,
            ForeColor = UIElementsTheme.Text,
            Font = UIElementsTheme.Font(),
            Dock = DockStyle.Fill,
            MinimumSize = new Size(80, field.multiline ? 64 : UIElementsTheme.ControlHeight),
            ScrollBars = field.multiline ? ScrollBars.Vertical : ScrollBars.None
        };
        if (field.scrollToEnd)
        {
            box.SelectionStart = box.TextLength;
            box.ScrollToCaret();
        }
        box.TextChanged += (_, _) =>
        {
            if (_applyingModel == 0 && binding.Element is TextField current) current.ChangeValueFromView(box.Text);
        };
        box.DoubleClick += (_, _) => (binding.Element as TextField)?.RaiseDoubleClicked();
        BindFieldFocus(box, binding);
        return WrapField(field.label, box, field.multiline);
    }

    private Control CreateNumericField(FloatField field, ElementBinding binding)
    {
        var numeric = CreateNumericUpDown();
        numeric.DecimalPlaces = 4;
        numeric.Increment = 0.1m;
        numeric.Value = ClampDecimal((decimal)field.value, numeric.Minimum, numeric.Maximum);
        numeric.ReadOnly = field.isReadOnly;
        numeric.ValueChanged += (_, _) =>
        {
            if (_applyingModel == 0 && binding.Element is FloatField current)
                current.ChangeValueFromView((float)numeric.Value);
        };
        BindFieldFocus(numeric, binding);
        return WrapField(field.label, numeric);
    }

    private Control CreateIntegerField(IntegerField field, ElementBinding binding)
    {
        var numeric = CreateNumericUpDown();
        numeric.DecimalPlaces = 0;
        numeric.Increment = 1;
        numeric.Value = ClampDecimal(field.value, numeric.Minimum, numeric.Maximum);
        numeric.ReadOnly = field.isReadOnly;
        numeric.ValueChanged += (_, _) =>
        {
            if (_applyingModel == 0 && binding.Element is IntegerField current)
                current.ChangeValueFromView(decimal.ToInt32(numeric.Value));
        };
        BindFieldFocus(numeric, binding);
        return WrapField(field.label, numeric);
    }

    private static NumericUpDown CreateNumericUpDown() => new()
    {
        Minimum = -100000000,
        Maximum = 100000000,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = UIElementsTheme.Field,
        ForeColor = UIElementsTheme.Text,
        Font = UIElementsTheme.Font(),
        Dock = DockStyle.Fill,
        MinimumSize = new Size(60, UIElementsTheme.ControlHeight),
        ThousandsSeparator = false
    };

    private static void BindFieldFocus(Control control, ElementBinding binding)
    {
        control.Enter += (_, _) =>
            control.BackColor = binding.Element switch
            {
                TextField { isReadOnly: true } => UIElementsTheme.FieldReadOnly,
                FloatField { isReadOnly: true } => UIElementsTheme.FieldReadOnly,
                IntegerField { isReadOnly: true } => UIElementsTheme.FieldReadOnly,
                _ => UIElementsTheme.FieldHover
            };
        control.Leave += (_, _) =>
            control.BackColor = binding.Element switch
            {
                TextField { isReadOnly: true } => UIElementsTheme.FieldReadOnly,
                FloatField { isReadOnly: true } => UIElementsTheme.FieldReadOnly,
                IntegerField { isReadOnly: true } => UIElementsTheme.FieldReadOnly,
                _ => UIElementsTheme.Field
            };
    }

    private static decimal ClampDecimal(decimal value, decimal minimum, decimal maximum) =>
        Math.Min(maximum, Math.Max(minimum, value));

    private Control CreateToggle(Toggle toggle, ElementBinding binding)
    {
        var checkBox = new CheckBox
        {
            Text = toggle.label,
            Checked = toggle.value,
            AutoSize = true,
            Padding = new Padding(3, 1, 3, 1),
            Margin = new Padding(1),
            ForeColor = UIElementsTheme.Text,
            BackColor = UIElementsTheme.Panel,
            Font = UIElementsTheme.Font(),
            FlatStyle = FlatStyle.Flat
        };
        checkBox.FlatAppearance.BorderColor = UIElementsTheme.Border;
        checkBox.FlatAppearance.MouseOverBackColor = UIElementsTheme.RowHover;
        checkBox.CheckedChanged += (_, _) =>
        {
            if (_applyingModel == 0 && binding.Element is Toggle current)
                current.ChangeValueFromView(checkBox.Checked);
        };
        return checkBox;
    }

    private Control CreateSlider(Slider slider, ElementBinding binding)
    {
        var track = new TrackBar
        {
            Minimum = 0,
            Maximum = 1000,
            TickStyle = TickStyle.None,
            Dock = DockStyle.Fill,
            Value = ToTrackValue(slider)
        };
        track.ValueChanged += (_, _) =>
        {
            var t = track.Value / 1000f;
            if (_applyingModel == 0 && binding.Element is Slider current)
                current.ChangeValueFromView(current.lowValue + (current.highValue - current.lowValue) * t);
        };
        return WrapField(slider.label, track);
    }

    private static int ToTrackValue(Slider slider)
    {
        var range = slider.highValue - slider.lowValue;
        return range <= float.Epsilon ? 0 : Math.Clamp((int)((slider.value - slider.lowValue) / range * 1000), 0, 1000);
    }

    private Control CreateDropdown(DropdownField field, ElementBinding binding)
    {
        var combo = new ThemedComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = UIElementsTheme.Field,
            ForeColor = UIElementsTheme.Text,
            Font = UIElementsTheme.Font(),
            Dock = DockStyle.Fill,
            MinimumSize = new Size(60, UIElementsTheme.ControlHeight)
        };
        combo.Items.AddRange(field.choices.Cast<object>().ToArray());
        combo.SelectedItem = field.value;
        if (combo.SelectedIndex < 0 && combo.Items.Count > 0) combo.SelectedIndex = 0;
        combo.SelectedIndexChanged += (_, _) =>
        {
            if (_applyingModel == 0 && combo.SelectedItem is string value && binding.Element is DropdownField current)
                current.ChangeValueFromView(value);
        };
        return WrapField(field.label, combo);
    }

    private static TableLayoutPanel WrapField(string label, Control editor, bool expand = false)
    {
        var hasLabel = !string.IsNullOrWhiteSpace(label);
        var row = new TableLayoutPanel
        {
            ColumnCount = hasLabel ? 2 : 1,
            RowCount = 1,
            AutoSize = !expand,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = expand ? DockStyle.Fill : DockStyle.Top,
            Margin = new Padding(2, 1, 2, 1),
            Padding = Padding.Empty,
            BackColor = UIElementsTheme.Panel,
            ForeColor = UIElementsTheme.Text,
            MinimumSize = new Size(180, Math.Max(UIElementsTheme.ControlHeight, editor.MinimumSize.Height))
        };
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        if (!hasLabel)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.Controls.Add(editor, 0, 0);
            return row;
        }
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        var caption = new System.Windows.Forms.Label
        {
            Text = label,
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UIElementsTheme.Text,
            Font = UIElementsTheme.Font(),
            Padding = new Padding(2, 0, 4, 0)
        };
        row.Controls.Add(caption, 0, 0);
        row.Controls.Add(editor, 1, 0);
        return row;
    }

    private static Control CreateImage(UiImage image)
    {
        var picture = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = UIElementsTheme.Field,
            MinimumSize = new Size(80, 80)
        };
        return picture;
    }

    private Control CreateTreeView(UiTreeView element, ElementBinding binding)
    {
        var tree = new ThemedTreeView
        {
            BorderStyle = BorderStyle.None,
            BackColor = UIElementsTheme.Panel,
            ForeColor = UIElementsTheme.Text,
            Font = UIElementsTheme.Font(),
            Dock = DockStyle.Fill,
            HideSelection = false,
            FullRowSelect = true,
            ShowLines = false
        };
        tree.AfterSelect += (_, args) =>
        {
            if (_applyingModel == 0 && binding.Element is UiTreeView current)
                current.SelectFromView(args.Node?.Tag as TreeViewItem);
        };
        tree.NodeMouseDoubleClick += (_, args) =>
        {
            if (args.Node is { Tag: TreeViewItem item } && binding.Element is UiTreeView current)
                current.ChooseFromView(item);
        };
        tree.NodeMouseClick += (_, args) =>
        {
            if (args.Button != MouseButtons.Right || args.Node is not { Tag: TreeViewItem item }) return;
            tree.SelectedNode = args.Node;
            var menu = new ContextMenuBuilder();
            if (binding.Element is UiTreeView current) current.BuildContextMenu(item, menu);
            ShowContextMenu(tree, args.Location, menu);
        };
        return tree;
    }

    private static TreeNode CreateTreeNode(TreeViewItem item)
    {
        var node = new TreeNode(item.Text) { Tag = item, Name = item.Id.ToString() };
        foreach (var child in item.Children ?? []) node.Nodes.Add(CreateTreeNode(child));
        return node;
    }

    private static TreeNode? FindTreeNode(TreeNodeCollection nodes, int id)
    {
        foreach (TreeNode node in nodes)
        {
            if (node.Tag is TreeViewItem item && item.Id == id) return node;
            if (FindTreeNode(node.Nodes, id) is { } descendant) return descendant;
        }
        return null;
    }

    private Control CreateListView(UiListView element, ElementBinding binding)
    {
        var list = new ThemedListBox(element.showAlternatingRowBackgrounds)
        {
            BorderStyle = BorderStyle.None,
            BackColor = UIElementsTheme.Panel,
            ForeColor = UIElementsTheme.Text,
            Font = element.ClassListContains("unity-console-list")
                ? UIElementsTheme.MonoFont()
                : UIElementsTheme.Font(),
            Dock = DockStyle.Fill,
            IntegralHeight = false,
            HorizontalScrollbar = true
        };
        list.SelectedIndexChanged += (_, _) =>
        {
            if (_applyingModel == 0 && binding.Element is UiListView current)
                current.SelectFromView(list.SelectedIndex);
        };
        list.DoubleClick += (_, _) =>
        {
            if (binding.Element is UiListView current) current.ChooseFromView(list.SelectedIndex);
        };
        list.MouseUp += (_, args) =>
        {
            if (args.Button != MouseButtons.Right) return;
            var index = list.IndexFromPoint(args.Location);
            if (index >= 0) list.SelectedIndex = index;
            var item = index >= 0 && index < list.Items.Count ? (list.Items[index] as DisplayItem)?.Value : null;
            var menu = new ContextMenuBuilder();
            if (binding.Element is UiListView current) current.BuildContextMenu(item, menu);
            ShowContextMenu(list, args.Location, menu);
        };
        return list;
    }

    private static void ShowContextMenu(Control owner, Point location, ContextMenuBuilder builder)
    {
        if (builder.Items.Count == 0) return;
        var menu = new ContextMenuStrip();
        UIElementsTheme.ApplyContextMenu(menu);
        foreach (var item in builder.Items)
        {
            if (item.Separator)
            {
                menu.Items.Add(new ToolStripSeparator());
                continue;
            }
            var menuItem = new ToolStripMenuItem(item.Name)
            {
                Enabled = item.Enabled,
                Checked = item.IsChecked
            };
            if (item.Action is not null) menuItem.Click += (_, _) => item.Action();
            menu.Items.Add(menuItem);
        }
        menu.Show(owner, location);
    }

    private void ApplyElement(VisualElement element, Control control, bool preserveInteraction)
    {
        control.Visible = element.visible && element.style.display != DisplayStyle.None;
        control.Enabled = element.enabledInHierarchy;
        control.Name = element.name;
        control.AccessibleDescription = element.tooltip;
        _bindings.TryGetValue(element, out var binding);
        var previousStyle = binding?.AppliedStyle ?? default;
        var defaults = binding?.StyleDefaults;

        var hasMargin = HasSpacing(element.style.marginLeft, element.style.marginTop,
            element.style.marginRight, element.style.marginBottom);
        if (hasMargin)
            control.Margin = new Padding(
                ToInt(element.style.marginLeft), ToInt(element.style.marginTop),
                ToInt(element.style.marginRight), ToInt(element.style.marginBottom));
        else if (previousStyle.Margin && defaults is not null)
            control.Margin = defaults.Margin;

        var hasPadding = HasSpacing(element.style.paddingLeft, element.style.paddingTop,
            element.style.paddingRight, element.style.paddingBottom);
        if (control is ScrollableControl scrollable && hasPadding)
        {
            scrollable.Padding = new Padding(
                ToInt(element.style.paddingLeft), ToInt(element.style.paddingTop),
                ToInt(element.style.paddingRight), ToInt(element.style.paddingBottom));
        }
        else if (control is ScrollableControl defaultScrollable && previousStyle.Padding && defaults is not null)
            defaultScrollable.Padding = defaults.Padding;

        var hasWidth = element.style.width > 0;
        var hasHeight = element.style.height > 0;
        if (hasWidth) control.Width = ToInt(element.style.width);
        else if (previousStyle.Width && defaults is not null) control.Width = defaults.Width;
        if (hasHeight) control.Height = ToInt(element.style.height);
        else if (previousStyle.Height && defaults is not null) control.Height = defaults.Height;

        var hasMinWidth = element.style.minWidth > 0;
        var hasMinHeight = element.style.minHeight > 0;
        if (hasMinWidth || hasMinHeight || previousStyle.MinWidth || previousStyle.MinHeight)
            control.MinimumSize = new Size(
                hasMinWidth ? ToInt(element.style.minWidth) : defaults?.MinimumSize.Width ?? control.MinimumSize.Width,
                hasMinHeight ? ToInt(element.style.minHeight) : defaults?.MinimumSize.Height ?? control.MinimumSize.Height);
        var maxWidth = float.IsPositiveInfinity(element.style.maxWidth) ? 0 : ToInt(element.style.maxWidth);
        var maxHeight = float.IsPositiveInfinity(element.style.maxHeight) ? 0 : ToInt(element.style.maxHeight);
        var hasMaxWidth = !float.IsPositiveInfinity(element.style.maxWidth);
        var hasMaxHeight = !float.IsPositiveInfinity(element.style.maxHeight);
        if (hasMaxWidth || hasMaxHeight || previousStyle.MaxWidth || previousStyle.MaxHeight)
            control.MaximumSize = new Size(
                hasMaxWidth ? maxWidth : defaults?.MaximumSize.Width ?? control.MaximumSize.Width,
                hasMaxHeight ? maxHeight : defaults?.MaximumSize.Height ?? control.MaximumSize.Height);

        var hasBackground = element.style.backgroundColor is not null;
        if (element.style.backgroundColor is { } background) control.BackColor = ToDrawing(background);
        else if (previousStyle.Background && defaults is not null) control.BackColor = defaults.BackColor;
        else if (control.BackColor == SystemColors.Control) control.BackColor = UIElementsTheme.Panel;

        var hasForeground = element.style.color is not null;
        if (element.style.color is { } foreground) control.ForeColor = ToDrawing(foreground);
        else if (previousStyle.Foreground && defaults is not null) control.ForeColor = defaults.ForeColor;
        else control.ForeColor = UIElementsTheme.Text;

        var hasFont = element.style.fontSize > 0;
        if (hasFont)
        {
            var requestedSize = Math.Clamp(element.style.fontSize, 8.5f, 20f);
            if (binding?.AppliedFont is null || Math.Abs(binding.AppliedFont.SizeInPoints - requestedSize) > 0.01f)
            {
                var previousFont = binding?.AppliedFont;
                var appliedFont = UIElementsTheme.Font(requestedSize);
                control.Font = appliedFont;
                if (binding is not null) binding.AppliedFont = appliedFont;
                previousFont?.Dispose();
            }
        }
        else if (previousStyle.Font && defaults is not null)
        {
            control.Font = defaults.Font;
            binding?.AppliedFont?.Dispose();
            if (binding is not null) binding.AppliedFont = null;
        }

        if (binding is not null)
            binding.AppliedStyle = new AppliedStyle(
                hasMargin, hasPadding, hasWidth, hasHeight,
                hasMinWidth, hasMinHeight, hasMaxWidth, hasMaxHeight,
                hasBackground, hasForeground, hasFont);
        _toolTip.SetToolTip(control, string.IsNullOrWhiteSpace(element.tooltip) ? null : element.tooltip);

        _applyingModel++;
        try
        {
            switch (element)
            {
                case UiLabel label when control is System.Windows.Forms.Label nativeLabel:
                    nativeLabel.Text = label.text;
                    break;
                case UiButton button when control is System.Windows.Forms.Button nativeButton:
                    nativeButton.Text = button.text;
                    break;
                case TextField field when FindControl<TextBox>(control) is { } textBox:
                    SyncFieldCaption(control, field.label);
                    SyncTextField(field, textBox);
                    break;
                case FloatField field when FindControl<NumericUpDown>(control) is { } numeric:
                    SyncFieldCaption(control, field.label);
                    numeric.ReadOnly = field.isReadOnly;
                    numeric.Value = ClampDecimal((decimal)field.value, numeric.Minimum, numeric.Maximum);
                    break;
                case IntegerField field when FindControl<NumericUpDown>(control) is { } numeric:
                    SyncFieldCaption(control, field.label);
                    numeric.ReadOnly = field.isReadOnly;
                    numeric.Value = ClampDecimal(field.value, numeric.Minimum, numeric.Maximum);
                    break;
                case Toggle toggle when control is CheckBox checkBox:
                    checkBox.Text = toggle.label;
                    checkBox.Checked = toggle.value;
                    break;
                case Slider slider when FindControl<TrackBar>(control) is { } track:
                    SyncFieldCaption(control, slider.label);
                    track.Value = ToTrackValue(slider);
                    break;
                case DropdownField dropdown when FindControl<ComboBox>(control) is { } combo:
                    SyncFieldCaption(control, dropdown.label);
                    SyncDropdown(dropdown, combo);
                    break;
                case UiTreeView treeElement when control is System.Windows.Forms.TreeView tree:
                    SyncTreeView(treeElement, tree, preserveInteraction);
                    break;
                case UiListView listElement when control is ThemedListBox list:
                    SyncListView(listElement, list, preserveInteraction);
                    break;
                case UiImage image when control is PictureBox picture:
                    SyncImage(image, picture);
                    break;
            }
        }
        finally
        {
            _applyingModel--;
        }
    }

    private static void SyncFieldCaption(Control wrapper, string text)
    {
        if (wrapper is not TableLayoutPanel table) return;
        var caption = table.Controls.OfType<System.Windows.Forms.Label>().FirstOrDefault();
        if (caption is not null) caption.Text = text;
    }

    private static void SyncTextField(TextField field, TextBox box)
    {
        var selectionStart = box.SelectionStart;
        var selectionLength = box.SelectionLength;
        var firstVisibleLine = box.IsHandleCreated
            ? SendMessage(box.Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero).ToInt32()
            : 0;
        box.Multiline = field.multiline;
        box.ReadOnly = field.isReadOnly;
        box.ScrollBars = field.multiline ? ScrollBars.Vertical : ScrollBars.None;
        box.MinimumSize = new Size(80, field.multiline ? 64 : UIElementsTheme.ControlHeight);
        if (box.Text != field.value) box.Text = field.value;
        box.BackColor = field.isReadOnly
            ? UIElementsTheme.FieldReadOnly
            : box.Focused ? UIElementsTheme.FieldHover : UIElementsTheme.Field;

        if (field.scrollToEnd)
        {
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;
            box.ScrollToCaret();
            return;
        }

        box.SelectionStart = Math.Clamp(selectionStart, 0, box.TextLength);
        box.SelectionLength = Math.Clamp(selectionLength, 0, box.TextLength - box.SelectionStart);
        if (!box.IsHandleCreated || !box.Multiline) return;
        var currentFirstLine = SendMessage(box.Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero).ToInt32();
        SendMessage(box.Handle, EmLineScroll, IntPtr.Zero, new IntPtr(firstVisibleLine - currentFirstLine));
    }

    private static void SyncDropdown(DropdownField field, ComboBox combo)
    {
        var choicesChanged = combo.Items.Count != field.choices.Count ||
                             combo.Items.Cast<object>().Select(combo.GetItemText)
                                 .Where((choice, index) => choice != field.choices[index]).Any();
        if (choicesChanged)
        {
            combo.BeginUpdate();
            combo.Items.Clear();
            combo.Items.AddRange(field.choices.Cast<object>().ToArray());
            combo.EndUpdate();
        }
        combo.SelectedItem = field.value;
        if (combo.SelectedIndex < 0 && combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private void SyncImage(UiImage element, PictureBox picture)
    {
        if (!_bindings.TryGetValue(element, out var binding) || binding.ImageSource == element.sourcePath) return;
        var previous = picture.Image;
        picture.Image = null;
        previous?.Dispose();
        binding.ImageSource = element.sourcePath;
        if (!File.Exists(element.sourcePath)) return;
        using var source = System.Drawing.Image.FromFile(element.sourcePath);
        picture.Image = new Bitmap(source);
    }

    private static void SyncTreeView(UiTreeView element, System.Windows.Forms.TreeView tree, bool preserveInteraction)
    {
        var expandedIds = EnumerateTreeNodes(tree.Nodes)
            .Where(node => node.IsExpanded && node.Tag is TreeViewItem)
            .Select(node => ((TreeViewItem)node.Tag!).Id)
            .ToHashSet();
        var selectedId = (tree.SelectedNode?.Tag as TreeViewItem)?.Id;
        var topId = (tree.TopNode?.Tag as TreeViewItem)?.Id;

        tree.BeginUpdate();
        try
        {
            if (TreeStructureMatches(tree.Nodes, element.items))
            {
                UpdateTreeNodes(tree.Nodes, element.items);
            }
            else
            {
                tree.Nodes.Clear();
                foreach (var item in element.items) tree.Nodes.Add(CreateTreeNode(item));
            }

            foreach (var id in expandedIds)
                FindTreeNode(tree.Nodes, id)?.Expand();

            var desiredSelectedId = element.selectedId ?? (preserveInteraction ? selectedId : null);
            tree.SelectedNode = desiredSelectedId is { } selectedNodeId
                ? FindTreeNode(tree.Nodes, selectedNodeId)
                : null;
            if (topId is { } previousTop && FindTreeNode(tree.Nodes, previousTop) is { } topNode)
                tree.TopNode = topNode;
        }
        finally
        {
            tree.EndUpdate();
        }
    }

    private static bool TreeStructureMatches(TreeNodeCollection nodes, IReadOnlyList<TreeViewItem> items)
    {
        if (nodes.Count != items.Count) return false;
        for (var index = 0; index < items.Count; index++)
        {
            if (nodes[index].Tag is not TreeViewItem current || current.Id != items[index].Id) return false;
            if (!TreeStructureMatches(nodes[index].Nodes, items[index].Children ?? [])) return false;
        }
        return true;
    }

    private static void UpdateTreeNodes(TreeNodeCollection nodes, IReadOnlyList<TreeViewItem> items)
    {
        for (var index = 0; index < items.Count; index++)
        {
            nodes[index].Text = items[index].Text;
            nodes[index].Name = items[index].Id.ToString();
            nodes[index].Tag = items[index];
            UpdateTreeNodes(nodes[index].Nodes, items[index].Children ?? []);
        }
    }

    private static IEnumerable<TreeNode> EnumerateTreeNodes(TreeNodeCollection nodes)
    {
        foreach (TreeNode node in nodes)
        {
            yield return node;
            foreach (var child in EnumerateTreeNodes(node.Nodes)) yield return child;
        }
    }

    private static void SyncListView(UiListView element, ThemedListBox list, bool preserveInteraction)
    {
        var selectedValue = (list.SelectedItem as DisplayItem)?.Value;
        var topIndex = list.Items.Count > 0 ? list.TopIndex : -1;
        var desired = element.itemsSource.Cast<object?>()
            .Select(item => new DisplayItem(item, element.makeItemText(item)))
            .ToArray();
        var contentChanged = list.Items.Count != desired.Length ||
                             list.Items.Cast<object>().OfType<DisplayItem>().Where((item, index) => item != desired[index]).Any();
        if (contentChanged)
        {
            list.BeginUpdate();
            list.Items.Clear();
            list.Items.AddRange(desired);
            list.EndUpdate();
        }

        list.AlternatingRows = element.showAlternatingRowBackgrounds;
        var selectedIndex = element.selectedIndex;
        if (selectedIndex < 0 && preserveInteraction && selectedValue is not null)
            selectedIndex = Array.FindIndex(desired, item => Equals(item.Value, selectedValue));
        list.SelectedIndex = Math.Clamp(selectedIndex, -1, list.Items.Count - 1);
        if (topIndex >= 0 && list.Items.Count > 0) list.TopIndex = Math.Clamp(topIndex, 0, list.Items.Count - 1);
    }

    private static T? FindControl<T>(Control root) where T : Control
    {
        if (root is T match) return match;
        foreach (Control child in root.Controls)
        {
            if (FindControl<T>(child) is { } descendant) return descendant;
        }
        return null;
    }

    private static bool HasSpacing(params float[] values) => values.Any(value => Math.Abs(value) > float.Epsilon);
    private static int ToInt(float value) => (int)MathF.Round(Math.Max(0, value));
    private static DrawingColor ToDrawing(UIColor color) => DrawingColor.FromArgb(color.A, color.R, color.G, color.B);
    private sealed record DisplayItem(object? Value, string Text) { public override string ToString() => Text; }
    private sealed class ElementBinding(VisualElement element)
    {
        public VisualElement Element { get; set; } = element;
        public Control Control { get; set; } = null!;
        public string ImageSource { get; set; } = string.Empty;
        public ControlStyleDefaults? StyleDefaults { get; private set; }
        public AppliedStyle AppliedStyle { get; set; }
        public Font? AppliedFont { get; set; }

        public void CaptureStyleDefaults() => StyleDefaults = new ControlStyleDefaults(
            Control.Margin,
            Control is ScrollableControl scrollable ? scrollable.Padding : Padding.Empty,
            Control.Width,
            Control.Height,
            Control.MinimumSize,
            Control.MaximumSize,
            Control.BackColor,
            Control.ForeColor,
            Control.Font);

        public void DisposeResources()
        {
            AppliedFont?.Dispose();
            AppliedFont = null;
            if (Control is not PictureBox picture) return;
            var image = picture.Image;
            picture.Image = null;
            image?.Dispose();
        }
    }

    private sealed record ControlStyleDefaults(
        Padding Margin,
        Padding Padding,
        int Width,
        int Height,
        Size MinimumSize,
        Size MaximumSize,
        DrawingColor BackColor,
        DrawingColor ForeColor,
        Font Font);

    private readonly record struct AppliedStyle(
        bool Margin,
        bool Padding,
        bool Width,
        bool Height,
        bool MinWidth,
        bool MinHeight,
        bool MaxWidth,
        bool MaxHeight,
        bool Background,
        bool Foreground,
        bool Font);

    private sealed class LayoutState
    {
        private readonly FlexDirection _direction;
        private readonly float _flexGrow;
        private readonly bool _scrollView;
        private readonly Control _container;
        private readonly Control[] _controls;
        private readonly ChildLayout[] _children;

        private LayoutState(VisualElement element, Control container, Control[] controls)
        {
            _direction = element.style.flexDirection;
            _flexGrow = element.style.flexGrow;
            _scrollView = element is ScrollView;
            _container = container;
            _controls = controls.ToArray();
            _children = element.Children.Select(child => new ChildLayout(
                child.style.width,
                child.style.height,
                child.style.flexGrow)).ToArray();
        }

        public static LayoutState Capture(VisualElement element, Control container, Control[] controls) =>
            new(element, container, controls);

        public bool Matches(VisualElement element, Control container, Control[] controls)
        {
            if (!ReferenceEquals(_container, container) || _direction != element.style.flexDirection ||
                _flexGrow != element.style.flexGrow || _scrollView != (element is ScrollView) ||
                _controls.Length != controls.Length || _children.Length != element.Children.Count)
                return false;
            for (var index = 0; index < controls.Length; index++)
            {
                if (!ReferenceEquals(_controls[index], controls[index])) return false;
                var child = element.Children[index];
                if (_children[index] != new ChildLayout(child.style.width, child.style.height, child.style.flexGrow))
                    return false;
            }
            return controls.All(control => ReferenceEquals(control.Parent, container));
        }
    }

    private readonly record struct ChildLayout(float Width, float Height, float FlexGrow);

    private const uint EmGetFirstVisibleLine = 0x00CE;
    private const uint EmLineScroll = 0x00B6;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr word, IntPtr parameter);

    private sealed class ThemedComboBox : ComboBox
    {
        public ThemedComboBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Flat;
            ItemHeight = 20;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Bounds.Width <= 0 || e.Bounds.Height <= 0) return;
            var selected = (e.State & DrawItemState.Selected) != 0;
            using var background = new SolidBrush(selected ? UIElementsTheme.Selection : UIElementsTheme.Field);
            e.Graphics.FillRectangle(background, e.Bounds);
            var text = e.Index >= 0 && e.Index < Items.Count ? GetItemText(Items[e.Index]) : Text;
            TextRenderer.DrawText(e.Graphics, text, Font,
                new Rectangle(e.Bounds.Left + 4, e.Bounds.Top, Math.Max(0, e.Bounds.Width - 6), e.Bounds.Height),
                Enabled ? UIElementsTheme.Text : UIElementsTheme.TextDisabled,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    private sealed class ThemedTreeView : System.Windows.Forms.TreeView
    {
        private TreeNode? _hotNode;

        public ThemedTreeView()
        {
            DrawMode = TreeViewDrawMode.OwnerDrawAll;
            ItemHeight = UIElementsTheme.RowHeight;
            Indent = 16;
            ShowPlusMinus = true;
            ShowRootLines = false;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }

        protected override void OnDrawNode(DrawTreeNodeEventArgs e)
        {
            if (e.Node is null) return;
            var row = new Rectangle(0, e.Bounds.Top, ClientSize.Width, ItemHeight);
            var selected = e.Node.IsSelected;
            var background = selected
                ? (Focused ? UIElementsTheme.Selection : UIElementsTheme.SelectionInactive)
                : ReferenceEquals(e.Node, _hotNode) ? UIElementsTheme.RowHover : BackColor;
            using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, row);

            var left = 4 + e.Node.Level * Indent;
            if (e.Node.Nodes.Count > 0)
            {
                var centerY = row.Top + row.Height / 2;
                Point[] triangle = e.Node.IsExpanded
                    ? [new(left, centerY - 2), new(left + 8, centerY - 2), new(left + 4, centerY + 3)]
                    : [new(left + 1, centerY - 4), new(left + 1, centerY + 4), new(left + 6, centerY)];
                using var glyph = new SolidBrush(UIElementsTheme.TextMuted);
                e.Graphics.FillPolygon(glyph, triangle);
            }

            var textLeft = left + 13;
            TextRenderer.DrawText(e.Graphics, e.Node.Text, Font,
                new Rectangle(textLeft, row.Top, Math.Max(0, row.Right - textLeft - 4), row.Height),
                Enabled ? UIElementsTheme.Text : UIElementsTheme.TextDisabled,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var node = GetNodeAt(e.Location);
            if (ReferenceEquals(node, _hotNode)) return;
            _hotNode = node;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hotNode = null;
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnAfterExpand(TreeViewEventArgs e) { base.OnAfterExpand(e); Invalidate(); }
        protected override void OnAfterCollapse(TreeViewEventArgs e) { base.OnAfterCollapse(e); Invalidate(); }
    }

    private sealed class ThemedListBox : ListBox
    {
        private int _hotIndex = -1;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool AlternatingRows { get; set; }

        public ThemedListBox(bool alternating)
        {
            AlternatingRows = alternating;
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = UIElementsTheme.RowHeight;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            var selected = (e.State & DrawItemState.Selected) != 0;
            var background = selected
                ? (Focused ? UIElementsTheme.Selection : UIElementsTheme.SelectionInactive)
                : e.Index == _hotIndex ? UIElementsTheme.RowHover
                : AlternatingRows && e.Index % 2 != 0 ? UIElementsTheme.Window : UIElementsTheme.Panel;
            using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font,
                new Rectangle(e.Bounds.Left + 5, e.Bounds.Top, Math.Max(0, e.Bounds.Width - 8), e.Bounds.Height),
                Enabled ? UIElementsTheme.Text : UIElementsTheme.TextDisabled,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var index = IndexFromPoint(e.Location);
            if (index == _hotIndex) return;
            _hotIndex = index;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hotIndex = -1;
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }
}
