namespace BEngine.Platform;

public interface IProcessHost
{
    IProcessHandle Start(ProcessHostRequest request);
    void StartDetached(ProcessHostRequest request);
}
