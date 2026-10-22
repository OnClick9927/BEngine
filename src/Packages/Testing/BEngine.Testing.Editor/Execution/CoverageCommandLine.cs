namespace BEngine.Testing.Editor;

public sealed class CoverageCommandLine : IEditorCommandLineTool
{
    public string Option => "--instrument-coverage";
    public int Run(IReadOnlyList<string> arguments)
    {
        if (arguments.Count != 3) throw new ArgumentException("Usage: --instrument-coverage <assembly.dll> <instrumented-copy.dll>");
        var sites = PrecompiledCoverageInstrumenter.Instrument(arguments[1], arguments[2]);
        Console.WriteLine($"BENGINE_BINARY_COVERAGE|sites={sites}|path={Path.GetFullPath(arguments[2])}"); return 0;
    }
}
