using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using BEngine;

namespace BEngine.ExampleTests.ConsoleTypeResolution;

internal static class Program
{
    private static int Main()
    {
        try
        {
            Require(!ReferencesSystemConsole(typeof(BEngine.Debug).Assembly.Location),
                "BEngine runtime must not reference System.Console from its logging path.");

            LogEntry? received = null;
            void Capture(LogEntry entry) => received = entry;
            BEngine.Debug.MessageLogged += Capture;
            try { BEngine.Debug.Log("CONSOLE_TYPE_RESOLUTION_PROBE"); }
            finally { BEngine.Debug.MessageLogged -= Capture; }

            Require(received?.Message == "CONSOLE_TYPE_RESOLUTION_PROBE",
                "Debug.Log did not publish the log entry without a console host.");
            global::System.Console.WriteLine(
                "CONSOLE_TYPE_RESOLUTION_OK|no-system-console-reference,event-published");
            return 0;
        }
        catch (Exception exception)
        {
            global::System.Console.Error.WriteLine($"CONSOLE_TYPE_RESOLUTION_FAILED|{exception}");
            return 1;
        }
    }

    private static bool ReferencesSystemConsole(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var image = new PEReader(stream);
        var metadata = image.GetMetadataReader();
        foreach (var handle in metadata.TypeReferences)
        {
            var reference = metadata.GetTypeReference(handle);
            if (metadata.GetString(reference.Namespace) == "System" &&
                metadata.GetString(reference.Name) == "Console")
                return true;
        }
        return false;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
