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
