using System.Globalization;
using BEngine.Serialization;
using BEngine.Documents;

namespace BEngine.UIElements;

public sealed class UIAssetDocument : Document
{
    public string Format { get; set; } = "BEngine.UI";
    public int Version { get; set; } = 1;
    public UIElementDocument Root { get; set; } = new();
}
