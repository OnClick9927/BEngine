using BEngine.Editor;

namespace BEngine.UIElements.Editor;

public static class UIBuilderLibrary
{
    private static readonly UIBuilderLibraryItem[] Items =
    [
        Item("Fields", "Vector2 Field", EditorBuiltinIcons.Toolbar.Field, () => new Vector2Field("Vector2")),
        Item("Fields", "Vector4 Field", EditorBuiltinIcons.Toolbar.Field, () => new Vector4Field("Vector4")),
        Item("Fields", "Radio Button", EditorBuiltinIcons.Toolbar.Field, () => new RadioButton("Option")),
        Item("Fields", "Password Field", EditorBuiltinIcons.Toolbar.Field, () => new PasswordField("Password")),
        Item("Fields", "Text Area", EditorBuiltinIcons.Toolbar.Field, () => new TextArea("Notes")),
        Item("Controls", "Help Box", EditorBuiltinIcons.Toolbar.Info, () => new HelpBox("Helpful information")),
        Item("Controls", "Separator", EditorBuiltinIcons.Toolbar.Minus, () => new Separator()),
        Item("Toolbar", "Toolbar Toggle", EditorBuiltinIcons.Toolbar.Field, () => new ToolbarToggle("Enabled")),
        Item("Containers", "Visual Element", EditorBuiltinIcons.Toolbar.Container, () => new VisualElement()),
        Item("Containers", "Box", EditorBuiltinIcons.Toolbar.Container, () => new Box()),
        Item("Containers", "Scroll View", EditorBuiltinIcons.Toolbar.Container, () => new ScrollView()),
        Item("Containers", "Foldout", EditorBuiltinIcons.Toolbar.Container, () => new Foldout("Foldout")),
        Item("Containers", "Toolbar", EditorBuiltinIcons.Toolbar.Container, () => new Toolbar()),
        Item("Controls", "Label", EditorBuiltinIcons.Toolbar.Label, () => new Label("Label")),
        Item("Controls", "Button", EditorBuiltinIcons.Toolbar.Button, () => new Button(text: "Button")),
        Item("Controls", "Image", EditorBuiltinIcons.Assets.Image, () => new Image()),
        Item("Controls", "Progress Bar", EditorBuiltinIcons.Toolbar.Field,
            () => new ProgressBar { title = "Progress", value = 50 }),
        Item("Fields", "Text Field", EditorBuiltinIcons.Toolbar.Field, () => new TextField("Text")),
        Item("Fields", "Integer Field", EditorBuiltinIcons.Toolbar.Field, () => new IntegerField("Integer")),
        Item("Fields", "Float Field", EditorBuiltinIcons.Toolbar.Field, () => new FloatField("Float")),
        Item("Fields", "Toggle", EditorBuiltinIcons.Toolbar.Field, () => new Toggle("Toggle")),
        Item("Fields", "Slider", EditorBuiltinIcons.Toolbar.Field,
            () => new Slider("Slider", 0, 100) { value = 50 }),
        Item("Fields", "Dropdown", EditorBuiltinIcons.Toolbar.Field,
            () => new DropdownField("Dropdown", ["One", "Two", "Three"])),
        Item("Fields", "Color Field", EditorBuiltinIcons.Toolbar.Field, () => new ColorField("Color")),
        Item("Fields", "Search Field", EditorBuiltinIcons.Toolbar.Field, () => new SearchField()),
        Item("Collections", "List View", EditorBuiltinIcons.Toolbar.Container, () => new ListView
        {
            itemsSource = new object?[] { "One", "Two", "Three" }
        }),
        Item("Collections", "Tree View", EditorBuiltinIcons.Toolbar.Container, () => new TreeView
        {
            items = [new TreeViewItem(1, "Root", Children: [new TreeViewItem(2, "Child")])]
        }),
        Item("Toolbar", "Toolbar Button", EditorBuiltinIcons.Toolbar.Button,
            () => new ToolbarButton(text: "Action")),
        Item("Toolbar", "Toolbar Menu", EditorBuiltinIcons.Toolbar.Button, () => new ToolbarMenu("Menu")),
        Item("Toolbar", "Toolbar Search", EditorBuiltinIcons.Toolbar.Field, () => new ToolbarSearchField()),
        Item("Toolbar", "Toolbar Spacer", EditorBuiltinIcons.Toolbar.Container, () => new ToolbarSpacer())
    ];

    private static long _revision = -1;
    private static UIBuilderLibraryItem[] _all = Items;
    public static IReadOnlyList<UIBuilderLibraryItem> Standard => Items;
    public static IReadOnlyList<UIBuilderLibraryItem> Project => All.Skip(Items.Length).ToArray();
    public static IReadOnlyList<UIBuilderLibraryItem> All
    {
        get
        {
            if (_revision != UxmlFactoryRegistry.revision)
            {
                _all = [.. Items, .. UxmlFactoryRegistry.libraryElements.Select(descriptor => new UIBuilderLibraryItem(
                    descriptor.Category, descriptor.DisplayName, descriptor.Tooltip, EditorBuiltinIcons.Toolbar.Container,
                    () => UxmlFactoryRegistry.CreateDefault(descriptor.ElementName)))];
                _revision = UxmlFactoryRegistry.revision;
            }
            return _all;
        }
    }

    public static IEnumerable<UIBuilderLibraryItem> Filter(string? search) =>
        string.IsNullOrWhiteSpace(search)
            ? All
            : All.Where(item => item.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                item.Category.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                item.Tooltip.Contains(search, StringComparison.OrdinalIgnoreCase));

    private static UIBuilderLibraryItem Item(
        string category,
        string displayName,
        string iconPath,
        Func<VisualElement> factory) =>
        new(category, displayName, $"Add {displayName}", iconPath, factory);
}
