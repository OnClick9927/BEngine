using System.Reflection;
using BEngine.Editor;
using BEngine.Editor.Rendering;
using BEngine.Documents;

namespace BEngine.ExampleTests.GUISkinAssets;

internal static class Program
{
    private const float InspectorIndentWidth = 15f;
    private static readonly MethodInfo BeginFrame = typeof(GUI).GetMethod("BeginFrame",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo EndFrame = typeof(GUI).GetMethod("EndFrame",
        BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly string[] EditorStyleSlots = """
        label miniLabel largeLabel boldLabel miniBoldLabel centeredGreyMiniLabel wordWrappedMiniLabel
        wordWrappedLabel linkLabel whiteLabel whiteMiniLabel whiteLargeLabel whiteBoldLabel radioButton
        miniButton miniButtonLeft miniButtonMid miniButtonRight miniPullDown textField boldTextField textArea
        miniTextField numberField popup objectField objectFieldButton objectFieldThumb objectFieldMiniThumb
        colorField layerMaskField toggle toggleMixed foldout titlebarFoldout foldoutPreDrop foldoutHeader
        foldoutHeaderIcon optionsButtonStyle toggleGroup textFieldDropDown textFieldDropDownText overrideMargin
        toolbar contentToolbar toolbarButton toolbarButtonLeft toolbarButtonRight toolbarPopup toolbarPopupLeft
        toolbarPopupRight toolbarDropDownLeft toolbarDropDown toolbarDropDownRight toolbarDropDownToggle
        toolbarDropDownToggleButton toolbarDropDownToggleRight toolbarCreateAddNewDropDown toolbarTextField
        toolbarLabel inspectorDefaultMargins inspectorHorizontalDefaultMargins inspectorFullWidthMargins
        defaultContentMargins frameBox helpBox toolbarSearchField toolbarSearchFieldPopup
        toolbarSearchFieldWithJumpSynced toolbarSearchFieldWithJumpPopupSynced toolbarSearchFieldWithJump
        toolbarSearchFieldWithJumpPopup toolbarSearchFieldJumpButton toolbarSearchFieldCancelButton
        toolbarSearchFieldCancelButtonEmpty toolbarSearchFieldCancelButtonWithJump
        toolbarSearchFieldCancelButtonWithJumpEmpty colorPickerBox viewBackground inspectorBig inspectorTitlebar
        inspectorTitlebarFlat inspectorTitlebarText foldoutSelected iconButton tooltip notificationText
        notificationBackground assetLabel assetLabelPartial assetLabelIcon searchField searchFieldCancelButton
        searchFieldCancelButtonEmpty selectionRect toolbarSlider minMaxHorizontalSliderThumb dropDownList
        dropDownToggleButton minMaxStateDropdown progressBarBack progressBarBar progressBarText scrollViewAlt
        vectorAxisLabel centeredBoldLabel centeredMiniLabel dropDownButton colorPickerSwatch toolbarIconButton
        toolbarIconButtonSelected dockTab dockTabActive windowTitle menuItem menuItemDisabled treeViewRow
        treeViewRowSelected hierarchySceneHeader hierarchySceneHeaderActive hierarchyRow hierarchyRowSelected
        hierarchyRowInactive hierarchyAction statusBar separator
        """.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), $"BEngine-GUISkin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            VerifyEditorAssemblyContract();
            VerifyCompleteNamedStyles();
            VerifyCloneIsolation(root);
            VerifyAssetRoundTrip(root);
            VerifyInvalidAssetFallback(root);
            VerifyBuiltInReadOnly(root);
            Console.WriteLine("GUI_SKIN_ASSETS_OK|editor-assembly,basset,142-slots,public-editorstyles,texture-state,style-only-theme,custom-styles," +
                              "deep-clone,versioned-yaml,typed-loader,create-menu,icon,builtin-readonly," +
                              "builtin-foldout,stable-custom-foldout");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void VerifyEditorAssemblyContract()
    {
        Require(typeof(GUIStyle).Assembly == typeof(GUISkin).Assembly &&
                typeof(GUISkin).Assembly.GetName().Name == "BEngine.Editor",
            "GUIStyle and GUISkin must live in BEngine.Editor.");
        Require(typeof(BAsset).IsAssignableFrom(typeof(GUISkin)), "GUISkin is not a BAsset.");
        Require(typeof(GUIStyleState).GetProperty(nameof(GUIStyleState.backgroundImage))?.PropertyType ==
                typeof(Texture), "GUIStyleState.backgroundImage is not a nullable Texture reference.");
        var menu = typeof(GUISkin).GetCustomAttribute<CreateAssetMenuAttribute>();
        Require(menu is { menuName: "GUI/GUISkin" } && menu.fileName == "New GUI Skin",
            "GUISkin does not expose the expected CreateAssetMenu entry.");
        Require(typeof(GUISkin).GetCustomAttribute<EditorIconAttribute>() is not null,
            "GUISkin does not expose an editor asset icon.");
        Require(AssetTypeRegistry.Resolve($"Assets/Test{GUISkin.FileExtension}") == nameof(GUISkin) &&
                AssetTypeRegistry.ResolveAssetType($"Assets/Test{GUISkin.FileExtension}") == typeof(GUISkin),
            "The dedicated GUISkin file extension is not registered.");
    }

    private static void VerifyCompleteNamedStyles()
    {
        var skin = new GUISkin();
        Require(skin.styles.Count == 142, $"Expected 142 built-in GUI styles, found {skin.styles.Count}.");
        Require(skin.centeredBoldLabel.alignment == TextAnchor.MiddleCenter &&
                skin.centeredMiniLabel.alignment == TextAnchor.MiddleCenter,
            "Centered editor label styles are not owned by GUISkin.");
        Require(skin.toggle.normal.backgroundColor.Equals(skin.textField.normal.backgroundColor) &&
                skin.toggle.normal.borderColor.Equals(skin.textField.normal.borderColor) &&
                skin.button.normal.backgroundImage is Texture &&
                !skin.treeViewRowSelected.normal.backgroundColor.Equals(skin.treeViewRow.normal.backgroundColor),
            "A newly created GUISkin is not initialized as a complete usable theme.");
        foreach (var name in EditorStyleSlots)
            Require(ReferenceEquals(skin.GetStyle(name.ToUpperInvariant()), skin.FindStyle(name)),
                $"Editor style '{name}' is missing or lookup is case-sensitive.");
        var previous = GUI.skin;
        GUI.skin = skin;
        try
        {
            foreach (var name in EditorStyleSlots)
            {
                var property = typeof(EditorStyles).GetProperty(name,
                    BindingFlags.Public | BindingFlags.Static);
                Require(property?.PropertyType == typeof(GUIStyle) &&
                        ReferenceEquals(property.GetValue(null), skin.GetStyle(name)),
                    $"Public EditorStyles.{name} does not dynamically resolve the active GUISkin slot.");
            }
            var publicStyles = typeof(EditorStyles).GetProperties(BindingFlags.Public | BindingFlags.Static)
                .Where(static property => property.PropertyType == typeof(GUIStyle) &&
                                          property.GetCustomAttribute<ObsoleteAttribute>() is null)
                .ToArray();
            Require(publicStyles.Length == EditorStyleSlots.Length,
                $"Expected {EditorStyleSlots.Length} public non-obsolete EditorStyles GUIStyle getters, " +
                $"found {publicStyles.Length}.");
            foreach (var property in publicStyles)
            {
                var skinProperty = typeof(GUISkin).GetProperty(property.Name,
                    BindingFlags.Public | BindingFlags.Instance);
                Require(skinProperty?.PropertyType == typeof(GUIStyle) &&
                        ReferenceEquals(property.GetValue(null), skinProperty.GetValue(skin)),
                    $"Public EditorStyles.{property.Name} is not backed by GUISkin.{property.Name}.");
            }
            var structHeading = typeof(EditorStyles).GetProperty("structHeadingLabel",
                BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            Require(ReferenceEquals(structHeading, skin.label),
                "The obsolete structHeadingLabel alias does not dynamically resolve GUI.skin.label.");
        }
        finally
        {
            GUI.skin = previous;
        }
        Require(skin.FindStyle("horizontalScrollbarLeftButton") is not null &&
                skin.FindStyle("horizontalScrollbarRightButton") is not null &&
                skin.FindStyle("verticalScrollbarUpButton") is not null &&
                skin.FindStyle("verticalScrollbarDownButton") is not null &&
                skin.FindStyle("scrollView") is not null,
            "Scrollbar button or scroll view styles are missing.");

        var replacement = new GUIStyle("BUTTON") { fixedHeight = 91 };
        skin.AddStyle(replacement);
        Require(ReferenceEquals(skin.GetStyle("button"), replacement),
            "A custom style cannot override a named built-in style.");
        Require(skin.RemoveStyle("BuTtOn") && ReferenceEquals(skin.GetStyle("button"), skin.button),
            "Removing a custom override did not restore the built-in style.");
    }

    private static void VerifyCloneIsolation(string root)
    {
        var source = new GUISkin { name = "Source" };
        var pressed = CreateTexture(root, "Pressed.png");
        var cloneImage = CreateTexture(root, "Clone.png");
        source.button.active.backgroundImage = pressed;
        source.button.onFocused.borderColor = new Color(1, 0, 0, 1);
        source.linkLabel.normal.textColor = new Color(1, 0, 1, 1);
        source.AddStyle(new GUIStyle("package/control") { fixedWidth = 37 });
        var clone = source.Clone("Clone");
        clone.button.active.backgroundImage = cloneImage;
        clone.button.onFocused.borderColor = new Color(0, 1, 0, 1);
        clone.linkLabel.normal.textColor = new Color(0, 1, 1, 1);
        clone.GetStyle("package/control").fixedWidth = 99;
        Require(source.name == "Source" && clone.name == "Clone" &&
                ReferenceEquals(source.button.active.backgroundImage, pressed) &&
                ReferenceEquals(clone.button.active.backgroundImage, cloneImage) &&
                source.button.onFocused.borderColor.Equals(new Color(1, 0, 0, 1)) &&
                source.linkLabel.normal.textColor.Equals(new Color(1, 0, 1, 1)) &&
                source.GetStyle("package/control").fixedWidth == 37,
            "GUISkin.Clone shares mutable GUIStyle or GUIStyleState instances.");

        var copied = new GUISkin();
        EditorUtility.CopySerialized(source, copied);
        Require(ReferenceEquals(copied.button.active.backgroundImage, pressed) &&
                copied.button.onFocused.borderColor.Equals(new Color(1, 0, 0, 1)) &&
                copied.GetStyle("package/control").fixedWidth == 37,
            "Editor CopySerialized did not preserve nested GUIStyle data.");
    }

    private static void VerifyAssetRoundTrip(string root)
    {
        var path = Path.Combine(root, $"Ocean{GUISkin.FileExtension}");
        var skin = new GUISkin { name = "Internal Display Name" };
        skin.toolbar.fixedHeight = 31;
        skin.toolbar.hover.backgroundColor = new Color((Fix64).1f, (Fix64).2f, (Fix64).3f, 1);
        var checkedImage = CreateTexture(root, "Checked.png");
        skin.toggle.onNormal.backgroundImage = checkedImage;
        skin.selectionRect.normal.backgroundColor = new Color((Fix64).8f, (Fix64).4f, (Fix64).2f, 1);
        skin.AddStyle(new GUIStyle("package/special")
        {
            wordWrap = true,
            alignment = TextAnchor.MiddleCenter
        });
        skin.Save(path);

        var yaml = File.ReadAllText(path);
        Require(yaml.Contains("format: BEngine.GUISkin", StringComparison.Ordinal) &&
                yaml.Contains("version: 1", StringComparison.Ordinal),
            "GUISkin is not stored in the versioned asset format.");
        var loaded = GUISkin.Load(path);
        Require(loaded.name == "Ocean", $"GUISkin name round-trip failed: '{loaded.name}'.");
        Require(loaded.toolbar.fixedHeight == 31,
            $"GUISkin style metric round-trip failed: {loaded.toolbar.fixedHeight}.");
        Require(loaded.toolbar.hover.backgroundColor.Equals(skin.toolbar.hover.backgroundColor),
            "GUISkin style color round-trip failed.");
        Require(loaded.toggle.onNormal.backgroundImage is { } loadedImage &&
                loadedImage.assetPath.Equals(checkedImage.assetPath, StringComparison.OrdinalIgnoreCase),
            $"GUISkin Texture reference round-trip failed: '{loaded.toggle.onNormal.backgroundImage?.assetPath}'.");
        Require(loaded.selectionRect.normal.backgroundColor.Equals(
                skin.selectionRect.normal.backgroundColor),
            "GUISkin GUIStyle state round-trip failed.");
        Require(loaded.GetStyle("PACKAGE/SPECIAL").wordWrap,
            "GUISkin custom style round-trip failed.");
        var referenced = BAsset.Load<GUISkin>(path);
        Require(referenced is not null && referenced.assetPath.Length > 0 &&
                referenced.GetStyle("package/special").alignment == TextAnchor.MiddleCenter,
            "BAsset.Load<GUISkin> did not use the typed GUISkin loader.");
        var registryLoaded = AssetTypeRegistry.Load(new AssetLoadContext(Guid.NewGuid(),
            "Assets/Ocean.guiskin.yaml", path, nameof(GUISkin)));
        Require(registryLoaded is GUISkin, "AssetTypeRegistry did not load a GUISkin asset.");
    }

    private static void VerifyInvalidAssetFallback(string root)
    {
        var assets = Path.Combine(root, "Assets");
        Directory.CreateDirectory(assets);
        var invalidPath = Path.Combine(assets, $"Broken{GUISkin.FileExtension}");
        File.WriteAllText(invalidPath, "format: BEngine.GUISkin\nversion: 1\nstyles:\n  broken: [\n");
        var previousDataPath = Application.dataPath;
        var previousEditorDataPath = Environment.GetEnvironmentVariable("BENGINE_EDITOR_DATA_PATH");
        var setDataPath = typeof(Application).GetProperty(nameof(Application.dataPath))!
            .GetSetMethod(nonPublic: true)!;
        try
        {
            setDataPath.Invoke(null, [assets]);
            Environment.SetEnvironmentVariable("BENGINE_EDITOR_DATA_PATH", Path.Combine(root, "EditorData"));
            var preferences = new BEngine.Editor.Documents.EditorPreferencesDocument
            {
                EditorTheme = nameof(EditorTheme.Custom),
                EditorSkin = $"Assets/Broken{GUISkin.FileExtension}"
            };
            preferences.Save(EditorDataPaths.preferencesPath);
            EditorPreferences.Initialize();
            Require(EditorAppearance.activeSkin.name == nameof(EditorTheme.Dark) &&
                    EditorPreferences.current.EditorSkin == "builtin:Dark" &&
                    Document.Load<BEngine.Editor.Documents.EditorPreferencesDocument>(
                        EditorDataPaths.preferencesPath).EditorSkin == "builtin:Dark",
                "An invalid active GUISkin did not fall back to the built-in Dark skin.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("BENGINE_EDITOR_DATA_PATH", previousEditorDataPath);
            setDataPath.Invoke(null, [previousDataPath]);
            EditorAppearance.Apply(new BEngine.Editor.Documents.EditorPreferencesDocument());
        }
    }

    private static void VerifyBuiltInReadOnly(string root)
    {
        var builtIn = new GUISkin();
        builtIn.button.fixedWidth = 41;
        typeof(GUISkin).GetMethod("MarkBuiltIn", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(builtIn, ["Dark"]);
        Require(builtIn.isBuiltIn && builtIn.isReadOnly && builtIn.name == "Dark" &&
                (builtIn.hideFlags & HideFlags.NotEditable) != 0,
            "Built-in skin state is not stable or read-only.");
        var path = Path.Combine(root, $"Forbidden{GUISkin.FileExtension}");
        try
        {
            builtIn.Save(path);
            throw new InvalidOperationException("A built-in GUISkin was saved.");
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("read-only",
                   StringComparison.OrdinalIgnoreCase)) { }
        using var builtInEditor = BEngine.Editor.Editor.CreateEditor(builtIn);
        Require(builtInEditor is GUISkinEditor,
            "Built-in and custom GUISkin selections do not use GUISkinEditor.");
        var collapsed = RenderEditor(builtInEditor);
        ClickEditorControl(builtInEditor, FindText(collapsed, nameof(GUISkin.button)).Rect);
        var expanded = RenderEditor(builtInEditor);
        Require(expanded.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                        command.Content == "Fixed Width"),
            "A built-in GUISkin style foldout could not be expanded.");

        var widthValue = FindText(expanded, "41");
        DispatchEditor(builtInEditor, new Event(EventType.MouseDown)
        {
            mousePosition = Center(widthValue.Rect), button = 0, clickCount = 2
        });
        DispatchEditor(builtInEditor, new Event(EventType.KeyDown) { character = '9' });
        Require(builtIn.button.fixedWidth == 41,
            "A leaf GUIStyle value on a built-in GUISkin was editable.");

        var stateHeader = FindText(RenderEditor(builtInEditor), "Normal");
        ClickEditorControl(builtInEditor, stateHeader.Rect);
        Require(RenderEditor(builtInEditor).Any(command => command.Type == GpuCanvasCommandType.Text &&
                                                          command.Content == "Background Image"),
            "A built-in GUISkin state foldout could not be expanded.");

        VerifyStyleHierarchyIndentation(builtIn, nameof(GUISkin.button));
        VerifyStyleHierarchyIndentation(builtIn, nameof(GUISkin.label));

        var custom = new GUISkin
        {
            customStyles = [new GUIStyle("Original Stable Name")]
        };
        using var customEditor = BEngine.Editor.Editor.CreateEditor(custom);
        var customCollapsed = RenderEditor(customEditor);
        ClickEditorControl(customEditor, FindText(customCollapsed, "Original Stable Name").Rect);
        Require(RenderEditor(customEditor).Any(command => command.Type == GpuCanvasCommandType.Text &&
                                                        command.Content == "Name"),
            "A custom GUIStyle foldout could not be expanded.");
        custom.customStyles[0].name = "Renamed While Expanded";
        Require(RenderEditor(customEditor).Any(command => command.Type == GpuCanvasCommandType.Text &&
                                                        command.Content == "Name"),
            "Renaming a custom GUIStyle discarded its foldout state.");
    }

    private static void VerifyStyleHierarchyIndentation(GUISkin skin, string styleName)
    {
        using var editor = BEngine.Editor.Editor.CreateEditor(skin);
        var collapsed = RenderEditor(editor);
        var styleHeader = FindText(collapsed, styleName);
        ClickEditorControl(editor, styleHeader.Rect);

        var styleExpanded = RenderEditor(editor);
        var normalHeader = FindText(styleExpanded, "Normal");
        var hoverHeader = FindText(styleExpanded, "Hover");
        RequireChildIndent(styleHeader, normalHeader, $"{styleName}/Normal");
        RequireChildIndent(styleHeader, hoverHeader, $"{styleName}/Hover");

        ClickEditorPoint(editor, new Vector2(4,
            (Fix64)((normalHeader.Rect.Y + normalHeader.Rect.Bottom) * 0.5f)));
        var afterLeftMarginClick = RenderEditor(editor);
        Require(!afterLeftMarginClick.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                                     command.Content == "Text Color"),
            $"The indented '{styleName}/Normal' foldout still accepted clicks in its former left margin.");

        normalHeader = FindText(afterLeftMarginClick, "Normal");
        ClickEditorControl(editor, normalHeader.Rect);
        var stateExpanded = RenderEditor(editor);
        var expandedNormalHeader = FindText(stateExpanded, "Normal");
        var textColor = FindText(stateExpanded, "Text Color");
        RequireChildIndent(expandedNormalHeader, textColor, $"{styleName}/Normal/Text Color");
    }

    private static void RequireChildIndent(GpuCanvasCommand parent, GpuCanvasCommand child, string path)
    {
        var actual = child.Rect.X - parent.Rect.X;
        Require(Math.Abs(actual - InspectorIndentWidth) <= 0.1f,
            $"GUISkin Inspector child '{path}' moved {actual:0.###}px from its parent instead of " +
            $"one {InspectorIndentWidth:0}px indent unit.");
    }

    private static List<GpuCanvasCommand> RenderEditor(BEngine.Editor.Editor editor)
    {
        var commands = new List<GpuCanvasCommand>();
        DispatchEditor(editor, new Event(EventType.Repaint), commands);
        return commands;
    }

    private static void ClickEditorControl(BEngine.Editor.Editor editor, GpuCanvasRect rect)
    {
        ClickEditorPoint(editor, Center(rect));
    }

    private static void ClickEditorPoint(BEngine.Editor.Editor editor, Vector2 position)
    {
        DispatchEditor(editor, new Event(EventType.MouseDown)
        {
            mousePosition = position, button = 0, clickCount = 1
        });
        DispatchEditor(editor, new Event(EventType.MouseUp)
        {
            mousePosition = position, button = 0, clickCount = 1
        });
    }

    private static void DispatchEditor(BEngine.Editor.Editor editor, Event evt,
        List<GpuCanvasCommand>? commands = null)
    {
        BeginFrame.Invoke(null, [evt, 720, 12000, commands ?? []]);
        try { editor.OnInspectorGUI(); }
        finally { EndFrame.Invoke(null, null); }
    }

    private static GpuCanvasCommand FindText(IEnumerable<GpuCanvasCommand> commands, string content) =>
        commands.FirstOrDefault(command => command.Type == GpuCanvasCommandType.Text &&
                                           command.Content == content) is { Rect.Width: > 0 } found
            ? found
            : throw new InvalidOperationException($"GUISkin Inspector text '{content}' was not rendered.");

    private static Vector2 Center(GpuCanvasRect rect) => new(
        (Fix64)((rect.X + rect.Right) * 0.5f),
        (Fix64)((rect.Y + rect.Bottom) * 0.5f));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static Texture CreateTexture(string root, string fileName)
    {
        var path = Path.Combine(root, fileName);
        File.WriteAllBytes(path, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        return BAsset.Load<Texture>(path) ??
               throw new InvalidOperationException($"Could not load Texture fixture '{path}'.");
    }
}
