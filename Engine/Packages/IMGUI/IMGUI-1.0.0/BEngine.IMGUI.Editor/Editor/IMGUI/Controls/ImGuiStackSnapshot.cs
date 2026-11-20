namespace BEngine.Editor;

internal static class ImGuiStackSnapshot
{
    internal static T[] BottomFirst<T>(Stack<T>? stack)
    {
        if (stack is null || stack.Count == 0) return [];
        // Stack.ToArray copies once, top first. Restore pushes bottom first.
        var snapshot = stack.ToArray();
        Array.Reverse(snapshot);
        return snapshot;
    }
}
