using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public sealed class ProgressBar : VisualElement
{
    private float _lowValue;
    private float _highValue = 100;
    private float _value;
    private string _title = string.Empty;

    public float lowValue { get => _lowValue; set { Set(ref _lowValue, value); this.value = _value; } }
    public float highValue { get => _highValue; set { Set(ref _highValue, value); this.value = _value; } }
    public float value
    {
        get => _value;
        set => Set(ref _value, Math.Clamp(value, Math.Min(_lowValue, _highValue), Math.Max(_lowValue, _highValue)));
    }
    public string title { get => _title; set => Set(ref _title, value ?? string.Empty); }
}
