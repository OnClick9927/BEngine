namespace BEngine.UIElements;

public class Vector4Field : BaseField<Vector4>
{
    private readonly Label _caption;
    private readonly FloatField[] _axes;

    public Vector4Field(string label = "") : base(label)
    {
        style.flexDirection = FlexDirection.Row;
        _caption = new Label(label);
        Add(_caption);
        _axes = [new("X"), new("Y"), new("Z"), new("W")];
        for (var i = 0; i < _axes.Length; i++)
        {
            var axis = i;
            _axes[i].style.flexGrow = 1;
            _axes[i].valueChanged += next =>
            {
                var v = value;
                value = new Vector4(axis == 0 ? (Fix64)next : v.x, axis == 1 ? (Fix64)next : v.y,
                    axis == 2 ? (Fix64)next : v.z, axis == 3 ? (Fix64)next : v.w);
            };
            Add(_axes[i]);
        }
    }

    protected override void OnViewValueChanged()
    {
        _caption.text = label;
        Fix64[] values = [value.x, value.y, value.z, value.w];
        for (var i = 0; i < _axes.Length; i++)
        { _axes[i].SetValueWithoutNotify((float)values[i]); _axes[i].isReadOnly = isReadOnly; }
    }
}
