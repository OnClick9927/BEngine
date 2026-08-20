using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public class SearchField : TextField
{
    private string _placeholderText = "Search";
    private bool _showClearButton = true;

    public string placeholderText
    {
        get => _placeholderText;
        set => Set(ref _placeholderText, value ?? string.Empty);
    }

    public bool showClearButton
    {
        get => _showClearButton;
        set => Set(ref _showClearButton, value);
    }

    public event Action<string> searchChanged
    {
        add => valueChanged += value;
        remove => valueChanged -= value;
    }

    public event Action<string>? searchSubmitted;

    public SearchField(string label = "") : base(label) { }

    public void ClearSearch() => value = string.Empty;
    public void ClearWithoutNotify() => SetValueWithoutNotify(string.Empty);
    internal void SubmitFromView() => searchSubmitted?.Invoke(value);
}
