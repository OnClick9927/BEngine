using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public class Slider : BaseField<float>
{
    public float lowValue { get => field; set => SetRepaint(ref field, value); }
    public float highValue { get => field; set => SetRepaint(ref field, value); } = 1;
    public Slider(string label = "", float start = 0, float end = 1) : base(label)
    {
        lowValue = start;
        highValue = end;
    }
}
