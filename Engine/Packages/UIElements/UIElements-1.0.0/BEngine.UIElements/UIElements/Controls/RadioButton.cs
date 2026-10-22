namespace BEngine.UIElements;

/// <summary>Mutually exclusive radio choices sharing the same direct parent.</summary>
public class RadioButton : Toggle
{
    public RadioButton(string label = "") : base(label)
    {
        valueChanged += selected =>
        {
            if (!selected || parent is null) return;
            foreach (var sibling in parent.Children.OfType<RadioButton>())
                if (!ReferenceEquals(this, sibling)) sibling.value = false;
        };
    }
}
