namespace BEngine.Editor;

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
