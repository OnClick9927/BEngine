using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public class Image : VisualElement
{
    public string sourcePath { get => field; set => Set(ref field, value ?? string.Empty); } = string.Empty;
    private string _texturePath = "";
    private Texture? _texture;
    internal Texture? ResolveTexture()
    {
        if (_texturePath != sourcePath || _texture is null || _texture.isDestroyed)
        { _texturePath = sourcePath; _texture = Resources.Load<Texture>(sourcePath); }
        return _texture;
    }
    public string scaleMode { get => field; set => SetRepaint(ref field, value ?? "ScaleToFit"); } = "ScaleToFit";
}
