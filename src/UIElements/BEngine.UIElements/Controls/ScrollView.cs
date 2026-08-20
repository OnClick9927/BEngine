using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public class ScrollView : VisualElement
{
    private float _scrollOffset;
    public float scrollOffset { get => _scrollOffset; set => Set(ref _scrollOffset, Math.Max(0, value)); }
    public ScrollView() => style.overflow = Overflow.Scroll;
}
