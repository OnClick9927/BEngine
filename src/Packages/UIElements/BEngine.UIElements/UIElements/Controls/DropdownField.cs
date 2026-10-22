using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public class DropdownField : BaseField<string>
{
    private IReadOnlyList<string> _choices = [];
    public IReadOnlyList<string> choices
    {
        get => _choices;
        set
        {
            _choices = value ?? [];
            MarkDirty();
        }
    }
    public DropdownField(string label = "", IEnumerable<string>? choices = null) : base(label)
    {
        _choices = choices?.ToArray() ?? [];
        SetValueWithoutNotify(_choices.FirstOrDefault() ?? string.Empty);
    }
}
