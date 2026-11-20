namespace BEngine.Editor;

/// <summary>Unity-style entry point for Inspector previews; asset policies belong to the asset's package.</summary>
public static class AssetPreview
{
    public static bool HasPreview(BObject? asset) => asset is BAsset;
    public static AssetPreviewImage? GetAssetPreview(BObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (AssetPreviewProviders.Find(asset) is { } provider) return provider.GetPreview(asset);
        return asset is BAsset value ? EditorAssetAdapters.GetPreview(value)?.GetImage(value) : null;
    }
    public static string GetMiniThumbnail(BObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (GetAssetPreview(asset) is { } image) return image.Source;
        if (AssetPreviewProviders.Find(asset) is { } provider) return provider.GetIcon(asset);
        return asset is BAsset value && EditorAssetAdapters.GetPreview(value) is { } preview
            ? preview.GetIcon(value) : IconForType(asset);
    }
    public static bool IsLoadingAssetPreview(int instanceId) => false;
    public static void Invalidate(string assetPath) => EditorAssetPreviewSources.Invalidate(assetPath);
    public static void ClearTemporaryAssetPreviews() => EditorAssetPreviewSources.ClearTemporaryAssetPreviews();
    public static string GetInfoString(BObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (AssetPreviewProviders.Find(asset) is { } provider) return provider.GetInfo(asset);
        return asset is BAsset value && EditorAssetAdapters.GetPreview(value) is { } preview
            ? preview.GetInfo(value) : ObjectNames.NicifyVariableName(asset.GetType().Name);
    }
    public static void DrawAssetPreview(BObject asset, Rect previewArea)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (previewArea.width <= 0 || previewArea.height <= 0) return;
        if (AssetPreviewProviders.Find(asset) is { } provider) { provider.Draw(asset, previewArea); return; }
        if (asset is not BAsset value || EditorAssetAdapters.GetPreview(value) is not { } preview)
        {
            DrawIconSummary(previewArea, IconForType(asset), asset.name, ObjectNames.NicifyVariableName(asset.GetType().Name));
            return;
        }
        var content = preview.GetContent(value);
        switch (content.Kind)
        {
            case EditorAssetPreviewKind.Image: DrawImage(previewArea, content.Image); break;
            case EditorAssetPreviewKind.Unavailable: DrawUnavailable(previewArea, content.Icon, content.Title); break;
            case EditorAssetPreviewKind.ColorSample: DrawColorSample(content.Color, content.Title, previewArea); break;
            case EditorAssetPreviewKind.GradientIcon: DrawGradientIcon(content.Icon, content.Title, previewArea); break;
            case EditorAssetPreviewKind.Text: DrawText(content.Text, previewArea); break;
            case EditorAssetPreviewKind.HeaderText: DrawHeaderText(content.Title, content.Icon, content.Text, previewArea); break;
            case EditorAssetPreviewKind.IconSummary: DrawIconSummary(previewArea, content.Icon, content.Title, content.Text); break;
            case EditorAssetPreviewKind.Label: GUI.Label(previewArea, content.Text); break;
        }
    }
    private static string IconForType(BObject asset) =>
        EditorIconRegistry.GetIconPath(asset.GetType()) ?? EditorBuiltinIcons.Assets.Default;

    internal static Rect DrawImage(Rect area, AssetPreviewImage image)
    {
        var inner = Inset(area, 5);
        var fitted = Fit(inner, image.Width, image.Height);
        DrawCheckerboard(fitted);
        GUI.DrawTexture(fitted, image.Source);
        DrawBorder(fitted, StyleBorder(EditorStyles.frameBox));
        return fitted;
    }

    private static void DrawColorSample(Color color, string title, Rect area)
    {
        var inner = Inset(area, 12);
        var swatch = new Rect(inner.x + inner.width * Fix64.FromRaw(515396075L) /* 0.12 */,
            inner.y + inner.height * Fix64.FromRaw(515396075L) /* 0.12 */,
            inner.width * Fix64.FromRaw(3264175144L) /* 0.76 */, inner.height * Fix64.FromRaw(2834678415L) /* 0.66 */);
        DrawCheckerboard(swatch);
        GUI.DrawRect(swatch, color);
        DrawBorder(swatch, StyleBorder(EditorStyles.colorPickerBox));
        var label = new Rect(inner.x, swatch.yMax + 4, inner.width,
            Fix64.Max(0, inner.yMax - swatch.yMax - 4));
        GUI.Label(label, title, EditorStyles.centeredMiniLabel);
    }

    private static void DrawGradientIcon(string icon, string title, Rect area)
    {
        var background = EditorStyles.viewBackground;
        var selection = EditorStyles.selectionRect;
        GUI.DrawGradientRect(area, background.normal.backgroundColor, selection.normal.backgroundColor,
            background.hover.backgroundColor, selection.active.backgroundColor);
        DrawCenteredIcon(area, icon, title);
    }

    private static void DrawHeaderText(string title, string icon, string text, Rect area)
    {
        var headerHeight = Fix64.Min(28, area.height / 4);
        var header = new Rect(area.x, area.y, area.width, headerHeight);
        var titlebar = EditorStyles.inspectorTitlebar;
        GUI.DrawGradientRect(header, titlebar.normal.backgroundColor, titlebar.active.backgroundColor,
            titlebar.hover.backgroundColor, titlebar.focused.backgroundColor);
        GUI.Label(Inset(header, 4), new GUIContent(title, icon),
            EditorStyles.boldLabel);
        DrawText(text, new Rect(area.x, header.yMax, area.width, Fix64.Max(0, area.yMax - header.yMax)));
    }

    private static void DrawText(string text, Rect area)
    {
        GUI.Box(area, GUIContent.none, EditorStyles.textArea);
        var inner = Inset(area, 5);
        var lineHeight = Fix64.Max(16, EditorStyles.miniLabel.fontSize + 5);
        var visibleLines = Math.Max(1, (int)(inner.height / lineHeight));
        var lines = text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < Math.Min(lines.Length, visibleLines); index++)
        {
            var lineRect = new Rect(inner.x, inner.y + lineHeight * index, inner.width, lineHeight);
            if ((index & 1) != 0)
                GUI.DrawRect(lineRect, EditorStyles.scrollViewAlt.normal.backgroundColor);
            var line = Sanitize(lines[index]);
            GUI.Label(lineRect, $"{index + 1,3}  {line}", EditorStyles.miniLabel);
        }
    }

    private static void DrawIconSummary(Rect area, string icon, string title, string detail)
    {
        GUI.Box(area, GUIContent.none, EditorStyles.viewBackground);
        var iconSize = Fix64.Clamp(Fix64.Min(area.width, area.height) * Fix64.FromRaw(1975684956L) /* 0.46 */, 36, 88);
        var iconRect = new Rect(area.x + (area.width - iconSize) * Fix64.Half,
            area.y + Fix64.Max(8, (area.height - iconSize - 42) * Fix64.Half), iconSize, iconSize);
        GUI.DrawTexture(iconRect, icon);
        var titleRect = new Rect(area.x + 6, iconRect.yMax + 4, Fix64.Max(0, area.width - 12), 20);
        GUI.Label(titleRect, string.IsNullOrWhiteSpace(title) ? "Unnamed asset" : title,
            EditorStyles.centeredBoldLabel);
        var detailRect = new Rect(area.x + 6, titleRect.yMax, Fix64.Max(0, area.width - 12), 18);
        GUI.Label(detailRect, detail, EditorStyles.centeredMiniLabel);
    }

    internal static void DrawUnavailable(Rect area, string icon, string message) =>
        DrawIconSummary(area, icon, message, "The source file is missing or unsupported");

    private static void DrawCenteredIcon(Rect area, string icon, string label)
    {
        var size = Fix64.Clamp(Fix64.Min(area.width, area.height) * Fix64.FromRaw(1803886264L) /* 0.42 */, 32, 76);
        var iconRect = new Rect(area.x + (area.width - size) * Fix64.Half,
            area.y + (area.height - size - 24) * Fix64.Half, size, size);
        GUI.DrawTexture(iconRect, icon);
        GUI.Label(new Rect(area.x + 6, iconRect.yMax + 4, Fix64.Max(0, area.width - 12), 20), label,
            EditorStyles.centeredBoldLabel);
    }

    private static void DrawCheckerboard(Rect area)
    {
        if (area.width <= 0 || area.height <= 0) return;
        var tile = Fix64.Max(8, Fix64.Max(area.width, area.height) / 24);
        var columns = Math.Max(1, (int)Math.Ceiling((double)(area.width / tile)));
        var rows = Math.Max(1, (int)Math.Ceiling((double)(area.height / tile)));
        var first = EditorStyles.textField.normal.backgroundColor;
        var second = EditorStyles.frameBox.normal.backgroundColor;
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
        {
            var x = area.x + tile * column;
            var y = area.y + tile * row;
            GUI.DrawRect(new Rect(x, y, Fix64.Min(tile, area.xMax - x), Fix64.Min(tile, area.yMax - y)),
                ((row + column) & 1) == 0 ? first : second);
        }
    }

    private static Color StyleBorder(GUIStyle style)
    {
        var color = style.normal.borderColor;
        return color.a > 0 ? color : EditorStyles.separator.normal.backgroundColor;
    }

    private static string Sanitize(string line)
    {
        if (line.Length == 0) return string.Empty;
        var result = new char[line.Length];
        for (var index = 0; index < line.Length; index++)
            result[index] = line[index] == '\t' ? ' ' : char.IsControl(line[index]) ? ' ' : line[index];
        return new string(result);
    }

    private static Rect Fit(Rect area, int width, int height)
    {
        if (width <= 0 || height <= 0) return area;
        var scale = Fix64.Min(area.width / width, area.height / height);
        var fittedWidth = width * scale;
        var fittedHeight = height * scale;
        return new Rect(area.x + (area.width - fittedWidth) * Fix64.Half,
            area.y + (area.height - fittedHeight) * Fix64.Half, fittedWidth, fittedHeight);
    }

    private static Rect Inset(Rect area, Fix64 amount) => new(area.x + amount, area.y + amount,
        Fix64.Max(0, area.width - amount * 2), Fix64.Max(0, area.height - amount * 2));

    internal static void DrawBorder(Rect rect, Color color)
    {
        if (rect.width <= 0 || rect.height <= 0) return;
        GUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), color);
        GUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), color);
        GUI.DrawRect(new Rect(rect.x, rect.y + 1, 1, Fix64.Max(0, rect.height - 2)), color);
        GUI.DrawRect(new Rect(rect.xMax - 1, rect.y + 1, 1, Fix64.Max(0, rect.height - 2)), color);
    }

}
