namespace BEngine.Player;

/// <summary>An owned Player log sink. Implementations preserve the order of accepted records.</summary>
public interface IPlayerLogWriter : IDisposable
{
    void Write(string line, bool flush = false);
    void Flush();
}
