using System.Reflection;
using BEngine.Serialization;

namespace BEngine.Editor;

public readonly record struct UndoRedoInfo(string undoName, int undoGroup, bool isRedo);
