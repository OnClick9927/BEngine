using BEngine.Hosting;

namespace BEngine.Platform;

public sealed class SystemHostOutput : IHostOutput
{
    public void WriteLine(string message) => Console.WriteLine(message);
    public void WriteErrorLine(string message) => Console.Error.WriteLine(message);
}
