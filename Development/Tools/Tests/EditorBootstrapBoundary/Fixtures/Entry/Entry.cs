using System.Reflection;
using BEngine.Editor;
namespace BootstrapProbe;
public sealed class Entry : IEditorApplicationEntry
{
    public int Run(string[] arguments)
    {
        if (arguments[0] == "legacy") return 7;
        if (arguments[0] == "throw") throw new InvalidOperationException("probe failure");
        return (int)Assembly.Load("BEngine.BootstrapPolicyProbe").GetType("BootstrapProbe.Policy")!
            .GetMethod("Read")!.Invoke(null, null)!;
    }
}
