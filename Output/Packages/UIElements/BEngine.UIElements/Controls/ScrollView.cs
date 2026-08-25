using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public class ScrollView : VisualElement
{
    public float scrollOffset { get => field; set => Set(ref field, Math.Max(0, value)); }
    public ScrollView() => style.overflow = Overflow.Scroll;
}
