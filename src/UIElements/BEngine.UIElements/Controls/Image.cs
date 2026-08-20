using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public class Image : VisualElement
{
    private string _sourcePath = string.Empty;
    public string sourcePath { get => _sourcePath; set => Set(ref _sourcePath, value ?? string.Empty); }
    public string scaleMode { get; set; } = "ScaleToFit";
}
