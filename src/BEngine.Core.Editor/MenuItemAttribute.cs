namespace BEngine.Editor;

public sealed class MenuCommand
{
    public BObject? context { get; }
    public int userData { get; }

    public MenuCommand(BObject? context, int userData = 0)
    {
        this.context = context;
        this.userData = userData;
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class MenuItemAttribute : Attribute
{
    public string itemName { get; }
    public bool isValidateFunction { get; }
    public int priority { get; }

    public MenuItemAttribute(string itemName, bool isValidateFunction = false, int priority = 1000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemName);
        this.itemName = itemName.Replace('\\', '/').Trim('/');
        this.isValidateFunction = isValidateFunction;
        this.priority = priority;
    }
}
