using BEngine.Documents;

namespace BEngine.Editor.Documents;

public sealed class EditorPreferencesDocument : Document
{
    public string Format { get; set; } = "BEngine.Preferences";
    public int Version { get; set; } = 1;
    public string Locale { get; set; } = "zh-CN";
    public string ExternalScriptEditor { get; set; } = string.Empty;
    public float EditorScale { get; set; } = 1f;
    public string EditorFont { get; set; } = "BEngine Built-in";
    public int EditorFontSize { get; set; } = 14;
    public string EditorTheme { get; set; } = "Dark";
    public string EditorSkin { get; set; } = string.Empty;
    public bool AutoRefreshAssets { get; set; } = true;
    public bool ShowAssetMetaFiles { get; set; }
}
