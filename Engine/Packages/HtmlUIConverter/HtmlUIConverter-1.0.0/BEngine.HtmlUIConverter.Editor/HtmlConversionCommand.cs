namespace BEngine.UIElements.Editor;

public sealed class HtmlConversionCommand : BEngine.Editor.IEditorCommandLineTool
{
    public string Option => "--convert-html";

    public int Run(IReadOnlyList<string> arguments)
    {
        if (arguments.Count is < 2 or > 3)
            throw new ArgumentException("Usage: BEngine.Editor.exe --convert-html <source.html> [destination.bxml]");
        var source = EditorHostServices.Paths.GetFullPath(arguments[1]);
        var destination = arguments.Count == 3
            ? EditorHostServices.Paths.GetFullPath(arguments[2])
            : EditorHostServices.ChangeExtension(source, ".bxml");
        HtmlToUIElementsConverter.ConvertFile(source, destination);
        Console.WriteLine("HTML_TO_UIELEMENTS_OK");
        Console.WriteLine($"BXML={destination}");
        return 0;
    }
}
