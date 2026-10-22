namespace BEngine.UIElements;

public enum AccessibilityRole
{
    Auto,
    Group,
    Text,
    Heading,
    Button,
    Toggle,
    RadioButton,
    TextField,
    SearchField,
    Slider,
    ComboBox,
    Image,
    ProgressBar,
    List,
    ListItem,
    ScrollView,
    Menu,
    MenuItem,
    Link
}

public enum AccessibilityLiveRegion
{
    Off,
    Polite,
    Assertive
}

[Flags]
public enum AccessibilityState
{
    None = 0,
    Disabled = 1 << 0,
    Focused = 1 << 1,
    Checked = 1 << 2,
    Selected = 1 << 3,
    ReadOnly = 1 << 4,
    Password = 1 << 5
}

public enum AccessibilityAction
{
    Focus,
    Activate,
    Increment,
    Decrement
}

public enum AccessibilityEventKind
{
    HierarchyChanged,
    NodeChanged,
    FocusChanged,
    Announcement
}
