using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public class TextField : BaseField<string>
{
    private bool _multiline;
    private bool _scrollToEnd;
    private bool _isPasswordField;
    private char _maskCharacter = '*';
    public bool multiline { get => _multiline; set => Set(ref _multiline, value); }
    public bool scrollToEnd { get => _scrollToEnd; set => Set(ref _scrollToEnd, value); }
    public bool isPasswordField { get => _isPasswordField; set => Set(ref _isPasswordField, value); }
    public char maskCharacter { get => _maskCharacter; set => Set(ref _maskCharacter, value); }
    public event Action? clicked;
    public event Action? doubleClicked;
    public TextField(string label = "") : base(label) => SetValueWithoutNotify(string.Empty);
    internal void RaiseClicked() => clicked?.Invoke();
    internal void RaiseDoubleClicked() => doubleClicked?.Invoke();
}
