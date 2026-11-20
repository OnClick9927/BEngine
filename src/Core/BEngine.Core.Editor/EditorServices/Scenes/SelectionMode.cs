
namespace BEngine.Editor;

[Flags]
public enum SelectionMode
{
    Unfiltered = 0,
    TopLevel = 1,
    Deep = 2,
    ExcludePrefab = 4,
    Editable = 8,
    Assets = 16,
    DeepAssets = 32
}
