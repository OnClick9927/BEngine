namespace BEngine.UIElements;

public class PasswordField : TextField
{
    public PasswordField(string label = "") : base(label) => isPasswordField = true;
}
