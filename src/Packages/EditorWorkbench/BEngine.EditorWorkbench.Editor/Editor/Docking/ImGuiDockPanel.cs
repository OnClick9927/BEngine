using System.Diagnostics;
using System.Reflection;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using BEngine.Serialization;
using BEngine.Editor.Documents;
using NVector4 = System.Numerics.Vector4;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.IProjectAssetDatabase;

namespace BEngine.Editor;

internal sealed class ImGuiDockPanel(string id, EditorWindow window, DockArea preferredArea = DockArea.Center)
{
    private TabContentKey _tabContentKey;
    private GUIContent? _fittedTabContent;

    public string Id { get; } = id;
    public EditorWindow Window { get; } = window;
    public DockArea PreferredArea { get; } = preferredArea;
    public bool Visible { get; set; } = true;
    internal ImGuiDockWorkspace.DockGroup? Group { get; set; }

    internal GUIContent GetTabContent(Fix64 visibleWidth, GUIStyle style)
    {
        var source = Window.titleContent;
        var key = new TabContentKey(source.text, source.image, source.tooltip,
            visibleWidth, style.fontSize, GUIUtility.fontFamily);
        if (_fittedTabContent is { } cached && _tabContentKey == key) return cached;

        var fitted = ImGuiDockWorkspace.FitTabContent(source, visibleWidth, style);
        _tabContentKey = key;
        // Keep one clipped result per panel. A title that fits still returns its original
        // GUIContent, including after callers replace or mutate titleContent in place.
        _fittedTabContent = ReferenceEquals(fitted, source) ? null : fitted;
        return fitted;
    }

    private readonly record struct TabContentKey(string Text, string Image, string Tooltip,
        Fix64 VisibleWidth, Fix64 FontSize, string FontFamily);
}
