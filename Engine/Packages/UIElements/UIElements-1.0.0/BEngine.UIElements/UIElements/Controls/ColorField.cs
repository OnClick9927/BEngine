using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public class ColorField : BaseField<BEngine.Color>
{
    public bool showAlpha { get => field; set => Set(ref field, value); } = true;
    public bool hdr { get => field; set => Set(ref field, value); }

    public ColorField(string label = "") : base(label) => SetValueWithoutNotify(BEngine.Color.white);
}
