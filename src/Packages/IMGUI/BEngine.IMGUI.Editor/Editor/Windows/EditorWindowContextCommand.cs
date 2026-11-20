using System.Reflection;
using System.Linq.Expressions;

namespace BEngine.Editor;

internal readonly record struct EditorWindowContextCommand(
    string Name,
    string MethodName,
    Action<EditorWindow> Callback);
