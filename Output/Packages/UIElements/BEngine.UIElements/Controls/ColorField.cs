using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public class ColorField : BaseField<BEngine.Color>
{
    private bool _showAlpha = true;
    private bool _hdr;

    public bool showAlpha { get => _showAlpha; set => Set(ref _showAlpha, value); }
    public bool hdr { get => _hdr; set => Set(ref _hdr, value); }

    public ColorField(string label = "") : base(label) => SetValueWithoutNotify(BEngine.Color.white);
}
