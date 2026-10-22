using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public class SearchField : TextField
{
    public bool showClearButton
    {
        get => field;
        set => Set(ref field, value);
    } = true;

    public event Action<string> searchChanged
    {
        add => valueChanged += value;
        remove => valueChanged -= value;
    }

    public event Action<string>? searchSubmitted;

    public SearchField(string label = "") : base(label) { placeholderText = "Search"; }

    public void ClearSearch() => value = string.Empty;
    public void ClearWithoutNotify() => SetValueWithoutNotify(string.Empty);
    internal void SubmitFromView() => searchSubmitted?.Invoke(value);
}
