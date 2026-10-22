namespace BEngine.Player;

/// <summary>System console adapter for Player host diagnostics and validation output.</summary>
public sealed class SystemPlayerHostOutput : IPlayerHostOutput
{
    public void WriteLine(string message) => Console.WriteLine(message);

    public void WriteErrorLine(string message) => Console.Error.WriteLine(message);
}
