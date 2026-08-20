namespace BEngine.Editor;

internal static class EditorTreeViewGUI
{
    public static Fix64 rowHeight => Fix64.Max(22,
        GUITextMetrics.MeasureLineHeight(GUI.skin.button.fontSize, GUIUtility.fontFamily));

    public static Fix64 BeginRow()
    {
        var height = rowHeight;
        GUILayout.BeginHorizontal(GUILayout.Height(height));
        return height;
    }

    public static void EndRow() => GUILayout.EndHorizontal();

    public static void Separator()
    {
        var area = GUILayoutUtility.GetControlRect(8);
        GUI.DrawRect(new Rect(area.x, area.y + 3, area.width, 2), EditorAppearance.palette.Border);
    }
}
