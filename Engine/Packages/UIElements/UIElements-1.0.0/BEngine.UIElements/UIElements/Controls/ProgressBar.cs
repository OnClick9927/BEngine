using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public sealed class ProgressBar : VisualElement
{
    private float _lowValue;
    private float _highValue = 100;
    private float _value;
    public UIColor fillColor { get => field; set => SetRepaint(ref field, value); } = new(45, 93, 135);
    public bool showText { get => field; set => SetRepaint(ref field, value); } = true;
    private string _title = string.Empty;

    // The bar's measured size does not depend on its numeric range, fill or title.
    // Animating progress must not invalidate the entire document's layout/cascade.
    public float lowValue { get => _lowValue; set { SetRepaint(ref _lowValue, value); this.value = _value; } }
    public float highValue { get => _highValue; set { SetRepaint(ref _highValue, value); this.value = _value; } }
    public float value
    {
        get => _value;
        set => SetRepaint(ref _value, Math.Clamp(value, Math.Min(_lowValue, _highValue), Math.Max(_lowValue, _highValue)));
    }
    public string title { get => _title; set => SetRepaint(ref _title, value ?? string.Empty); }
}
