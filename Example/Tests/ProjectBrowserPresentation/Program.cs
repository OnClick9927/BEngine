using System.Reflection;
using BEngine.Editor;
using BEngine.Editor.Rendering;

namespace BEngine.ExampleTests.ProjectBrowserPresentation;

internal static class Program
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public |
                                                         BindingFlags.NonPublic;
    private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public |
                                                       BindingFlags.NonPublic;
    private static readonly MethodInfo BeginFrame = typeof(GUI).GetMethod("BeginFrame", StaticMembers) ??
        throw new MissingMethodException(typeof(GUI).FullName, "BeginFrame");
    private static readonly MethodInfo EndFrame = typeof(GUI).GetMethod("EndFrame", StaticMembers) ??
        throw new MissingMethodException(typeof(GUI).FullName, "EndFrame");

    private static int Main()
    {
        try
        {
            VerifyOneAndTwoColumnSwitching();
            VerifyTwoColumnProjectPresentation();
            VerifyFolderFirstNameOrdering();
            VerifyFolderTreeAndBreadcrumbNavigation();
            VerifyGuiContentTooltipPipeline();
            Console.WriteLine(
                "PROJECT_BROWSER_PRESENTATION_OK|one-column,two-column,folder-first-name-order,folder-tree,direct-children,zoom,tile-labels,breadcrumb,tooltip-delay,tooltip-bounds,blank-tooltip");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"PROJECT_BROWSER_PRESENTATION_FAILED|{exception}");
            return 1;
        }
    }

    private static void VerifyOneAndTwoColumnSwitching()
    {
        var (projectType, itemType, project) = CreateProjectWindow();
        SetItemsAndSelection(projectType, project, itemType, "Assets");

        Require(SetProjectMode(projectType, project, twoColumn: false),
            "Project window has no switchable one-column presentation state.");
        var oneColumn = RenderProject(projectType, project, 1040, 620);
        var oneColumnScenes = FindText(oneColumn, "Scenes", takeLast: false);
        Require(oneColumnScenes.Rect.X < 240,
            "OneColumn mode did not render the project hierarchy as one full-width tree.");
        Require(!HasVerticalDivider(oneColumn, 620),
            "OneColumn mode retained the two-column folder divider.");

        Require(SetProjectMode(projectType, project, twoColumn: true),
            "Project window has no switchable two-column presentation state.");
        var twoColumn = RenderProject(projectType, project, 1040, 620);
        var leftScenes = FindText(twoColumn, "Scenes", takeLast: false);
        var rightScenes = FindText(twoColumn, "Scenes", takeLast: true);
        Require(rightScenes.Rect.X >= leftScenes.Rect.X + 160,
            $"Switching back to TwoColumn did not restore separate tree and content panes: " +
            $"left={leftScenes.Rect.X}, right={rightScenes.Rect.X}.");
        Require(HasVerticalDivider(twoColumn, 620),
            "Switching back to TwoColumn did not restore the draggable divider.");
    }

    private static void VerifyTwoColumnProjectPresentation()
    {
        var (projectType, itemType, project) = CreateProjectWindow();
        SetItemsAndSelection(projectType, project, itemType, "Assets/Scenes");
        Require(SetProjectMode(projectType, project, twoColumn: true),
            "Project window has no switchable TwoColumn presentation state.");
        Require(SetAssetScale(projectType, project, 96),
            "Project window has no persistent resource zoom/thumbnail-size state.");

        var commands = RenderProject(projectType, project, 1040, 620);
        var scenes = FindText(commands, "Scenes", takeLast: false);
        var mainScene = FindText(commands, "Main.scene.yaml", takeLast: true);
        var lighting = FindText(commands, "Lighting.material.yaml", takeLast: true);
        Require(mainScene.Rect.X >= scenes.Rect.X + 160 && lighting.Rect.X >= scenes.Rect.X + 160,
            $"TwoColumn mode did not separate the folder tree from the selected folder contents: " +
            $"Scenes={scenes.Rect.X}, Main={mainScene.Rect.X}, Lighting={lighting.Rect.X}.");
        Require(!commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                         command.Content == "Player.cs" &&
                                         command.Rect.X >= mainScene.Rect.X - 8),
            "The right Project pane contains an asset outside the selected folder.");
        Require(!commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                         command.Content == "Deep.scene.yaml"),
            "The right Project pane contains a descendant deeper than one level.");

        var divider = commands.Where(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                                command.Rect.Width is >= 0.9f and <= 2.1f &&
                                                command.Rect.Height >= 400 &&
                                                command.Rect.X > scenes.Rect.X + 80 &&
                                                command.Rect.X < mainScene.Rect.X)
            .ToArray();
        Require(divider.Length > 0, "TwoColumn mode has no visible draggable divider.");

        var preview = commands.Where(command => command.Type == GpuCanvasCommandType.Image &&
                                                command.Rect.X >= mainScene.Rect.X - 16 &&
                                                command.Rect.Width >= 64 && command.Rect.Height >= 64)
            .OrderBy(command => command.Rect.Y)
            .FirstOrDefault();
        Require(preview.Type == GpuCanvasCommandType.Image,
            "Increasing Project resource scale did not produce a larger asset preview.");
        Require(mainScene.Rect.Y >= preview.Rect.Bottom - 2,
            "The Project asset label is not laid out below its scaled preview.");
        Require(preview.Rect.Right <= preview.ClipRect.Right + 0.1f &&
                preview.Rect.Bottom <= preview.ClipRect.Bottom + 0.1f,
            "A scaled Project preview escapes its content pane clipping bounds.");

        var gridMinimumX = scenes.Rect.X + 160;
        VerifyGridTilesHaveLabels(commands, gridMinimumX, 96,
            "Main.scene.yaml", "Lighting.material.yaml", "SubScenes");
        foreach (var zoom in new[] { 32, 128 })
        {
            Require(SetAssetScale(projectType, project, zoom), $"Could not set Project zoom to {zoom}.");
            var zoomed = RenderProject(projectType, project, 1040, 620);
            var zoomedScenes = FindText(zoomed, "Scenes", takeLast: false);
            VerifyGridTilesHaveLabels(zoomed, zoomedScenes.Rect.X + 160, zoom,
                "Main.scene.yaml", "Lighting.material.yaml", "SubScenes");
        }
    }

    private static void VerifyFolderTreeAndBreadcrumbNavigation()
    {
        var (projectType, itemType, project) = CreateProjectWindow();
        SetItemsAndSelection(projectType, project, itemType, "Assets");
        Require(SetProjectMode(projectType, project, twoColumn: true),
            "Project window has no TwoColumn state for folder semantics.");

        var commands = RenderProject(projectType, project, 1040, 620);
        var onlyFiles = FindLeftTreeText(commands, "OnlyFiles", 260);
        var nested = FindLeftTreeText(commands, "Nested", 260);
        Require(!commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                         command.Content == "Leaf.cs"),
            "The TwoColumn left pane leaked a file row instead of directories only.");
        Require(!HasFoldoutOnRow(commands, onlyFiles),
            "A directory whose direct children are files incorrectly exposes a foldout.");
        Require(HasFoldoutOnRow(commands, nested),
            "A directory with a child directory has no foldout.");
        Require(commands.Where(command => command.Type == GpuCanvasCommandType.Text)
                .All(command => !string.IsNullOrWhiteSpace(command.Content)),
            "Project rendering emitted an empty-name text node.");

        SetSelectedPath(projectType, project, "Assets/Scenes");
        var selectedScenes = RenderProject(projectType, project, 1040, 620);
        var breadcrumb = selectedScenes.Where(command => command.Type == GpuCanvasCommandType.Text &&
                                                          command.Content == "Assets" &&
                                                          command.Rect.Y >= 570)
            .OrderByDescending(command => command.Rect.Y)
            .FirstOrDefault();
        Require(breadcrumb.Type == GpuCanvasCommandType.Text,
            "Project footer does not expose Assets as an individually clickable breadcrumb.");
        var pointer = new Vector2((Fix64)(breadcrumb.Rect.X + breadcrumb.Rect.Width / 2),
            (Fix64)(breadcrumb.Rect.Y + breadcrumb.Rect.Height / 2));
        var mouseDown = new Event(EventType.MouseDown) { button = 0, mousePosition = pointer };
        _ = RenderProject(projectType, project, 1040, 620, mouseDown);
        Require(mouseDown.type == EventType.Used,
            "Project breadcrumb MouseDown was not consumed and can leak into the content below.");
        var mouseUp = new Event(EventType.MouseUp) { button = 0, mousePosition = pointer };
        _ = RenderProject(projectType, project, 1040, 620, mouseUp);
        Require(mouseUp.type == EventType.Used,
            "Project breadcrumb MouseUp was not consumed and can leak into the content below.");
        var navigated = RenderProject(projectType, project, 1040, 620);
        var dividerX = navigated.Where(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                                   command.Rect.Width is >= 0.9f and <= 2.1f &&
                                                   command.Rect.Height >= 400 &&
                                                   command.Rect.X is > 120 and < 700)
            .Select(command => command.Rect.X)
            .DefaultIfEmpty(float.MaxValue)
            .Min();
        Require(dividerX < float.MaxValue,
            "Assets breadcrumb navigation could not resolve the two-column divider.");
        var rightScenes = navigated.Where(command => command.Type == GpuCanvasCommandType.Text &&
                                                      command.Content == "Scenes" &&
                                                      command.Rect.X > dividerX)
            .ToArray();
        Require(rightScenes.Length > 0 &&
                navigated.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                         command.Content == "Scripts" &&
                                         command.Rect.X > dividerX),
            "Clicking the Assets breadcrumb did not navigate the right pane to Assets direct children.");
        Require(!navigated.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                          command.Content == "Main.scene.yaml"),
            "Assets breadcrumb navigation retained content from the previous Scenes folder.");
    }

    private static void VerifyFolderFirstNameOrdering()
    {
        var (projectType, itemType, _) = CreateProjectWindow();
        var directChildren = projectType.GetMethod("DirectChildren", StaticMembers) ??
                             throw new MissingMethodException(projectType.FullName, "DirectChildren");
        var ordered = ((System.Collections.IEnumerable)(directChildren.Invoke(null,
                           [CreateItems(itemType), "Assets/Scenes"]) ??
                       throw new InvalidOperationException("Project direct-child ordering returned null.")))
            .Cast<object>()
            .Select(item => (string)(itemType.GetProperty("EffectiveDisplayName", InstanceMembers)!
                .GetValue(item) ?? string.Empty))
            .ToArray();
        var expected = new[]
        {
            "AlphaFolder", "SubScenes", "ZetaFolder",
            "Lighting.material.yaml", "Main.scene.yaml", "Unnamed.asset"
        };
        Require(ordered.SequenceEqual(expected, StringComparer.Ordinal),
            $"Project entries are not folders-first with independent name ordering: {string.Join(", ", ordered)}");
    }

    private static void VerifyGuiContentTooltipPipeline()
    {
        const string tooltip = "Open the selected resource in its default editor";
        var source = new GUIContent(string.Empty, "Icons/Toolbar/OpenFolder.png", tooltip);
        var copy = new GUIContent(source);
        Require(copy.tooltip == tooltip && copy.image == source.image,
            "GUIContent copy construction lost its tooltip or image.");

        var hover = new Event(EventType.Repaint) { mousePosition = new Vector2(304, 108) };
        var first = RenderTooltip(hover, source, out var hoveredTooltip);
        Require(hoveredTooltip == tooltip,
            "GUI.tooltip did not expose the GUIContent tooltip under the pointer.");
        Require(!ContainsTooltip(first, tooltip), "GUIContent tooltip ignored the editor hover delay.");

        Thread.Sleep(700);
        var delayed = RenderTooltip(hover, source, out hoveredTooltip);
        Require(hoveredTooltip == tooltip, "GUI.tooltip was not stable while the pointer remained hovered.");
        var text = delayed.FirstOrDefault(command => command.Type == GpuCanvasCommandType.Text &&
                                                     command.Content == tooltip);
        Require(text.Type == GpuCanvasCommandType.Text,
            "Hovered GUIContent.tooltip was captured but never rendered by the GPU IMGUI host.");
        Require(text.Rect.X >= 0 && text.Rect.Y >= 0 && text.Rect.Right <= 320.1f &&
                text.Rect.Bottom <= 120.1f,
            "Tooltip text was not clamped to the editor-window bounds.");
        Require(delayed.Any(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                      command.Rect.X <= text.Rect.X && command.Rect.Y <= text.Rect.Y &&
                                      command.Rect.Right >= text.Rect.Right &&
                                      command.Rect.Bottom >= text.Rect.Bottom),
            "Rendered GUIContent.tooltip has no readable background panel.");

        var left = RenderTooltip(new Event(EventType.Repaint) { mousePosition = new Vector2(8, 8) }, source,
            out hoveredTooltip);
        Require(hoveredTooltip.Length == 0, "GUI.tooltip did not clear after pointer exit.");
        Require(!ContainsTooltip(left, tooltip), "GUIContent tooltip remained visible after pointer exit.");

        foreach (var invalidTooltip in new[] { string.Empty, "   \t" })
        {
            var invalid = RenderTooltip(hover,
                new GUIContent("No tooltip", EditorBuiltinIcons.Toolbar.Info, invalidTooltip),
                out hoveredTooltip);
            Require(string.IsNullOrWhiteSpace(hoveredTooltip),
                "An empty or whitespace-only GUIContent tooltip entered the hover pipeline.");
            Require(!invalid.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                            string.IsNullOrWhiteSpace(command.Content)),
                "An empty or whitespace-only tooltip produced a GPU text command.");
        }
    }

    private static Array CreateItems(Type itemType)
    {
        var definitions = new (string Path, string Name, string Source, string Type, bool Directory, bool Package)[]
        {
            ("Assets", "Assets", "C:/Project/Assets", "Folder", true, false),
            ("Assets/Scenes", "Scenes", "C:/Project/Assets/Scenes", "Folder", true, false),
            ("Assets/Scenes/Main.scene.yaml", "Main.scene.yaml", "C:/Project/Assets/Scenes/Main.scene.yaml", "Scene", false, false),
            ("Assets/Scenes/Lighting.material.yaml", "Lighting.material.yaml", "C:/Project/Assets/Scenes/Lighting.material.yaml", "Material", false, false),
            ("Assets/Scenes/ZetaFolder", "ZetaFolder", "C:/Project/Assets/Scenes/ZetaFolder", "Folder", true, false),
            ("Assets/Scenes/SubScenes", "SubScenes", "C:/Project/Assets/Scenes/SubScenes", "Folder", true, false),
            ("Assets/Scenes/SubScenes/Deep.scene.yaml", "Deep.scene.yaml", "C:/Project/Assets/Scenes/SubScenes/Deep.scene.yaml", "Scene", false, false),
            ("Assets/Scenes/AlphaFolder", "AlphaFolder", "C:/Project/Assets/Scenes/AlphaFolder", "Folder", true, false),
            ("Assets/Scenes/Unnamed.asset", "", "C:/Project/Assets/Scenes/Unnamed.asset", "Data", false, false),
            ("Assets/Scripts", "Scripts", "C:/Project/Assets/Scripts", "Folder", true, false),
            ("Assets/Scripts/Player.cs", "Player.cs", "C:/Project/Assets/Scripts/Player.cs", "Script", false, false),
            ("Assets/OnlyFiles", "OnlyFiles", "C:/Project/Assets/OnlyFiles", "Folder", true, false),
            ("Assets/OnlyFiles/Leaf.cs", "Leaf.cs", "C:/Project/Assets/OnlyFiles/Leaf.cs", "Script", false, false),
            ("Assets/Nested", "Nested", "C:/Project/Assets/Nested", "Folder", true, false),
            ("Assets/Nested/Child", "Child", "C:/Project/Assets/Nested/Child", "Folder", true, false),
            ("Assets/Readme.md", "Readme.md", "C:/Project/Assets/Readme.md", "Text", false, false),
            ("Packages", "Packages", "C:/Project/Packages", "Folder", true, true)
        };
        var items = Array.CreateInstance(itemType, definitions.Length);
        for (var index = 0; index < definitions.Length; index++)
        {
            var item = definitions[index];
            items.SetValue(Activator.CreateInstance(itemType, InstanceMembers, null,
                [item.Path, item.Name, item.Source, item.Type, item.Directory, item.Package, null, null, null], null),
                index);
        }
        return items;
    }

    private static bool SetProjectMode(Type type, object instance, bool twoColumn)
    {
        foreach (var member in CandidateMembers(type, "projectBrowserMode", "browserMode", "viewMode", "twoColumn"))
        {
            var memberType = GetMemberType(member);
            object value;
            if (memberType == typeof(bool)) value = twoColumn;
            else if (memberType == typeof(string)) value = twoColumn ? "TwoColumn" : "OneColumn";
            else if (memberType.IsEnum && Enum.GetNames(memberType).Any(name =>
                         name.Equals(twoColumn ? "TwoColumn" : "OneColumn", StringComparison.OrdinalIgnoreCase)))
                value = Enum.Parse(memberType, twoColumn ? "TwoColumn" : "OneColumn", true);
            else continue;
            SetMemberValue(member, instance, value);
            return true;
        }
        return false;
    }

    private static (Type ProjectType, Type ItemType, object Project) CreateProjectWindow()
    {
        var applicationType = typeof(EditorWindow).Assembly.GetTypes().FirstOrDefault(type =>
            type.GetNestedTypes(BindingFlags.NonPublic).Any(nested =>
                typeof(EditorWindow).IsAssignableFrom(nested) &&
                nested.GetMethod("CaptureLayout", InstanceMembers) is not null)) ??
            throw new InvalidOperationException("The GPU editor application type was not found.");
        var projectType = applicationType.GetNestedTypes(BindingFlags.NonPublic).Single(type =>
            typeof(EditorWindow).IsAssignableFrom(type) &&
            type.GetMethod("CaptureLayout", InstanceMembers) is not null &&
            type.GetMethod("ApplyLayout", InstanceMembers) is not null);
        var itemType = projectType.GetMethods(InstanceMembers)
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .First(type => type.GetProperty("VirtualPath", InstanceMembers) is not null &&
                           type.GetProperty("DisplayName", InstanceMembers) is not null);
        var project = Activator.CreateInstance(projectType, nonPublic: true) ??
                      throw new InvalidOperationException("The GPU Project window could not be created.");
        return (projectType, itemType, project);
    }

    private static void SetItemsAndSelection(Type projectType, object project, Type itemType, string selectedPath)
    {
        var cache = projectType.GetFields(InstanceMembers).FirstOrDefault(field =>
            field.FieldType.IsArray && field.FieldType.GetElementType() == itemType) ??
            throw new MissingMemberException(projectType.FullName, "project item cache");
        cache.SetValue(project, CreateItems(itemType));
        SetSelectedPath(projectType, project, selectedPath);
    }

    private static void SetSelectedPath(Type projectType, object project, string path)
    {
        var selected = CandidateMembers(projectType, "selectedPath", "selectionPath", "activePath")
            .FirstOrDefault(member => GetMemberType(member) == typeof(string)) ??
            throw new MissingMemberException(projectType.FullName, "selected project path");
        SetMemberValue(selected, project, path);
    }

    private static bool SetAssetScale(Type type, object instance, int size)
    {
        foreach (var member in CandidateMembers(type, "thumbnailSize", "assetScale", "iconSize", "zoom"))
        {
            var memberType = GetMemberType(member);
            object value;
            if (memberType == typeof(int)) value = size;
            else if (memberType == typeof(float)) value = (float)size;
            else if (memberType == typeof(double)) value = (double)size;
            else if (memberType == typeof(Fix64)) value = (Fix64)size;
            else continue;
            SetMemberValue(member, instance, value);
            return true;
        }
        return false;
    }

    private static IEnumerable<MemberInfo> CandidateMembers(Type type, params string[] names)
    {
        foreach (var name in names)
        {
            var field = type.GetField("_" + name, InstanceMembers) ?? type.GetField(name, InstanceMembers);
            if (field is not null) yield return field;
            var property = type.GetProperty(name, InstanceMembers) ??
                           type.GetProperty(char.ToUpperInvariant(name[0]) + name[1..], InstanceMembers);
            if (property is not null && property.CanWrite) yield return property;
        }
    }

    private static Type GetMemberType(MemberInfo member) => member switch
    {
        FieldInfo field => field.FieldType,
        PropertyInfo property => property.PropertyType,
        _ => throw new NotSupportedException(member.MemberType.ToString())
    };

    private static void SetMemberValue(MemberInfo member, object target, object value)
    {
        if (member is FieldInfo field) field.SetValue(target, value);
        else ((PropertyInfo)member).SetValue(target, value);
    }

    private static List<GpuCanvasCommand> RenderProject(Type type, object project, int width, int height,
        Event? current = null)
    {
        var commands = new List<GpuCanvasCommand>();
        InvokeBeginFrame(current ?? new Event(EventType.Repaint) { mousePosition = new Vector2(8, 8) }, width, height,
            commands);
        try
        {
            typeof(EditorWindow).GetMethod("OnGUIInternal", InstanceMembers)!.Invoke(project, null);
        }
        finally { InvokeEndFrame(); }
        return commands;
    }

    private static List<GpuCanvasCommand> RenderTooltip(Event current, GUIContent content, out string tooltip)
    {
        var commands = new List<GpuCanvasCommand>();
        InvokeBeginFrame(current, 320, 120, commands);
        try
        {
            GUI.Button(new Rect(280, 96, 36, 20), content);
            tooltip = GUI.tooltip;
        }
        finally { InvokeEndFrame(); }
        return commands;
    }

    private static void InvokeBeginFrame(Event current, int width, int height,
        List<GpuCanvasCommand> commands) => BeginFrame.Invoke(null, [current, width, height, commands]);

    private static void InvokeEndFrame() => EndFrame.Invoke(null, null);

    private static GpuCanvasCommand FindText(IEnumerable<GpuCanvasCommand> commands, string text, bool takeLast)
    {
        var matches = commands.Where(command => command.Type == GpuCanvasCommandType.Text &&
                                                command.Content == text).ToArray();
        Require(matches.Length > 0, $"Project presentation did not draw '{text}'.");
        return takeLast ? matches[^1] : matches[0];
    }

    private static bool ContainsTooltip(IEnumerable<GpuCanvasCommand> commands, string tooltip) =>
        commands.Any(command => command.Type == GpuCanvasCommandType.Text && command.Content == tooltip);

    private static bool HasVerticalDivider(IEnumerable<GpuCanvasCommand> commands, int height) =>
        commands.Any(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                command.Rect.Width is >= 0.9f and <= 2.1f &&
                                command.Rect.Height >= height - 160 &&
                                command.Rect.X is > 120 and < 700);

    private static GpuCanvasCommand FindLeftTreeText(
        IEnumerable<GpuCanvasCommand> commands, string text, float maximumX)
    {
        var match = commands.FirstOrDefault(command => command.Type == GpuCanvasCommandType.Text &&
                                                       command.Content == text && command.Rect.X < maximumX);
        Require(match.Type == GpuCanvasCommandType.Text, $"Project folder tree did not draw '{text}'.");
        return match;
    }

    private static bool HasFoldoutOnRow(IEnumerable<GpuCanvasCommand> commands, GpuCanvasCommand label) =>
        commands.Any(command => command.Type == GpuCanvasCommandType.Image &&
                                (command.Content is EditorBuiltinIcons.Toolbar.FoldoutClosed or
                                    EditorBuiltinIcons.Toolbar.FoldoutOpen) &&
                                command.Rect.X < label.Rect.X &&
                                command.Rect.Bottom >= label.Rect.Y &&
                                command.Rect.Y <= label.Rect.Bottom);

    private static void VerifyGridTilesHaveLabels(
        IReadOnlyList<GpuCanvasCommand> commands,
        float minimumX,
        int previewSize,
        params string[] expectedLabels)
    {
        var previews = commands.Where(command => command.Type == GpuCanvasCommandType.Image &&
                                                  command.Rect.X >= minimumX &&
                                                  Math.Abs(command.Rect.Width - previewSize) <= 2 &&
                                                  Math.Abs(command.Rect.Height - previewSize) <= 2)
            .ToArray();
        Require(previews.Length >= expectedLabels.Length,
            $"Project grid rendered only {previews.Length} tiles for {expectedLabels.Length} expected direct children.");
        foreach (var preview in previews)
            Require(commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                            !string.IsNullOrWhiteSpace(command.Content) &&
                                            command.Rect.Y >= preview.Rect.Bottom - 2 &&
                                            command.Rect.Y <= preview.Rect.Bottom + 32 &&
                                            command.Rect.Right >= preview.Rect.X &&
                                            command.Rect.X <= preview.Rect.Right),
                "A Project tile lost its non-empty label after zooming.");
        foreach (var label in expectedLabels)
            Require(commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                            command.Content == label),
                $"Project grid did not draw direct child label '{label}'.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
