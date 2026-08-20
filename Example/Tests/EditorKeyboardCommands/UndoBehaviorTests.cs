using BEngine.Editor;

namespace BEngine.ExampleTests.EditorKeyboardCommands;

internal static class UndoBehaviorTests
{
    internal static void Run()
    {
        Undo.ClearAll();
        var target = new GameObject("Before");
        var notifications = new List<UndoRedoInfo>();
        void OnUndoRedo(UndoRedoInfo info) => notifications.Add(info);
        Undo.undoRedoEvent += OnUndoRedo;
        try
        {
            Undo.RecordObject(target, "Rename Probe");
            target.name = "After";
            TestAssert.Require(Undo.canUndo && !Undo.canRedo, "Recording an edit did not populate Undo.");
            Undo.PerformUndo();
            TestAssert.Require(target.name == "Before" && Undo.canRedo,
                "Undo did not restore the recorded object state.");
            Undo.PerformRedo();
            TestAssert.Require(target.name == "After" && notifications.Count == 2 &&
                               notifications[0].undoName == "Rename Probe" && !notifications[0].isRedo &&
                               notifications[1].undoName == "Rename Probe" && notifications[1].isRedo,
                "Redo or Undo/Redo notifications did not preserve the operation identity.");
        }
        finally
        {
            Undo.undoRedoEvent -= OnUndoRedo;
            Undo.ClearAll();
        }
    }
}
