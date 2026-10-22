namespace BEngine;

[EditorIcon("Icons/Assets/AssetScript.png")]
public class Script : BAsset
{
    private string _text = string.Empty;
    private Type? _scriptClass;

    public string text => _text;
    public byte[] bytes => System.Text.Encoding.UTF8.GetBytes(_text);
    public Type? GetClass() => _scriptClass;

    internal void SetImportedContents(string source, Type? scriptClass)
    {
        _text = source ?? string.Empty;
        _scriptClass = scriptClass;
    }
}
