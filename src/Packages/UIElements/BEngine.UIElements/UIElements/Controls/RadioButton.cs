namespace BEngine.UIElements;

/// <summary>Mutually exclusive radio choices sharing the same direct parent.</summary>
public class RadioButton : Toggle
{
    public RadioButton(string label = "") : base(label)
    {
        valueChanged += selected =>
        {
            if (!selected || parent is null) return;
            {
                var __typedSource328 = parent.Children;
                ArgumentNullException.ThrowIfNull(__typedSource328, "source");
                foreach (var __typedItem328 in __typedSource328)
                {
                    if (__typedItem328 is not RadioButton sibling)
                        continue;
                    if (!ReferenceEquals(this, sibling))
                        sibling.value = false;
                }
            }
        };
    }
}
