namespace BEngine.Player;

/// <summary>Host-owned text output used by command-line Player entry points.</summary>
public interface IPlayerHostOutput
{
    void WriteLine(string message);
    void WriteErrorLine(string message);
}
