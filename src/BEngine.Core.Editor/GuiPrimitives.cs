namespace BEngine.Editor;

public static class EditorGUIUtility
{
    public static Fix64 singleLineHeight => (Fix64)18;
    public static Fix64 standardVerticalSpacing => (Fix64)2;
    public static bool isProSkin => true;
    public static Fix64 pixelsPerPoint => Fix64.One;
    public static bool wideMode { get; set; } = true;
    public static Fix64 currentViewWidth => EditorWindow.focusedWindow?.position.width ?? (Fix64)0;
    public static string systemCopyBuffer { get; set; } = string.Empty;

    public static GUIContent IconContent(string name, string? text = null) =>
        new(text ?? string.Empty, name ?? string.Empty);
    public static void PingObject(BObject target) => EditorUtility.PingObject(target);
    public static void PingObject(int instanceId) => EditorUtility.PingObject(instanceId);
}

public static class ProjectWindowUtil
{
    public static void CreateAsset(BObject asset, string pathName) => AssetDatabase.CreateAsset(asset, pathName);
    public static void ShowCreatedAsset(BObject asset) => EditorGUIUtility.PingObject(asset);
}
