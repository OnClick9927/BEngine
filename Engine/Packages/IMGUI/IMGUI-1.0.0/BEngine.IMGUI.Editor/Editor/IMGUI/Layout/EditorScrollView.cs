namespace BEngine.Editor;

public sealed class EditorScrollView
{
    private Fix64 _height = 1;

    public Vector2 Begin(Vector2 position)
    {
        var viewport = GUILayoutUtility.GetControlRect(60, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        var width = Fix64.Max(1, viewport.width - 12);
        var height = Fix64.Max(viewport.height, _height);
        position = GUI.BeginScrollView(viewport, position, new Rect(0, 0, width, height));
        GUILayout.BeginContainer(new Rect(0, 0, width, height));
        return position;
    }

    public void End()
    {
        _height = Fix64.Max(1, GUILayout.CurrentContentHeight + 4);
        GUILayout.EndContainer();
        GUI.EndScrollView();
    }
}
