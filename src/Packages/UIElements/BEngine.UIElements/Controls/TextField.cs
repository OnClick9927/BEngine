using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public class TextField : BaseField<string>
{
    public bool multiline { get => field; set => Set(ref field, value); }
    public bool scrollToEnd { get => field; set => Set(ref field, value); }
    public bool isPasswordField { get => field; set => Set(ref field, value); }
    public char maskCharacter { get => field; set => Set(ref field, value); } = '*';
    public event Action? clicked;
    public event Action? doubleClicked;
    public TextField(string label = "") : base(label) => SetValueWithoutNotify(string.Empty);
    internal void RaiseClicked() => clicked?.Invoke();
    internal void RaiseDoubleClicked() => doubleClicked?.Invoke();
}
