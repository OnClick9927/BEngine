using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public class ScrollView : VisualElement
{
    public float scrollOffset { get => field; set => SetRepaint(ref field, float.IsFinite(value) ? Math.Max(0, value) : 0); }
    public ScrollView() => style.overflow = Overflow.Scroll;
}
