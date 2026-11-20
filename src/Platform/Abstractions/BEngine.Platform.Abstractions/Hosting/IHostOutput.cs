namespace BEngine.Hosting;

public interface IHostOutput
{
    void WriteLine(string message);
    void WriteErrorLine(string message);
}
