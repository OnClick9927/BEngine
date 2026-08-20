using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Serialization;

namespace BEngine.Editor;

public readonly record struct EditorProgressInfo(
    string Title,
    string Info,
    float Progress,
    bool IsVisible,
    bool IsCancelable,
    bool IsCancellationRequested)
{
    public static EditorProgressInfo None { get; } = new(string.Empty, string.Empty, 0, false, false, false);
}
