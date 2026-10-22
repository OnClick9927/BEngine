using System.Globalization;

namespace BEngine.UIElements;

internal static class AccessibilityTreeBuilder
{
    internal static AccessibilitySnapshot Build(
        VisualElement root,
        UIRenderCommandList renderList,
        long revision)
    {
        var nodes = new List<AccessibilityNode>();
        Visit(root, parentId: null, isRoot: true);
        return new AccessibilitySnapshot(revision, nodes);

        void Visit(VisualElement element, int? parentId, bool isRoot = false)
        {
            if (element.accessibilityHidden || !element.visible || element.style.display == DisplayStyle.None)
                return;
            var role = ResolveRole(element, isRoot);
            var include = role != AccessibilityRole.Auto;
            var nextParent = parentId;
            if (include)
            {
                renderList.TryGetVisibleRect(element, out var bounds);
                var state = ResolveState(element);
                var (minimum, maximum) = ResolveRange(element);
                nodes.Add(new AccessibilityNode(
                    element.accessibilityId,
                    parentId,
                    role,
                    ResolveName(element),
                    element.accessibilityDescription.Length > 0
                        ? element.accessibilityDescription
                        : element.tooltip,
                    ResolveValue(element),
                    state,
                    bounds,
                    minimum,
                    maximum,
                    element.accessibilityLiveRegion));
                nextParent = element.accessibilityId;
            }
            foreach (var child in element.Children) Visit(child, nextParent);
        }
    }

    internal static AccessibilityRole ResolveRole(VisualElement element, bool isRoot = false)
    {
        if (element.accessibilityRole != AccessibilityRole.Auto) return element.accessibilityRole;
        return element switch
        {
            RadioButton => AccessibilityRole.RadioButton,
            Toggle => AccessibilityRole.Toggle,
            SearchField => AccessibilityRole.SearchField,
            TextField => AccessibilityRole.TextField,
            Slider => AccessibilityRole.Slider,
            DropdownField => AccessibilityRole.ComboBox,
            ToolbarMenu => AccessibilityRole.Menu,
            Button => AccessibilityRole.Button,
            Label => AccessibilityRole.Text,
            Image => AccessibilityRole.Image,
            ProgressBar => AccessibilityRole.ProgressBar,
            ListView or TreeView => AccessibilityRole.List,
            ScrollView => AccessibilityRole.ScrollView,
            _ when isRoot => AccessibilityRole.Group,
            _ when element.accessibilityName.Length > 0 => AccessibilityRole.Group,
            _ => AccessibilityRole.Auto
        };
    }

    private static string ResolveName(VisualElement element)
    {
        if (element.accessibilityName.Length > 0) return element.accessibilityName;
        var name = element switch
        {
            Button button => button.text,
            Toggle toggle => toggle.label,
            TextField field => field.label.Length > 0 ? field.label : field.placeholderText,
            Slider slider => slider.label,
            DropdownField dropdown => dropdown.label,
            FloatField field => field.label,
            IntegerField field => field.label,
            ColorField field => field.label,
            Vector2Field field => field.label,
            Vector4Field field => field.label,
            Label label => label.text,
            ProgressBar progress => progress.title,
            _ => string.Empty
        };
        return name.Length > 0 ? name : element.name;
    }

    private static string ResolveValue(VisualElement element)
    {
        if (element.accessibilityValue.Length > 0) return element.accessibilityValue;
        return element switch
        {
            TextField { isPasswordField: true } => string.Empty,
            TextField field => field.value,
            FloatField field => field.value.ToString(CultureInfo.InvariantCulture),
            IntegerField field => field.value.ToString(CultureInfo.InvariantCulture),
            Toggle field => field.value.ToString(CultureInfo.InvariantCulture),
            Slider field => field.value.ToString(CultureInfo.InvariantCulture),
            DropdownField field => field.value,
            ProgressBar progress => progress.value.ToString(CultureInfo.InvariantCulture),
            Label label => label.text,
            ListView list when list.selectedItems.Count > 0 =>
                string.Join(", ", list.selectedItems.Select(item => item?.ToString() ?? string.Empty)),
            _ => string.Empty
        };
    }

    internal static string ResolveAnnouncement(VisualElement element)
    {
        var value = ResolveValue(element);
        return value.Length > 0 ? value : ResolveName(element);
    }

    private static AccessibilityState ResolveState(VisualElement element)
    {
        var state = AccessibilityState.None;
        if (!element.enabledInHierarchy) state |= AccessibilityState.Disabled;
        if (element.focused) state |= AccessibilityState.Focused;
        if (element is Toggle { value: true }) state |= AccessibilityState.Checked;
        if (element is ListView { selectedIndices.Count: > 0 }) state |= AccessibilityState.Selected;
        if (element is TextField { isPasswordField: true }) state |= AccessibilityState.Password;
        if (element is TextField { isReadOnly: true } or Slider { isReadOnly: true } or
            Toggle { isReadOnly: true } or DropdownField { isReadOnly: true } or
            FloatField { isReadOnly: true } or IntegerField { isReadOnly: true })
            state |= AccessibilityState.ReadOnly;
        return state;
    }

    private static (float? Minimum, float? Maximum) ResolveRange(VisualElement element) => element switch
    {
        Slider slider => (slider.lowValue, slider.highValue),
        ProgressBar progress => (progress.lowValue, progress.highValue),
        _ => (null, null)
    };
}
