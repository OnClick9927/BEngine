using System.Collections;
using System.Reflection;
using BEngine;
using BEngine.Editor;
using BEngine.Editor.Rendering;
using UnityEditorInternal;

namespace BEngine.ExampleTests.ReorderableListTests;

internal static class Program
{
    private static readonly MethodInfo BeginFrame = typeof(GUI).GetMethod("BeginFrame",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo EndFrame = typeof(GUI).GetMethod("EndFrame",
        BindingFlags.Static | BindingFlags.NonPublic)!;

    private static int Main()
    {
        try
        {
            VerifyPublicSurface();
            VerifyRawListDefaultsAndCallbacks();
            VerifySerializedArrayAndListMutations();
            VerifyMultiTargetCollectionsAndUndo();
            VerifyNullCollections();
            VerifyDrawingAndDragging();
            VerifyDefaultInspectorUsesReorderableList();
            Console.WriteLine("REORDERABLE_LIST_OK|public-api,ilist,serialized-array,serialized-list," +
                              "multi-target,null-collections,undo,without-undo,postprocess," +
                              "add,remove,move,multiselect,callbacks,dynamic-height,drag,default-inspector");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"REORDERABLE_LIST_FAILED|{exception}");
            return 1;
        }
        finally
        {
            GUIUtility.hotControl = 0;
            GUIUtility.keyboardControl = 0;
        }
    }

    private static void VerifyPublicSurface()
    {
        var type = typeof(ReorderableList);
        Require(type.IsPublic && type.Namespace == "UnityEditorInternal" &&
                type.Assembly.GetName().Name == "BEngine.Editor",
            "UnityEditorInternal.ReorderableList is not public from BEngine.Editor.");
        foreach (var constructor in new[]
                 {
                     new[] { typeof(IList), typeof(Type) },
                     [typeof(IList), typeof(Type), typeof(bool), typeof(bool), typeof(bool), typeof(bool)],
                     [typeof(SerializedObject), typeof(SerializedProperty)],
                     [typeof(SerializedObject), typeof(SerializedProperty), typeof(bool), typeof(bool),
                         typeof(bool), typeof(bool)]
                 })
            Require(type.GetConstructor(constructor) is not null,
                $"Missing ReorderableList constructor ({string.Join(",", constructor.Select(t => t.Name))}).");
        foreach (var method in new[]
                 {
                     "DoLayoutList", "DoList", "GetHeight", "GrabKeyboardFocus", "ReleaseKeyboardFocus",
                     "HasKeyboardControl", "ClearSelection", "Select", "SelectRange", "IsSelected", "Deselect"
                 })
            Require(type.GetMethods().Any(candidate => candidate.Name == method),
                $"Missing ReorderableList.{method}.");
        Require(ReorderableList.defaultBehaviours is not null,
            "ReorderableList.defaultBehaviours is unavailable.");
    }

    private static void VerifyRawListDefaultsAndCallbacks()
    {
        IList values = new List<string> { "Alpha", "Beta", "Gamma" };
        var list = new ReorderableList(values, typeof(string), true, true, true, true)
        {
            multiSelect = true
        };
        list.SelectRange(0, 1);
        Require(list.selectedIndices.SequenceEqual([0, 1]) && list.IsSelected(0) && list.IsSelected(1),
            "ReorderableList multi-selection state is incorrect.");
        list.Deselect(0);
        Require(list.selectedIndices.SequenceEqual([1]), "Deselect did not remove the requested index.");

        list.multiSelect = false;
        list.ClearSelection();
        list.Select(0);
        list.Select(1, append: true);
        Require(list.selectedIndices.SequenceEqual([0, 1]),
            "Select(append: true) incorrectly depended on the interactive multiSelect flag.");
        list.Select(2);

        ReorderableList.defaultBehaviours.DoAddButton(list);
        Require(values.Count == 4 && Equals(values[3], string.Empty) && list.index == 3,
            "Default raw-list add did not create and select a string element.");
        ReorderableList.defaultBehaviours.DoRemoveButton(list);
        Require(values.Count == 3 && values.Cast<string>().SequenceEqual(["Alpha", "Beta", "Gamma"]),
            "Default raw-list remove did not delete the selected element.");

        var changed = 0;
        var selected = 0;
        list.onChangedCallback = _ => changed++;
        list.onSelectCallback = _ => selected++;
        list.Select(1);
        list.onSelectCallback(list);
        Require(selected == 1 && changed == 0, "Selection and changed callbacks were conflated.");
    }

    private static void VerifySerializedArrayAndListMutations()
    {
        Undo.ClearAll();
        var probe = ScriptableObject.CreateInstance<ReorderableListProbe>();
        using var serialized = new SerializedObject(probe);
        var numbers = serialized.FindProperty(nameof(ReorderableListProbe.numbers))!;
        numbers.InsertArrayElementAtIndex(0);
        Require(probe.numbers.SequenceEqual([0, 1, 2, 3]) && serialized.hasModifiedProperties,
            "Serialized array insertion did not resize and mark the owner modified.");
        Require(numbers.MoveArrayElement(0, 2) && probe.numbers.SequenceEqual([1, 2, 0, 3]),
            "Serialized fixed-size array could not be reordered.");
        numbers.DeleteArrayElementAtIndex(1);
        Require(probe.numbers.SequenceEqual([1, 0, 3]),
            "Serialized fixed-size array could not delete an element.");

        var previousLabels = probe.labels;
        var labels = serialized.FindProperty(nameof(ReorderableListProbe.labels))!;
        labels.InsertArrayElementAtIndex(labels.arraySize);
        Require(!ReferenceEquals(previousLabels, probe.labels) &&
                probe.labels.SequenceEqual(["Alpha", "Beta", "Gamma", "Gamma"]),
            "Serialized List insertion mutated the existing instance or produced the wrong value.");
        Require(labels.MoveArrayElement(2, 0) &&
                probe.labels.SequenceEqual(["Gamma", "Alpha", "Beta", "Gamma"]),
            "Serialized List could not be reordered.");
        labels.DeleteArrayElementAtIndex(3);
        Require(probe.labels.SequenceEqual(["Gamma", "Alpha", "Beta"]),
            "Serialized List could not delete an element.");
        Require(serialized.ApplyModifiedProperties(),
            "Serialized collection operations did not leave an applicable modification.");
        Require(Undo.canUndo, "Applying serialized collection operations did not register Undo.");
        Undo.ClearAll();
    }

    private static void VerifyMultiTargetCollectionsAndUndo()
    {
        Undo.ClearAll();
        var first = ScriptableObject.CreateInstance<ReorderableListProbe>();
        var second = ScriptableObject.CreateInstance<ReorderableListProbe>();
        second.numbers = [10, 20];
        second.labels = ["Left", "Right"];

        using var serialized = new SerializedObject([first, second]);
        var numbers = serialized.FindProperty(nameof(ReorderableListProbe.numbers))!;
        var labels = serialized.FindProperty(nameof(ReorderableListProbe.labels))!;
        Require(numbers.arraySize == 2 && labels.arraySize == 2,
            "Multi-target collection size did not use the common editable range.");
        numbers.isExpanded = true;
        var multiHeight = EditorGUI.GetPropertyHeight(numbers, includeChildren: true);
        var multiCommands = Render(new Event(EventType.Repaint), () =>
            EditorGUI.PropertyField(new Rect(0, 0, 360, multiHeight), numbers, includeChildren: true),
            360, (int)Math.Ceiling((double)multiHeight) + 8);
        var multiLabels = multiCommands.Where(command => command.Type == GpuCanvasCommandType.Text)
            .Select(command => command.Content).ToArray();
        Require(multiLabels.Contains("Element 0") && multiLabels.Contains("Element 1") &&
                !multiLabels.Contains("Element 2"),
            "The default multi-target Inspector drew outside the common collection range.");
        Require(numbers.MoveArrayElement(0, 1) && labels.MoveArrayElement(0, 1),
            "Multi-target collections could not be reordered in their common range.");
        Require(first.numbers.SequenceEqual([2, 1, 3]) && second.numbers.SequenceEqual([20, 10]) &&
                first.labels.SequenceEqual(["Beta", "Alpha", "Gamma"]) &&
                second.labels.SequenceEqual(["Right", "Left"]),
            "A multi-target reorder copied the first target collection over another target.");
        Require(!ReferenceEquals(first.numbers, second.numbers) &&
                !ReferenceEquals(first.labels, second.labels),
            "Multi-target collection edits assigned a shared collection instance.");
        Require(!Undo.canUndo, "SerializedObject registered Undo before ApplyModifiedProperties.");

        var postprocessCalls = 0;
        UndoPropertyModification[] processed = [];
        Undo.postprocessModifications += OnPostprocess;
        try
        {
            Require(serialized.ApplyModifiedProperties(), "Multi-target collection changes were not applied.");
        }
        finally { Undo.postprocessModifications -= OnPostprocess; }

        Require(postprocessCalls == 1 && processed.Length == 4 &&
                processed.All(modification => modification.currentValue.PropertyPath is
                    nameof(ReorderableListProbe.numbers) or nameof(ReorderableListProbe.labels)),
            "ApplyModifiedProperties did not publish all target collection modifications.");
        Require(Undo.canUndo, "ApplyModifiedProperties did not register multi-target Undo.");
        Undo.PerformUndo();
        Require(first.numbers.SequenceEqual([1, 2, 3]) && second.numbers.SequenceEqual([10, 20]) &&
                first.labels.SequenceEqual(["Alpha", "Beta", "Gamma"]) &&
                second.labels.SequenceEqual(["Left", "Right"]),
            "Multi-target collection Undo did not restore each target independently.");
        Undo.PerformRedo();
        Require(first.numbers.SequenceEqual([2, 1, 3]) && second.numbers.SequenceEqual([20, 10]) &&
                first.labels.SequenceEqual(["Beta", "Alpha", "Gamma"]) &&
                second.labels.SequenceEqual(["Right", "Left"]) &&
                !ReferenceEquals(first.labels, second.labels),
            "Multi-target collection Redo did not restore independent edited values.");

        Undo.ClearAll();
        using var withoutUndo = new SerializedObject(first);
        var withoutUndoNumbers = withoutUndo.FindProperty(nameof(ReorderableListProbe.numbers))!;
        Require(withoutUndoNumbers.MoveArrayElement(0, 1), "WithoutUndo collection setup failed.");
        Require(withoutUndo.ApplyModifiedPropertiesWithoutUndo(),
            "ApplyModifiedPropertiesWithoutUndo did not apply a pending collection change.");
        Require(!Undo.canUndo, "ApplyModifiedPropertiesWithoutUndo unexpectedly registered Undo.");

        var replacement = new List<string> { "Independent" };
        using var boxedAssignment = new SerializedObject([first, second]);
        boxedAssignment.FindProperty(nameof(ReorderableListProbe.labels))!.boxedValue = replacement;
        Require(first.labels.SequenceEqual(replacement) && second.labels.SequenceEqual(replacement) &&
                !ReferenceEquals(first.labels, second.labels) &&
                !ReferenceEquals(first.labels, replacement) && !ReferenceEquals(second.labels, replacement),
            "Multi-target boxedValue assignment shared a mutable collection instance.");
        Require(boxedAssignment.ApplyModifiedPropertiesWithoutUndo() && !Undo.canUndo,
            "Independent multi-target boxedValue assignment could not be applied without Undo.");

        UndoPropertyModification[] OnPostprocess(UndoPropertyModification[] modifications)
        {
            postprocessCalls++;
            processed = modifications;
            return modifications;
        }
    }

    private static void VerifyNullCollections()
    {
        Undo.ClearAll();
        var probe = ScriptableObject.CreateInstance<ReorderableListProbe>();
        using var serialized = new SerializedObject(probe);
        var numbers = serialized.FindProperty(nameof(ReorderableListProbe.nullableNumbers))!;
        var labels = serialized.FindProperty(nameof(ReorderableListProbe.nullableLabels))!;
        Require(numbers.isArray && labels.isArray && numbers.arraySize == 0 && labels.arraySize == 0,
            "Null declared Array/List fields were not recognized as collections.");

        var numberList = new ReorderableList(serialized, numbers);
        var labelList = new ReorderableList(serialized, labels);
        ReorderableList.defaultBehaviours.DoAddButton(numberList);
        ReorderableList.defaultBehaviours.DoAddButton(labelList);
        Require(probe.nullableNumbers?.SequenceEqual([0]) == true &&
                probe.nullableLabels?.SequenceEqual([string.Empty]) == true,
            "The default add action did not initialize null Array/List fields.");

        var deleteCallbackCount = 0;
        numberList.onDeleteArrayElementCallback = (_, _) => deleteCallbackCount++;
        ReorderableList.defaultBehaviours.DoRemoveButton(numberList);
        Require(deleteCallbackCount == 0 && probe.nullableNumbers?.Length == 0,
            "Default footer removal invoked the context-menu delete callback or failed to remove the item.");
        Require(serialized.ApplyModifiedPropertiesWithoutUndo() && !Undo.canUndo,
            "Null collection initialization could not be committed without Undo.");
    }

    private static void VerifyDrawingAndDragging()
    {
        IList values = new List<string> { "Alpha", "Beta", "Gamma" };
        var list = new ReorderableList(values, typeof(string), true, false, false, false)
        {
            elementHeightCallback = index => 20 + index * 3
        };
        var drawn = new List<(int Index, int Height)>();
        list.drawElementCallback = (rect, index, _, _) =>
        {
            drawn.Add((index, (int)rect.height));
            EditorGUI.LabelField(rect, values[index]?.ToString() ?? "Null");
        };
        var commands = Render(new Event(EventType.Repaint), () =>
            list.DoList(new Rect(0, 0, 260, (Fix64)list.GetHeight())), 260, 150);
        Require(drawn.Select(item => item.Index).SequenceEqual([0, 1, 2]) &&
                drawn.Select(item => item.Height).SequenceEqual([18, 21, 24]) &&
                commands.Any(command => command.Type == GpuCanvasCommandType.Text && command.Content == "="),
            "ReorderableList did not honor dynamic heights or draw drag handles.");

        var reorderDetails = (-1, -1);
        var reorderCount = 0;
        var changeCount = 0;
        var mouseUpCount = 0;
        list.onReorderCallbackWithDetails = (_, oldIndex, newIndex) => reorderDetails = (oldIndex, newIndex);
        list.onReorderCallback = _ => reorderCount++;
        list.onChangedCallback = _ => changeCount++;
        list.onMouseUpCallback = _ => mouseUpCount++;
        Dispatch(new Event(EventType.MouseDown) { mousePosition = new Vector2(7, 12), button = 0 },
            () => list.DoList(new Rect(0, 0, 260, (Fix64)list.GetHeight())), 260, 150);
        Dispatch(new Event(EventType.MouseDrag) { mousePosition = new Vector2(7, 62), button = 0 },
            () => list.DoList(new Rect(0, 0, 260, (Fix64)list.GetHeight())), 260, 150);
        Dispatch(new Event(EventType.MouseUp) { mousePosition = new Vector2(7, 62), button = 0 },
            () => list.DoList(new Rect(0, 0, 260, (Fix64)list.GetHeight())), 260, 150);
        Require(values.Cast<string>().SequenceEqual(["Beta", "Gamma", "Alpha"]) &&
                reorderDetails == (0, 2) && reorderCount == 0 && changeCount == 1 && list.index == 2 &&
                mouseUpCount == 0,
            $"Drag reorder failed: [{string.Join(",", values.Cast<string>())}], " +
            $"details={reorderDetails}, callbacks={reorderCount}/{changeCount}/{mouseUpCount}, " +
            $"index={list.index}.");

        list.onReorderCallbackWithDetails = null;
        Dispatch(new Event(EventType.MouseDown) { mousePosition = new Vector2(7, 62), button = 0 },
            () => list.DoList(new Rect(0, 0, 260, (Fix64)list.GetHeight())), 260, 150);
        Dispatch(new Event(EventType.MouseDrag) { mousePosition = new Vector2(7, 12), button = 0 },
            () => list.DoList(new Rect(0, 0, 260, (Fix64)list.GetHeight())), 260, 150);
        Dispatch(new Event(EventType.MouseUp) { mousePosition = new Vector2(7, 12), button = 0 },
            () => list.DoList(new Rect(0, 0, 260, (Fix64)list.GetHeight())), 260, 150);
        Require(reorderCount == 1 && mouseUpCount == 0,
            "The ordinary reorder callback was not used as the details-callback fallback.");

        Dispatch(new Event(EventType.MouseDown) { mousePosition = new Vector2(50, 12), button = 0 },
            () => list.DoList(new Rect(0, 0, 260, (Fix64)list.GetHeight())), 260, 150);
        Dispatch(new Event(EventType.MouseUp) { mousePosition = new Vector2(50, 12), button = 0 },
            () => list.DoList(new Rect(0, 0, 260, (Fix64)list.GetHeight())), 260, 150);
        Require(mouseUpCount == 1, "A click without reordering did not invoke onMouseUpCallback.");

        var hiddenHeaderHeight = 0f;
        var hiddenHeader = new ReorderableList(new List<int> { 1 }, typeof(int), true, false, false, false)
        {
            drawHeaderCallback = rect => hiddenHeaderHeight = (float)rect.height
        };
        _ = Render(new Event(EventType.Repaint), () =>
            hiddenHeader.DoList(new Rect(0, 0, 260, (Fix64)hiddenHeader.GetHeight())), 260, 80);
        Require(hiddenHeaderHeight == 2,
            $"A hidden ReorderableList header did not preserve Unity's 2px minimum: {hiddenHeaderHeight}.");
    }

    private static void VerifyDefaultInspectorUsesReorderableList()
    {
        var probe = ScriptableObject.CreateInstance<ReorderableListProbe>();
        using var serialized = new SerializedObject(probe);
        var numbers = serialized.FindProperty(nameof(ReorderableListProbe.numbers))!;
        numbers.isExpanded = true;
        var height = EditorGUI.GetPropertyHeight(numbers, includeChildren: true);
        var commands = Render(new Event(EventType.Repaint), () =>
            EditorGUI.PropertyField(new Rect(0, 0, 360, height), numbers, includeChildren: true), 360,
            (int)Math.Ceiling((double)height) + 8);
        var texts = commands.Where(command => command.Type == GpuCanvasCommandType.Text)
            .Select(command => command.Content).ToArray();
        Require(ReorderableList.GetReorderableListFromSerializedProperty(numbers) is not null &&
                texts.Contains("Numbers") && texts.Contains("Element 0") && texts.Contains("=") &&
                texts.Contains("+") && texts.Contains("-"),
            $"Default Inspector did not render the array through ReorderableList: {string.Join("|", texts)}");
    }

    private static List<GpuCanvasCommand> Render(Event input, Action draw, int width, int height)
    {
        var commands = new List<GpuCanvasCommand>();
        Dispatch(input, draw, width, height, commands);
        return commands;
    }

    private static void Dispatch(Event input, Action draw, int width, int height,
        List<GpuCanvasCommand>? commands = null)
    {
        BeginFrame.Invoke(null, [input, width, height, commands ?? []]);
        try { draw(); }
        finally { EndFrame.Invoke(null, null); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
