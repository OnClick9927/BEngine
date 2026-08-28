using BEngine;

namespace BEngine.ExampleTests.ReorderableListTests;

internal sealed class ReorderableListProbe : ScriptableObject
{
    public int[] numbers = [1, 2, 3];
    public List<string> labels = ["Alpha", "Beta", "Gamma"];
    public int[]? nullableNumbers = null;
    public List<string>? nullableLabels = null;
}
