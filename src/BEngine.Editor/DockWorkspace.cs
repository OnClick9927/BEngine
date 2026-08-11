using System.ComponentModel;
using BEngine.UIElements.Editor;

namespace BEngine.Editor;

internal enum DockZone
{
    Left,
    Center,
    Right,
    Bottom
}

internal sealed class DockWorkspace : UserControl
{
    private readonly SplitContainer _vertical = new() { Orientation = Orientation.Horizontal, Dock = DockStyle.Fill };
    private readonly SplitContainer _left = new() { Orientation = Orientation.Vertical, Dock = DockStyle.Fill };
    private readonly SplitContainer _right = new() { Orientation = Orientation.Vertical, Dock = DockStyle.Fill };
    private readonly Dictionary<DockZone, DockTabControl> _zones = [];
    private readonly Dictionary<string, DockPanelRecord> _panels = new(StringComparer.Ordinal);
    private readonly List<FloatingDockForm> _floatingForms = [];
    private DockTabControl? _activeTabs;

    public event Action? layoutChanged;

    public DockWorkspace()
    {
        Dock = DockStyle.Fill;
        BackColor = UIElementsTheme.Border;
        _zones[DockZone.Left] = CreateTabs(DockZone.Left);
        _zones[DockZone.Center] = CreateTabs(DockZone.Center);
        _zones[DockZone.Right] = CreateTabs(DockZone.Right);
        _zones[DockZone.Bottom] = CreateTabs(DockZone.Bottom);
        _activeTabs = _zones[DockZone.Center];

        UIElementsTheme.ApplySplitContainer(_vertical);
        UIElementsTheme.ApplySplitContainer(_left);
        UIElementsTheme.ApplySplitContainer(_right);

        _left.Panel1.Controls.Add(_zones[DockZone.Left]);
        _left.Panel2.Controls.Add(_right);
        _right.Panel1.Controls.Add(_zones[DockZone.Center]);
        _right.Panel2.Controls.Add(_zones[DockZone.Right]);
        _vertical.Panel1.Controls.Add(_left);
        _vertical.Panel2.Controls.Add(_zones[DockZone.Bottom]);
        Controls.Add(_vertical);

        _vertical.SplitterMoved += (_, _) => layoutChanged?.Invoke();
        _left.SplitterMoved += (_, _) => layoutChanged?.Invoke();
        _right.SplitterMoved += (_, _) => layoutChanged?.Invoke();
        Resize += (_, _) => ApplySafeSplitterDistances();
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int LeftWidth
    {
        get => _left.SplitterDistance;
        set { _left.Tag = value; ApplySafeSplitterDistances(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int RightWidth
    {
        get => Math.Max(0, _right.Width - _right.SplitterDistance);
        set { _right.Tag = value; ApplySafeSplitterDistances(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int BottomHeight
    {
        get => Math.Max(0, _vertical.Height - _vertical.SplitterDistance);
        set { _vertical.Tag = value; ApplySafeSplitterDistances(); }
    }

    public IEnumerable<(string Id, string Title, bool Visible)> Panels =>
        _panels.Values.Select(panel => (panel.Id, panel.Title, panel.Tab.Parent is not null));

    public void AddPanel(string id, string title, Control content, DockZone zone, Action? closed = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (_panels.ContainsKey(id)) throw new InvalidOperationException($"Dock panel '{id}' already exists.");
        var tab = new TabPage(title)
        {
            Name = id,
            BackColor = UIElementsTheme.Panel,
            ForeColor = UIElementsTheme.Text,
            Padding = Padding.Empty
        };
        content.Dock = DockStyle.Fill;
        tab.Controls.Add(content);
        var record = new DockPanelRecord(id, title, zone, tab, closed);
        tab.Tag = record;
        _panels.Add(id, record);
        _zones[zone].TabPages.Add(tab);
    }

    public void ShowPanel(string id)
    {
        if (!_panels.TryGetValue(id, out var panel)) return;
        if (panel.Tab.Parent is null) _zones[panel.DefaultZone].TabPages.Add(panel.Tab);
        if (panel.Tab.Parent is TabControl tabs) tabs.SelectedTab = panel.Tab;
        panel.Tab.Focus();
        layoutChanged?.Invoke();
    }

    public void ClosePanel(string id)
    {
        if (!_panels.TryGetValue(id, out var panel)) return;
        panel.Tab.Parent?.Controls.Remove(panel.Tab);
        panel.Closed?.Invoke();
        CloseEmptyFloatingForms();
        layoutChanged?.Invoke();
    }

    public void FloatPanel(string id, Size? preferredSize = null)
    {
        if (!_panels.TryGetValue(id, out var panel) || panel.Tab.FindForm() is FloatingDockForm) return;
        if (panel.Tab.Parent is DockTabControl owner) FloatTab(panel.Tab, owner, preferredSize);
    }

    private DockTabControl CreateTabs(DockZone zone)
    {
        var tabs = new DockTabControl(this, zone) { Dock = DockStyle.Fill };
        tabs.CloseRequested += tab => ClosePanel(tab.Name);
        tabs.LayoutChanged += () => layoutChanged?.Invoke();
        return tabs;
    }

    internal void MoveTab(TabPage tab, DockTabControl destination)
    {
        if (ReferenceEquals(tab.Parent, destination)) return;
        tab.Parent?.Controls.Remove(tab);
        destination.TabPages.Add(tab);
        destination.SelectedTab = tab;
        Activate(destination);
        CloseEmptyFloatingForms();
        layoutChanged?.Invoke();
    }

    internal bool IsActive(DockTabControl tabs) => ReferenceEquals(_activeTabs, tabs);

    internal void Activate(DockTabControl tabs)
    {
        if (ReferenceEquals(_activeTabs, tabs)) return;
        var previous = _activeTabs;
        _activeTabs = tabs;
        previous?.Invalidate();
        tabs.Invalidate();
    }

    internal void FloatTab(TabPage tab, DockTabControl source, Size? preferredSize = null)
    {
        if (tab.Parent is null) return;
        source.TabPages.Remove(tab);
        var screen = System.Windows.Forms.Screen.FromPoint(Cursor.Position).WorkingArea;
        var requestedSize = preferredSize ?? new Size(Math.Max(360, tab.Width), Math.Max(240, tab.Height));
        var size = new Size(
            Math.Clamp(requestedSize.Width, 360, Math.Max(360, screen.Width)),
            Math.Clamp(requestedSize.Height, 240, Math.Max(240, screen.Height)));
        var requestedLocation = Cursor.Position - new Size(80, 12);
        var location = new Point(
            Math.Clamp(requestedLocation.X, screen.Left, Math.Max(screen.Left, screen.Right - size.Width)),
            Math.Clamp(requestedLocation.Y, screen.Top, Math.Max(screen.Top, screen.Bottom - size.Height)));
        var form = new FloatingDockForm(this, tab)
        {
            StartPosition = FormStartPosition.Manual,
            Location = location,
            Size = size,
            Icon = FindForm()?.Icon
        };
        _floatingForms.Add(form);
        form.FormClosed += (_, _) =>
        {
            _floatingForms.Remove(form);
            if (tab.Parent is null && _panels.TryGetValue(tab.Name, out var panel))
                _zones[panel.DefaultZone].TabPages.Add(tab);
            layoutChanged?.Invoke();
        };
        form.Show(FindForm());
        Debug.Log($"Floating editor window shown: {tab.Text}; Handle=0x{form.Handle.ToInt64():X}; " +
                  $"Bounds={form.Left},{form.Top},{form.Width},{form.Height}.");
        layoutChanged?.Invoke();
    }

    private void CloseEmptyFloatingForms()
    {
        foreach (var form in _floatingForms.Where(form => form.Tabs.TabCount == 0).ToArray()) form.CloseWithoutRestore();
    }

    private void ApplySafeSplitterDistances()
    {
        if (Width < 500 || Height < 300) return;
        if (_vertical.Tag is int bottom)
            _vertical.SplitterDistance = Math.Clamp(_vertical.Height - bottom, 120, Math.Max(120, _vertical.Height - 100));
        if (_left.Tag is int leftWidth)
            _left.SplitterDistance = Math.Clamp(leftWidth, 100, Math.Max(100, _left.Width - 260));
        if (_right.Tag is int rightWidth)
            _right.SplitterDistance = Math.Clamp(_right.Width - rightWidth, 220, Math.Max(220, _right.Width - 120));
    }

    private sealed record DockPanelRecord(
        string Id,
        string Title,
        DockZone DefaultZone,
        TabPage Tab,
        Action? Closed);
}

internal sealed class DockTabControl : TabControl
{
    private const int WmPaint = 0x000F;
    private readonly DockWorkspace _workspace;
    private Point _dragStart;
    private TabPage? _dragTab;
    private int _hoverIndex = -1;
    private int _hoverCloseIndex = -1;
    private bool _formEventsHooked;

    public DockZone Zone { get; }
    public event Action<TabPage>? CloseRequested;
    public event Action? LayoutChanged;

    public DockTabControl(DockWorkspace workspace, DockZone zone)
    {
        _workspace = workspace;
        Zone = zone;
        AllowDrop = true;
        DrawMode = TabDrawMode.OwnerDrawFixed;
        ItemSize = new Size(112, UIElementsTheme.TabHeight);
        SizeMode = TabSizeMode.Normal;
        Padding = new Point(9, 3);
        BackColor = UIElementsTheme.Panel;
        ForeColor = UIElementsTheme.Text;
        Font = UIElementsTheme.Font();
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        DrawItem += DrawTab;
        MouseDown += OnTabMouseDown;
        MouseMove += OnTabMouseMove;
        MouseUp += OnTabMouseUp;
        MouseClick += OnTabMouseClick;
        DragEnter += OnTabDragEnter;
        DragOver += OnTabDragEnter;
        DragDrop += OnTabDragDrop;
        MouseLeave += (_, _) =>
        {
            if (_hoverIndex < 0 && _hoverCloseIndex < 0) return;
            _hoverIndex = -1;
            _hoverCloseIndex = -1;
            Invalidate();
        };
        SelectedIndexChanged += (_, _) =>
        {
            Invalidate();
            LayoutChanged?.Invoke();
        };
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (e.Control is TabPage tab)
        {
            tab.BackColor = UIElementsTheme.Panel;
            tab.ForeColor = UIElementsTheme.Text;
            tab.Font = UIElementsTheme.Font();
            tab.Enter += (_, _) => _workspace.Activate(this);
            tab.MouseDown += (_, _) => _workspace.Activate(this);
        }
    }

    protected override void OnCreateControl()
    {
        base.OnCreateControl();
        if (_formEventsHooked || FindForm() is not { } form) return;
        _formEventsHooked = true;
        form.Activated += (_, _) => Invalidate();
        form.Deactivate += (_, _) => Invalidate();
    }

    private void DrawTab(object? sender, DrawItemEventArgs args)
    {
        if (args.Index < 0 || args.Index >= TabPages.Count) return;
        var selected = args.Index == SelectedIndex;
        var ownerForm = FindForm();
        var formActive = ownerForm is not null &&
                         (ownerForm.ContainsFocus || ReferenceEquals(Form.ActiveForm, ownerForm));
        var active = selected && formActive && _workspace.IsActive(this);
        var hovered = args.Index == _hoverIndex;
        var rect = GetTabRect(args.Index);
        using var background = new SolidBrush(selected
            ? UIElementsTheme.PanelRaised
            : hovered ? UIElementsTheme.TabHover : UIElementsTheme.TabInactive);
        args.Graphics.FillRectangle(background, rect);
        using (var divider = new Pen(UIElementsTheme.Divider))
        {
            args.Graphics.DrawLine(divider, rect.Right - 1, rect.Top + 3, rect.Right - 1, rect.Bottom - 2);
            args.Graphics.DrawLine(divider, rect.Left, rect.Bottom - 1, rect.Right, rect.Bottom - 1);
        }
        if (active)
        {
            using var accent = new SolidBrush(UIElementsTheme.Accent);
            args.Graphics.FillRectangle(accent, rect.Left, rect.Top, rect.Width, 2);
        }
        var closeRect = GetCloseRect(rect);
        if (args.Index == _hoverCloseIndex)
        {
            using var closeHover = new SolidBrush(UIElementsTheme.Hover);
            args.Graphics.FillRectangle(closeHover, closeRect);
        }
        TextRenderer.DrawText(args.Graphics, TabPages[args.Index].Text, Font,
            new Rectangle(rect.Left + 8, rect.Top + 1, Math.Max(8, rect.Width - 30), rect.Height - 2),
            selected ? UIElementsTheme.TextStrong : UIElementsTheme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(args.Graphics, "x", Font, closeRect,
            args.Index == _hoverCloseIndex ? UIElementsTheme.TextStrong : UIElementsTheme.TextDisabled,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void OnTabMouseDown(object? sender, MouseEventArgs args)
    {
        if (args.Button != MouseButtons.Left) return;
        _workspace.Activate(this);
        _dragStart = args.Location;
        _dragTab = TabAt(args.Location);
    }

    private void OnTabMouseMove(object? sender, MouseEventArgs args)
    {
        var hoveredTab = TabAt(args.Location);
        var hoverIndex = hoveredTab is null ? -1 : TabPages.IndexOf(hoveredTab);
        var hoverCloseIndex = hoverIndex >= 0 && GetCloseRect(GetTabRect(hoverIndex)).Contains(args.Location)
            ? hoverIndex
            : -1;
        if (hoverIndex != _hoverIndex || hoverCloseIndex != _hoverCloseIndex)
        {
            _hoverIndex = hoverIndex;
            _hoverCloseIndex = hoverCloseIndex;
            Invalidate();
        }
        if (_dragTab is null || args.Button != MouseButtons.Left) return;
        var dragSize = SystemInformation.DragSize;
        var bounds = new Rectangle(_dragStart.X - dragSize.Width / 2, _dragStart.Y - dragSize.Height / 2,
            dragSize.Width, dragSize.Height);
        if (bounds.Contains(args.Location)) return;
        var tab = _dragTab;
        _dragTab = null;
        var effect = DoDragDrop(tab, DragDropEffects.Move);
        if (effect == DragDropEffects.None && tab.Parent == this) _workspace.FloatTab(tab, this);
    }

    private void OnTabMouseUp(object? sender, MouseEventArgs args) => _dragTab = null;

    private void OnTabMouseClick(object? sender, MouseEventArgs args)
    {
        var tab = TabAt(args.Location);
        if (tab is null) return;
        var index = TabPages.IndexOf(tab);
        if (args.Button == MouseButtons.Left && GetCloseRect(GetTabRect(index)).Contains(args.Location))
        {
            CloseRequested?.Invoke(tab);
            return;
        }
        if (args.Button != MouseButtons.Right) return;
        _workspace.Activate(this);
        SelectedTab = tab;
        var menu = new ContextMenuStrip();
        UIElementsTheme.ApplyContextMenu(menu);
        menu.Items.Add("Float", null, (_, _) => _workspace.FloatTab(tab, this));
        menu.Items.Add("Close", null, (_, _) => CloseRequested?.Invoke(tab));
        menu.Show(this, args.Location);
    }

    private void OnTabDragEnter(object? sender, DragEventArgs args)
    {
        args.Effect = args.Data?.GetDataPresent(typeof(TabPage)) == true ? DragDropEffects.Move : DragDropEffects.None;
    }

    private void OnTabDragDrop(object? sender, DragEventArgs args)
    {
        if (args.Data?.GetData(typeof(TabPage)) is TabPage tab) _workspace.MoveTab(tab, this);
    }

    private TabPage? TabAt(Point point)
    {
        for (var index = 0; index < TabPages.Count; index++)
        {
            if (GetTabRect(index).Contains(point)) return TabPages[index];
        }
        return null;
    }

    private static Rectangle GetCloseRect(Rectangle tabRect) =>
        new(tabRect.Right - 22, tabRect.Top + 3, 18, Math.Max(16, tabRect.Height - 6));

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != WmPaint || Width <= 0 || Height <= 0) return;
        using var graphics = Graphics.FromHwnd(Handle);
        PaintNativeFrame(graphics);
    }

    private void PaintNativeFrame(Graphics graphics)
    {
        var display = DisplayRectangle;
        if (display.Width <= 0 || display.Height <= 0) return;
        using var panel = new SolidBrush(UIElementsTheme.Panel);
        using var tabStrip = new SolidBrush(UIElementsTheme.TabInactive);
        using var divider = new Pen(UIElementsTheme.Divider);

        var lastTabRight = TabCount > 0 ? GetTabRect(TabCount - 1).Right : 0;
        if (lastTabRight < Width)
            graphics.FillRectangle(tabStrip, lastTabRight, 0, Width - lastTabRight, Math.Max(0, display.Top - 1));

        graphics.FillRectangle(panel, 0, display.Top, Math.Max(0, display.Left), Height - display.Top);
        graphics.FillRectangle(panel, display.Right, display.Top, Math.Max(0, Width - display.Right), Height - display.Top);
        graphics.FillRectangle(panel, 0, display.Bottom, Width, Math.Max(0, Height - display.Bottom));
        graphics.DrawLine(divider, 0, display.Top - 1, Width, display.Top - 1);
    }
}

internal sealed class FloatingDockForm : Form
{
    private bool _closeWithoutRestore;
    public DockTabControl Tabs { get; }

    public FloatingDockForm(DockWorkspace workspace, TabPage tab)
    {
        Text = tab.Text;
        UIElementsTheme.Apply(this, UIElementsTheme.Window);
        UIElementsTheme.ApplyDarkTitleBar(this);
        MinimumSize = new Size(220, 140);
        Tabs = new DockTabControl(workspace, DockZone.Center) { Dock = DockStyle.Fill };
        Tabs.CloseRequested += page => page.Parent?.Controls.Remove(page);
        Tabs.TabPages.Add(tab);
        Controls.Add(Tabs);
    }

    public void CloseWithoutRestore()
    {
        _closeWithoutRestore = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_closeWithoutRestore) Tabs.TabPages.Clear();
        base.OnFormClosing(e);
    }
}
