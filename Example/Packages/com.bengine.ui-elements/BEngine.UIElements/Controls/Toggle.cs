using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public class Toggle : BaseField<bool>
{
    public Toggle(string label = "") : base(label) { }
}
