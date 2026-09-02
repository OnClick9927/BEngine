namespace BEngine.Editor;

[Flags]
public enum HierarchyDropFlags
{
    None = 0,
    DropUpon = 1 << 0,
    DropBetween = 1 << 1,
    DropAfterParent = 1 << 2,
    DropOutside = 1 << 3
}
