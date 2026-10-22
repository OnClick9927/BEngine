using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace BEngine.Testing.Editor;

public static class TestSourceLocator
{
    public static void Populate(IEnumerable<TestCase> tests)
    {
        foreach (var test in tests)
            if (TryLocate(test.Method, out var file, out var line))
            {
                test.SourceFile = file;
                test.SourceLine = line;
            }
    }

    public static bool TryLocate(MethodInfo method, out string file, out int line)
    {
        file = string.Empty;
        line = 0;
        var assemblyPath = method.Module.Assembly.Location;
        if (string.IsNullOrWhiteSpace(assemblyPath)) return false;
        var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
        if (!File.Exists(pdbPath)) return false;
        try
        {
            using var stream = File.OpenRead(pdbPath);
            using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
            var reader = provider.GetMetadataReader();
            var handle = MetadataTokens.MethodDefinitionHandle(method.MetadataToken & 0x00ffffff);
            if (TryRead(reader, reader.GetMethodDebugInformation(handle), out file, out line)) return true;
            foreach (var debugHandle in reader.MethodDebugInformation)
            {
                var information = reader.GetMethodDebugInformation(debugHandle);
                if (information.GetStateMachineKickoffMethod() != handle) continue;
                if (TryRead(reader, information, out file, out line)) return true;
            }
        }
        catch (Exception exception) when (exception is IOException or BadImageFormatException or ArgumentException)
        { }
        return false;
    }

    private static bool TryRead(
        MetadataReader reader,
        MethodDebugInformation information,
        out string file,
        out int line)
    {
        foreach (var point in information.GetSequencePoints())
        {
            if (point.IsHidden) continue;
            var documentHandle = point.Document.IsNil ? information.Document : point.Document;
            if (documentHandle.IsNil) continue;
            file = reader.GetString(reader.GetDocument(documentHandle).Name);
            line = point.StartLine;
            return line > 0;
        }
        file = string.Empty;
        line = 0;
        return false;
    }
}
