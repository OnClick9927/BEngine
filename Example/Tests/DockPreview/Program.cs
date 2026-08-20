using System.Collections;
using System.Reflection;

namespace BEngine.ExampleTests.DockPreview;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        ApplicationConfiguration.Initialize();
        var assembly = Assembly.Load("BEngine.Editor");
        var workspaceType = assembly.GetType("BEngine.Editor.DockWorkspace", true)!;
        using var workspace = (Control)Activator.CreateInstance(workspaceType, nonPublic: true)!;
        workspace.Size = new Size(900, 600);
        workspace.PerformLayout();

        var zones = (IDictionary)workspaceType.GetField("_zones", BindingFlags.Instance |
            BindingFlags.NonPublic)!.GetValue(workspace)!;
        var targets = zones.Values.Cast<Control>().ToArray();
        Require(targets.Length == 4, "Dock workspace did not expose four docking targets.");

        var hide = workspaceType.GetMethod("HideDockPreview", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var active = workspaceType.GetProperty("IsDockPreviewActive", BindingFlags.Instance |
            BindingFlags.NonPublic)!;
        var activeTarget = workspaceType.GetProperty("DockPreviewTarget", BindingFlags.Instance |
            BindingFlags.NonPublic)!;
        var overlay = (Control)workspaceType.GetField("_dockPreview", BindingFlags.Instance |
            BindingFlags.NonPublic)!.GetValue(workspace)!;
        var showOverlay = overlay.GetType().GetMethod("ShowFor", BindingFlags.Instance |
            BindingFlags.NonPublic)!;
        var updateDropPosition = overlay.GetType().GetMethod("UpdateDropPosition", BindingFlags.Instance |
            BindingFlags.NonPublic)!;
        var dropPosition = workspaceType.GetProperty("DockPreviewPosition", BindingFlags.Instance |
            BindingFlags.NonPublic)!;
        var previewBounds = workspaceType.GetProperty("DockPreviewBounds", BindingFlags.Instance |
            BindingFlags.NonPublic)!;

        for (var index = 0; index < targets.Length; index++)
        {
            var target = targets[index];
            var expectedBounds = index switch
            {
                0 => new Rectangle(0, 0, 220, 420),
                1 => new Rectangle(220, 0, 450, 420),
                2 => new Rectangle(670, 0, 230, 420),
                _ => new Rectangle(0, 420, 900, 180)
            };
            showOverlay.Invoke(overlay, [target, expectedBounds, expectedBounds, $"Target {index}"]);
            Require((bool)active.GetValue(workspace)!, "A valid docking target did not activate its preview.");
            Require(ReferenceEquals(activeTarget.GetValue(workspace), target),
                "Dock preview highlighted a different docking target.");
            Require(overlay.Visible && overlay.Parent == workspace,
                "Dock preview is not a visible top-level workspace overlay.");
            Require(overlay.Bounds == expectedBounds, "Dock preview bounds do not match the destination pane.");
            Require(ContainsAccentPixels(overlay), "Dock preview did not paint its visible blue docking cue.");
        }

        var centerTarget = targets.Single(target => string.Equals(
            target.GetType().GetProperty("Zone")!.GetValue(target)!.ToString(), "Center",
            StringComparison.Ordinal));
        var previewArea = new Rectangle(0, 0, 600, 400);
        showOverlay.Invoke(overlay, [centerTarget, previewArea, previewArea, "Scene"]);
        foreach (var (name, point, expected) in new[]
                 {
                     ("Left", new Point(10, 200), new Rectangle(0, 0, 300, 400)),
                     ("Right", new Point(590, 200), new Rectangle(300, 0, 300, 400)),
                     ("Top", new Point(300, 10), new Rectangle(0, 0, 600, 200)),
                     ("Bottom", new Point(300, 390), new Rectangle(0, 200, 600, 200)),
                     ("Center", new Point(300, 200), new Rectangle(0, 0, 600, 400))
                 })
        {
            updateDropPosition.Invoke(overlay, [overlay.PointToScreen(point)]);
            Require(string.Equals(dropPosition.GetValue(workspace)!.ToString(), name,
                    StringComparison.Ordinal), $"Dock point {name} selected the wrong drop position.");
            Require((Rectangle)previewBounds.GetValue(workspace)! == expected,
                $"Dock point {name} did not preview the expected target sub-region.");
        }

        hide.Invoke(workspace, null);
        Require(!(bool)active.GetValue(workspace)! && !overlay.Visible,
            "Dock preview remained visible after the drag left the docking target.");

        VerifySplitDockingAndEmptyCollapse(workspaceType, workspace, zones);

        Console.WriteLine("DOCK_PREVIEW_OK|four-zones,five-drop-targets,split-region,empty-collapse,blue-overlay,cleanup");
        return 0;
    }

    private static void VerifySplitDockingAndEmptyCollapse(Type workspaceType, Control workspace, IDictionary zones)
    {
        var zoneType = workspaceType.Assembly.GetType("BEngine.Editor.DockZone", true)!;
        var dropType = workspaceType.Assembly.GetType("BEngine.Editor.DockDropPosition", true)!;
        var centerZone = Enum.Parse(zoneType, "Center");
        var rightZone = Enum.Parse(zoneType, "Right");
        var centerDrop = Enum.Parse(dropType, "Center");
        var rightDrop = Enum.Parse(dropType, "Right");
        var addPanel = workspaceType.GetMethod("AddPanel")!;
        var closePanel = workspaceType.GetMethod("ClosePanel")!;
        var moveTab = workspaceType.GetMethod("MoveTab", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var dynamicSplits = (IEnumerable)workspaceType.GetField("_dynamicSplits", BindingFlags.Instance |
            BindingFlags.NonPublic)!.GetValue(workspace)!;
        var centerTabs = zones.Values.Cast<Control>().Single(target => string.Equals(
            target.GetType().GetProperty("Zone")!.GetValue(target)!.ToString(), "Center",
            StringComparison.Ordinal));

        addPanel.Invoke(workspace, ["DockA", "Dock A", new Panel(), centerZone, null]);
        addPanel.Invoke(workspace, ["DockB", "Dock B", new Panel(), centerZone, null]);
        addPanel.Invoke(workspace, ["KeepRight", "Keep Right", new Panel(), rightZone, null]);
        var tabPages = (TabControl.TabPageCollection)centerTabs.GetType().GetProperty("TabPages")!
            .GetValue(centerTabs)!;
        var tabB = tabPages.Cast<TabPage>().Single(tab => tab.Name == "DockB");
        moveTab.Invoke(workspace, [tabB, centerTabs, rightDrop]);
        workspace.PerformLayout();

        Require(dynamicSplits.Cast<object>().Count() == 1,
            "Edge docking did not create a split region inside the target.");
        var split = dynamicSplits.Cast<SplitContainer>().Single();
        Require(split.Orientation == Orientation.Vertical,
            "Right-side docking created a horizontal split instead of a vertical split.");
        Require(split.Panel1.Controls.Contains(centerTabs) && tabB.Parent != centerTabs,
            "Right-side docking replaced the target instead of sharing it as a separate region.");
        Require(Math.Abs(split.Panel1.Width - split.Panel2.Width) <= split.SplitterWidth + 4,
            "New target regions were not initialized at approximately 50/50 size.");

        moveTab.Invoke(workspace, [tabB, centerTabs, centerDrop]);
        workspace.PerformLayout();
        System.Windows.Forms.Application.DoEvents();
        Require(!dynamicSplits.Cast<object>().Any(),
            "Collapsed split was not removed after the drag/drop message completed.");
        Require(tabB.Parent == centerTabs && centerTabs.Parent is SplitterPanel,
            "Remaining dock target did not replace the retired split container.");

        closePanel.Invoke(workspace, ["DockA"]);
        closePanel.Invoke(workspace, ["DockB"]);
        System.Windows.Forms.Application.DoEvents();
        var right = (SplitContainer)workspaceType.GetField("_right", BindingFlags.Instance |
            BindingFlags.NonPublic)!.GetValue(workspace)!;
        Require(right.Panel1Collapsed, "Closing the last center tab left a blank center dock region.");
        closePanel.Invoke(workspace, ["KeepRight"]);
    }

    private static bool ContainsAccentPixels(Control control)
    {
        using var bitmap = new Bitmap(Math.Max(1, control.Width), Math.Max(1, control.Height));
        using (var graphics = Graphics.FromImage(bitmap))
        using (var paint = new PaintEventArgs(graphics, new Rectangle(Point.Empty, bitmap.Size)))
            control.GetType().GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(control, [paint]);
        var matches = 0;
        for (var y = 0; y < bitmap.Height; y += Math.Max(1, bitmap.Height / 24))
        for (var x = 0; x < bitmap.Width; x += Math.Max(1, bitmap.Width / 24))
        {
            var pixel = bitmap.GetPixel(x, y);
            if (pixel.B > pixel.R + 20 && pixel.B > pixel.G + 5) matches++;
        }
        return matches >= 3;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
