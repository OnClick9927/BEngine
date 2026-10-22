namespace BEngine.UIElements;

public class Vector2Field : BaseField<Vector2>
{
    private readonly Label _caption;
    private readonly FloatField _x;
    private readonly FloatField _y;

    public Vector2Field(string label = "") : base(label)
    {
        style.flexDirection = FlexDirection.Row;
        _caption = new Label(label);
        _x = new FloatField("X");
        _y = new FloatField("Y");
        _x.style.flexGrow = _y.style.flexGrow = 1;
        _x.valueChanged += x => value = new Vector2((Fix64)x, value.y);
        _y.valueChanged += y => value = new Vector2(value.x, (Fix64)y);
        Add(_caption); Add(_x); Add(_y);
    }

    protected override void OnViewValueChanged()
    {
        _caption.text = label;
        _x.SetValueWithoutNotify((float)value.x);
        _y.SetValueWithoutNotify((float)value.y);
        _x.isReadOnly = _y.isReadOnly = isReadOnly;
    }
}
