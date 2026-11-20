using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public class TextField : BaseField<string>
{
    private readonly TextEditingBuffer _editing = new();
    private int _maxLength = -1;
    /// <summary>Maximum UTF-16 length; -1 is unlimited. Limits never split a grapheme.</summary>
    public int maxLength
    {
        get => _maxLength;
        set
        {
            if (value < -1) throw new ArgumentOutOfRangeException(nameof(value));
            if (_maxLength == value) return;
            Set(ref _maxLength, value);
            this.value = this.value;
        }
    }
    protected override string NormalizeValue(string value)
    {
        value ??= string.Empty;
        if (_maxLength < 0 || value.Length <= _maxLength) return value;
        var end = Scan817(System.Globalization.StringInfo.ParseCombiningCharacters(value));
        [global::BEngine.SkipLoopGuard]
        int Scan817(global::System.Collections.Generic.IEnumerable<int> scanSource817)
        {
            ArgumentNullException.ThrowIfNull(scanSource817, "source");
            if (scanSource817 is global::System.Collections.Generic.IList<int> scanList817)
            {
                for (var scanIndex817 = scanList817.Count - 1; scanIndex817 >= 0; scanIndex817--)
                {
                    var scanItem817 = scanList817[scanIndex817];
                    if (Match817(scanItem817))
                        return scanItem817;
                }
        
                return default!;
            }
        
            int result = default!;
            foreach (var scanItem817 in scanSource817)
                if (Match817(scanItem817))
                    result = scanItem817;
            return result;
            bool Match817(int index) => index <= _maxLength;
        }
        return value[..end];
    }
    internal TextEditingBuffer editing { get { _editing.Synchronize(value); return _editing; } }
    public int cursorIndex => editing.Caret;
    public int selectIndex => editing.Anchor;
    public void SelectRange(int anchor, int caret) => editing.Select(anchor, caret);
    public string placeholderText { get => field; set => Set(ref field, value ?? string.Empty); } = "";
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
