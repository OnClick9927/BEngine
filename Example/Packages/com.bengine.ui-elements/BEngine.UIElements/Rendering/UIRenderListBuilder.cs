namespace BEngine.UIElements;

public static class UIRenderListBuilder
{
    public static UIRenderCommandList Build(
        VisualElement root,
        int viewportWidth,
        int viewportHeight,
        Fix64? scale = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        viewportWidth = Math.Max(1, viewportWidth);
        viewportHeight = Math.Max(1, viewportHeight);
        var layouts = UIElementLayoutEngine.Calculate(root, viewportWidth, viewportHeight, scale);
        var layoutByElement = layouts.ToDictionary(layout => layout.Element, layout => layout.Rect);
        var commands = new List<UIRenderCommand>(layouts.Count * 2);
        var hitRegions = new List<UIElementLayout>(layouts.Count);
        var viewport = new UIElementRect(0, 0, viewportWidth, viewportHeight);
        BuildElement(root, viewport, layoutByElement, commands, hitRegions);
        return new UIRenderCommandList(layouts, commands, hitRegions);
    }

    private static void BuildElement(
        VisualElement element,
        UIElementRect inheritedClip,
        IReadOnlyDictionary<VisualElement, UIElementRect> layouts,
        ICollection<UIRenderCommand> commands,
        ICollection<UIElementLayout> hitRegions)
    {
        if (!layouts.TryGetValue(element, out var rect)) return;
        var elementClip = Intersect(inheritedClip, rect);
        if (HasArea(elementClip)) hitRegions.Add(new UIElementLayout(element, elementClip));
        if (TryBuildField(element, rect, elementClip, commands) ||
            TryBuildCollection(element, rect, elementClip, commands))
            return;
        var background = ResolveBackground(element);
        if (background.A > 0 && HasArea(elementClip))
            commands.Add(new UIRenderCommand(
                UIRenderCommandType.SolidRect, element, rect, inheritedClip, background));
        AddBorder(commands, element, rect, elementClip);
        if (element.focused)
            AddRectBorder(commands, element, rect, elementClip, Fix64.One, new UIColor(58, 121, 178));

        if (element is Image image && !string.IsNullOrWhiteSpace(image.sourcePath) && HasArea(elementClip))
            commands.Add(new UIRenderCommand(
                UIRenderCommandType.Image, element, rect, elementClip, UIColor.FromRgb(255, 255, 255),
                image.sourcePath));

        var text = ResolveText(element);
        if (!string.IsNullOrWhiteSpace(text) && HasArea(elementClip))
            commands.Add(new UIRenderCommand(
                UIRenderCommandType.Text, element, rect, elementClip, ResolveTextColor(element), text,
                element.style.fontSize > 0 ? element.style.fontSize : 14));

        var childClip = element.style.overflow == Overflow.Visible ? inheritedClip : elementClip;
        foreach (var child in element.Children) BuildElement(child, childClip, layouts, commands, hitRegions);
    }

    private static bool TryBuildField(
        VisualElement element,
        UIElementRect rect,
        UIElementRect clip,
        ICollection<UIRenderCommand> commands)
    {
        if (element is ProgressBar progress)
        {
            var range = progress.highValue - progress.lowValue;
            var normalized = Math.Abs(range) <= float.Epsilon
                ? 0 : Math.Clamp((progress.value - progress.lowValue) / range, 0, 1);
            AddOutlinedRect(commands, element, rect, clip,
                element.style.backgroundColor ?? new UIColor(42, 42, 42),
                element.style.borderColor ?? new UIColor(29, 29, 29));
            commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element,
                new UIElementRect(rect.X + 1, rect.Y + 1,
                    Fix64.Max(0, (rect.Width - 2) * (Fix64)normalized), Fix64.Max(0, rect.Height - 2)),
                clip, new UIColor(45, 93, 135)));
            AddText(commands, element, rect, clip, string.IsNullOrWhiteSpace(progress.title)
                ? $"{progress.value:0.#}" : progress.title);
            return true;
        }

        if (element is Toggle toggle)
        {
            var boxSize = Fix64.Min(14, Fix64.Max(0, rect.Height - 6));
            var box = new UIElementRect(rect.X + 2, rect.Y + (rect.Height - boxSize) / 2, boxSize, boxSize);
            AddOutlinedRect(commands, element, box, clip, toggle.value ? new UIColor(70, 122, 170) : new UIColor(62, 62, 62),
                new UIColor(29, 29, 29));
            if (toggle.value)
                AddText(commands, element, new UIElementRect(box.X - 1, box.Y - 2, box.Width + 4, box.Height + 4), clip, "v");
            AddText(commands, element, new UIElementRect(rect.X + boxSize + 7, rect.Y,
                Fix64.Max(0, rect.Width - boxSize - 7), rect.Height), clip, toggle.label);
            return true;
        }

        var editingValue = element.textEditingValue;
        var field = element switch
        {
            TextField value => (value.label, value.isPasswordField
                ? new string(value.maskCharacter, (editingValue ?? value.value).Length)
                : editingValue ?? value.value),
            FloatField value => (value.label, editingValue ??
                value.value.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            IntegerField value => (value.label, editingValue ??
                value.value.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ColorField value => (value.label, FormatColor(value.value, value.showAlpha)),
            Slider value => (value.label, value.value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)),
            DropdownField value => (value.label, value.value),
            _ => ((string Label, string Value)?)null
        };
        if (field is null) return false;

        var labelWidth = string.IsNullOrWhiteSpace(field.Value.Label) ? Fix64.Zero : rect.Width * (Fix64)0.4;
        var labelRect = new UIElementRect(rect.X, rect.Y, labelWidth, rect.Height);
        var valueRect = new UIElementRect(rect.X + labelWidth, rect.Y,
            Fix64.Max(0, rect.Width - labelWidth), rect.Height);
        if (labelWidth > 0) AddText(commands, element, labelRect, clip, field.Value.Label);
        var fieldBackground = element.style.backgroundColor ??
                              (element.isReadOnlyField() ? new UIColor(47, 47, 47) : new UIColor(42, 42, 42));
        if (element.hovered && !element.isReadOnlyField()) fieldBackground = Lighten(fieldBackground, 7);
        AddOutlinedRect(commands, element, valueRect, clip, fieldBackground,
            element.focused ? new UIColor(58, 121, 178) : element.style.borderColor ?? new UIColor(29, 29, 29));
        if (element is ColorField color)
        {
            var swatch = new UIElementRect(valueRect.X + 3, valueRect.Y + 3,
                Fix64.Min(36, Fix64.Max(0, valueRect.Width - 6)), Fix64.Max(0, valueRect.Height - 6));
            AddCheckerboard(commands, element, swatch, clip);
            commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element, swatch, clip,
                ToUiColor(color.value)));
            AddText(commands, element,
                new UIElementRect(swatch.X + swatch.Width + 5, valueRect.Y,
                    Fix64.Max(0, valueRect.Width - swatch.Width - 8), valueRect.Height),
                clip, field.Value.Value);
        }
        else if (element is Slider slider)
        {
            var range = slider.highValue - slider.lowValue;
            var t = range <= float.Epsilon ? 0 : Math.Clamp((slider.value - slider.lowValue) / range, 0, 1);
            commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element,
                new UIElementRect(valueRect.X, valueRect.Y, valueRect.Width * (Fix64)t, valueRect.Height), clip,
                new UIColor(45, 93, 135)));
            AddText(commands, element, valueRect, clip, field.Value.Value);
        }
        else if (element is SearchField search)
        {
            var iconWidth = Fix64.Min(20, valueRect.Width);
            AddText(commands, element,
                new UIElementRect(valueRect.X + 2, valueRect.Y, iconWidth, valueRect.Height), clip, "\u2315");
            var displayValue = field.Value.Value;
            var clearWidth = search.showClearButton && !string.IsNullOrEmpty(displayValue)
                ? Fix64.Min(20, valueRect.Width) : Fix64.Zero;
            var contentRect = new UIElementRect(valueRect.X + iconWidth, valueRect.Y,
                Fix64.Max(0, valueRect.Width - iconWidth - clearWidth), valueRect.Height);
            if (string.IsNullOrEmpty(displayValue) && !search.isTextEditing && !search.focused)
                commands.Add(new UIRenderCommand(UIRenderCommandType.Text, element, contentRect, clip,
                    new UIColor(125, 125, 125), search.placeholderText,
                    element.style.fontSize > 0 ? element.style.fontSize : 14));
            else
            {
                AddTextSelection(commands, element, contentRect, clip, displayValue);
                AddText(commands, element, contentRect, clip, displayValue);
            }
            AddTextCaret(commands, element, contentRect, clip, displayValue);
            if (clearWidth > 0)
                AddText(commands, element, new UIElementRect(valueRect.X + valueRect.Width - clearWidth,
                    valueRect.Y, clearWidth, valueRect.Height), clip, "x");
        }
        else
        {
            if (element is TextField or FloatField or IntegerField)
                AddTextSelection(commands, element, valueRect, clip, field.Value.Value);
            AddText(commands, element, valueRect, clip, field.Value.Value);
            if (element is TextField or FloatField or IntegerField)
                AddTextCaret(commands, element, valueRect, clip, field.Value.Value);
            if (element is DropdownField)
                AddText(commands, element, new UIElementRect(valueRect.X + valueRect.Width - 18, valueRect.Y,
                    18, valueRect.Height), clip, "v");
        }
        return true;
    }

    private static void AddTextSelection(
        ICollection<UIRenderCommand> commands,
        VisualElement element,
        UIElementRect contentRect,
        UIElementRect clip,
        string displayText)
    {
        if (!element.isTextEditing || element.isReadOnlyField()) return;
        var start = Math.Clamp(Math.Min(element.textEditingSelectionStart,
            element.textEditingSelectionEnd), 0, displayText.Length);
        var end = Math.Clamp(Math.Max(element.textEditingSelectionStart,
            element.textEditingSelectionEnd), 0, displayText.Length);
        if (end <= start) return;

        var fontSize = element.style.fontSize > 0 ? element.style.fontSize : 14;
        var left = contentRect.X + 4 + EstimateTextWidth(displayText.AsSpan(0, start), fontSize);
        var right = contentRect.X + 4 + EstimateTextWidth(displayText.AsSpan(0, end), fontSize);
        var contentRight = contentRect.X + Fix64.Max(0, contentRect.Width - 2);
        left = Fix64.Min(left, contentRight);
        right = Fix64.Min(right, contentRight);
        var selectionRect = new UIElementRect(left, contentRect.Y + 2,
            Fix64.Max(0, right - left), Fix64.Max(0, contentRect.Height - 4));
        var selectionClip = Intersect(clip, contentRect);
        if (HasArea(selectionRect) && HasArea(selectionClip))
            commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element, selectionRect,
                selectionClip, new UIColor(45, 93, 135)));
    }

    private static void AddTextCaret(
        ICollection<UIRenderCommand> commands,
        VisualElement element,
        UIElementRect contentRect,
        UIElementRect clip,
        string displayText)
    {
        if (!element.isTextEditing || !element.textEditingCaretVisible || element.isReadOnlyField()) return;

        var index = Math.Clamp(element.textEditingCaretIndex, 0, displayText.Length);
        var prefix = displayText.AsSpan(0, index);
        var lastLineBreak = prefix.LastIndexOf('\n');
        if (lastLineBreak >= 0) prefix = prefix[(lastLineBreak + 1)..];
        var fontSize = element.style.fontSize > 0 ? element.style.fontSize : 14;
        var x = contentRect.X + 4 + EstimateTextWidth(prefix, fontSize);
        var maxX = contentRect.X + Fix64.Max(1, contentRect.Width - 2);
        x = Fix64.Min(x, maxX);
        var caretRect = new UIElementRect(
            x,
            contentRect.Y + 3,
            Fix64.One,
            Fix64.Max(0, contentRect.Height - 6));
        var caretClip = Intersect(clip, contentRect);
        if (HasArea(caretRect) && HasArea(caretClip))
            commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element, caretRect,
                caretClip, ResolveTextColor(element)));
    }

    private static Fix64 EstimateTextWidth(ReadOnlySpan<char> text, float fontSize)
    {
        double units = 0;
        foreach (var character in text)
        {
            units += character switch
            {
                '\t' => 2.2,
                ' ' => 0.35,
                'i' or 'l' or 'I' or '.' or ',' or ':' or ';' or '!' or '|' or '\'' => 0.3,
                'm' or 'w' or 'M' or 'W' or '@' => 0.85,
                _ when character >= 0x2E80 => 1.0,
                _ when char.IsUpper(character) => 0.62,
                _ => 0.55
            };
        }
        return (Fix64)(units * fontSize * 0.62);
    }

    private static bool TryBuildCollection(
        VisualElement element,
        UIElementRect rect,
        UIElementRect clip,
        ICollection<UIRenderCommand> commands)
    {
        if (element is ListView list)
        {
            commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element, rect, clip,
                element.style.backgroundColor ?? new UIColor(56, 56, 56)));
            if (list.showBorder) AddRectBorder(commands, element, rect, clip, Fix64.One,
                element.style.borderColor ?? new UIColor(29, 29, 29));
            var rowHeight = (Fix64)list.fixedItemHeight;
            var first = Math.Max(0, (int)(list.scrollOffset / list.fixedItemHeight));
            var visible = Math.Max(1, (int)Math.Ceiling((double)rect.Height / list.fixedItemHeight) + 1);
            for (var index = first; index < Math.Min(list.itemsSource.Count, first + visible); index++)
            {
                var y = rect.Y + index * rowHeight - (Fix64)list.scrollOffset;
                var row = new UIElementRect(rect.X, y, rect.Width, rowHeight);
                var rowClass = list.makeItemClass(list.itemsSource[index]);
                if (list.selectedIndices.Contains(index))
                    commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element, row, clip,
                        new UIColor(45, 93, 135)));
                else if (rowClass.Contains("error", StringComparison.OrdinalIgnoreCase))
                    commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element, row, clip,
                        new UIColor(83, 40, 40)));
                else if (rowClass.Contains("warning", StringComparison.OrdinalIgnoreCase))
                    commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element, row, clip,
                        new UIColor(78, 67, 39)));
                else if (list.showAlternatingRowBackgrounds && index % 2 == 1)
                    commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element, row, clip,
                        new UIColor(52, 52, 52)));
                var icon = list.makeItemIcon(list.itemsSource[index]);
                var textRect = row;
                if (!string.IsNullOrWhiteSpace(icon))
                {
                    var iconRect = new UIElementRect(row.X + 3, row.Y + 2,
                        Fix64.Max(0, rowHeight - 4), Fix64.Max(0, rowHeight - 4));
                    commands.Add(new UIRenderCommand(UIRenderCommandType.Image, element, iconRect, clip,
                        UIColor.FromRgb(255, 255, 255), icon));
                    textRect = new UIElementRect(row.X + rowHeight + 3, row.Y,
                        Fix64.Max(0, row.Width - rowHeight - 3), row.Height);
                }
                AddText(commands, element, textRect, clip, list.makeItemText(list.itemsSource[index]));
            }
            return true;
        }
        if (element is not TreeView tree) return false;
        commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element, rect, clip,
            element.style.backgroundColor ?? new UIColor(56, 56, 56)));
        if (tree.showBorder) AddRectBorder(commands, element, rect, clip, Fix64.One,
            element.style.borderColor ?? new UIColor(29, 29, 29));
        var treeRowHeight = (Fix64)tree.fixedItemHeight;
        var rows = tree.GetVisibleRows();
        var firstRow = Math.Max(0, (int)(tree.scrollOffset / tree.fixedItemHeight));
        var visibleRows = Math.Max(1, (int)Math.Ceiling((double)rect.Height / tree.fixedItemHeight) + 1);
        for (var index = firstRow; index < Math.Min(rows.Count, firstRow + visibleRows); index++)
        {
            var (item, depth) = rows[index];
            var y = rect.Y + index * treeRowHeight - (Fix64)tree.scrollOffset;
            var row = new UIElementRect(rect.X, y, rect.Width, treeRowHeight);
            if (tree.selectedIds.Contains(item.Id))
                commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element, row, clip,
                    tree.focused ? new UIColor(45, 93, 135) : new UIColor(69, 69, 69)));
            else if (tree.pressedId == item.Id)
                commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element, row, clip,
                    new UIColor(62, 86, 108)));
            else if (tree.hoveredId == item.Id)
                commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element, row, clip,
                    new UIColor(67, 67, 67)));
            else if (tree.showAlternatingRowBackgrounds && index % 2 == 1)
                commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element, row, clip,
                    new UIColor(52, 52, 52)));
            if (tree.dropTargetId == item.Id)
            {
                var marker = tree.dropPosition switch
                {
                    TreeViewDropPosition.Before => new UIElementRect(row.X, row.Y, row.Width, 2),
                    TreeViewDropPosition.After => new UIElementRect(row.X, row.Y + row.Height - 2, row.Width, 2),
                    _ => row
                };
                commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element, marker, clip,
                    tree.dropPosition == TreeViewDropPosition.Inside
                        ? new UIColor(38, 94, 132, 170)
                        : new UIColor(72, 160, 220)));
            }
            var indent = depth * 14;
            if (item.Children is { Count: > 0 })
                AddText(commands, element, new UIElementRect(row.X + indent, row.Y, 14, row.Height), clip,
                    tree.IsExpanded(item.Id) ? "v" : ">");
            var contentX = row.X + indent + 14;
            var itemIconPath = tree.IsExpanded(item.Id) &&
                               !string.IsNullOrWhiteSpace(item.ExpandedIconPath)
                ? item.ExpandedIconPath
                : item.IconPath;
            if (!string.IsNullOrWhiteSpace(itemIconPath))
            {
                var iconSize = Fix64.Max(0, treeRowHeight - 4);
                commands.Add(new UIRenderCommand(UIRenderCommandType.Image, element,
                    new UIElementRect(contentX, row.Y + 2, iconSize, iconSize), clip,
                    UIColor.FromRgb(255, 255, 255), itemIconPath));
                contentX += treeRowHeight;
            }
            var textRect = new UIElementRect(contentX, row.Y,
                Fix64.Max(0, row.X + row.Width - contentX), row.Height);
            var text = tree.renamingId == item.Id ? tree.renameValue : item.Text;
            AddText(commands, element, textRect, clip, text);
            if (tree.renamingId == item.Id)
                AddRectBorder(commands, element, textRect, clip, Fix64.One, new UIColor(58, 121, 178));
        }
        if (tree.focused)
            AddRectBorder(commands, element, rect, clip, Fix64.One, new UIColor(58, 121, 178));
        return true;
    }

    private static void AddText(
        ICollection<UIRenderCommand> commands,
        VisualElement element,
        UIElementRect rect,
        UIElementRect clip,
        string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        commands.Add(new UIRenderCommand(UIRenderCommandType.Text, element, rect, clip,
            ResolveTextColor(element), text, element.style.fontSize > 0 ? element.style.fontSize : 14));
    }

    private static string FormatColor(BEngine.Color color, bool showAlpha)
    {
        static byte ToByte(Fix64 value) => (byte)Math.Clamp((int)Math.Round(
            Math.Clamp((double)value, 0, 1) * 255), 0, 255);
        var rgb = $"#{ToByte(color.r):X2}{ToByte(color.g):X2}{ToByte(color.b):X2}";
        return showAlpha ? $"{rgb}{ToByte(color.a):X2}" : rgb;
    }

    private static void AddCheckerboard(
        ICollection<UIRenderCommand> commands,
        VisualElement element,
        UIElementRect rect,
        UIElementRect clip)
    {
        var tileWidth = rect.Width / 2;
        var tileHeight = rect.Height / 2;
        for (var row = 0; row < 2; row++)
        for (var column = 0; column < 2; column++)
        {
            var x = rect.X + tileWidth * column;
            var y = rect.Y + tileHeight * row;
            commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element,
                new UIElementRect(x, y, column == 0 ? tileWidth : rect.Width - tileWidth,
                    row == 0 ? tileHeight : rect.Height - tileHeight), clip,
                (row + column) % 2 == 0 ? new UIColor(190, 190, 190) : new UIColor(100, 100, 100)));
        }
    }

    private static UIColor ToUiColor(BEngine.Color color)
    {
        static byte ToByte(Fix64 value) => (byte)Math.Clamp((int)Math.Round(
            Math.Clamp((double)value, 0, 1) * 255), 0, 255);
        return new UIColor(ToByte(color.r), ToByte(color.g), ToByte(color.b), ToByte(color.a));
    }

    private static UIColor ResolveBackground(VisualElement element) =>
        ResolveInteractiveBackground(element, element.style.backgroundColor ?? element switch
        {
            Button => new UIColor(88, 88, 88),
            TextField or FloatField or IntegerField or DropdownField => new UIColor(38, 41, 45),
            _ => UIColor.Clear
        });

    private static UIColor ResolveInteractiveBackground(VisualElement element, UIColor color)
    {
        if (!element.enabledInHierarchy) return new UIColor(55, 55, 55, color.A);
        if (element.pressed) return new UIColor(45, 93, 135, color.A);
        if (element.hovered && element is Button) return Lighten(color, 12);
        return color;
    }

    private static UIColor Lighten(UIColor color, int amount) => new(
        (byte)Math.Min(255, color.R + amount),
        (byte)Math.Min(255, color.G + amount),
        (byte)Math.Min(255, color.B + amount), color.A);

    private static UIColor ResolveTextColor(VisualElement element) =>
        element.enabledInHierarchy
            ? element.style.color ?? new UIColor(210, 210, 210)
            : new UIColor(128, 128, 128);

    private static string ResolveText(VisualElement element) => element switch
    {
        ToolbarMenu menu => $"{menu.text}  v",
        TextField field => field.value,
        FloatField field => field.value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        IntegerField field => field.value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Toggle toggle => $"{(toggle.value ? "[x]" : "[ ]")} {toggle.label}",
        DropdownField dropdown => dropdown.value,
        TextElement text => text.text,
        _ => string.Empty
    };

    private static UIElementRect Intersect(UIElementRect first, UIElementRect second)
    {
        var left = Fix64.Max(first.X, second.X);
        var top = Fix64.Max(first.Y, second.Y);
        var right = Fix64.Min(first.X + first.Width, second.X + second.Width);
        var bottom = Fix64.Min(first.Y + first.Height, second.Y + second.Height);
        return new UIElementRect(left, top, Fix64.Max(0, right - left), Fix64.Max(0, bottom - top));
    }

    private static bool HasArea(UIElementRect rect) => rect.Width > 0 && rect.Height > 0;

    private static void AddBorder(
        ICollection<UIRenderCommand> commands,
        VisualElement element,
        UIElementRect rect,
        UIElementRect clip)
    {
        var width = element.style.borderWidth;
        if (width <= 0 || element.style.borderColor is not { } color) return;
        AddRectBorder(commands, element, rect, clip, (Fix64)width, color);
    }

    private static void AddOutlinedRect(
        ICollection<UIRenderCommand> commands,
        VisualElement element,
        UIElementRect rect,
        UIElementRect clip,
        UIColor fill,
        UIColor border)
    {
        commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element, rect, clip, fill));
        AddRectBorder(commands, element, rect, clip, Fix64.One, border);
    }

    private static void AddRectBorder(
        ICollection<UIRenderCommand> commands,
        VisualElement element,
        UIElementRect rect,
        UIElementRect clip,
        Fix64 width,
        UIColor color)
    {
        commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element,
            new UIElementRect(rect.X, rect.Y, rect.Width, width), clip, color));
        commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element,
            new UIElementRect(rect.X, rect.Y + rect.Height - width, rect.Width, width), clip, color));
        commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element,
            new UIElementRect(rect.X, rect.Y, width, rect.Height), clip, color));
        commands.Add(new UIRenderCommand(UIRenderCommandType.SolidRect, element,
            new UIElementRect(rect.X + rect.Width - width, rect.Y, width, rect.Height), clip, color));
    }

    private static bool isReadOnlyField(this VisualElement element) => element switch
    {
        TextField field => field.isReadOnly,
        FloatField field => field.isReadOnly,
        IntegerField field => field.isReadOnly,
        ColorField field => field.isReadOnly,
        Slider field => field.isReadOnly,
        DropdownField field => field.isReadOnly,
        _ => false
    };
}
