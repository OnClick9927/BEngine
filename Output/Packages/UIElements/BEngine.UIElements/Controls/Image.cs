using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public class Image : VisualElement
{
    public string sourcePath { get => field; set => Set(ref field, value ?? string.Empty); } = string.Empty;
    public string scaleMode { get; set; } = "ScaleToFit";
}
