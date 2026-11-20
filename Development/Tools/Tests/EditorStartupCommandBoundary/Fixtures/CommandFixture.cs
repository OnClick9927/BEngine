using System;
using System.Collections.Generic;
using System.IO;
using BEngine.Editor;

namespace SC43;

public sealed class Command : IEditorCommandLineTool, IDisposable
{
    static string Mode => Environment.GetEnvironmentVariable("SC43_MODE") ?? "";
    static void Event(string value) => File.AppendAllText(Environment.GetEnvironmentVariable("SC43_EVENTS")!,value+"\n");
    public Command() { Event("create-main"); }
    public string Option => "--fixture";
    public int Run(IReadOnlyList<string> arguments)
    {
        Event("run:"+arguments[0]);
        if (Mode == "readonly")
        {
            try { ((IList<string>)arguments)[0]="changed"; throw new Exception("mutable arguments"); }
            catch (NotSupportedException) { Event("readonly"); }
        }
        if (Mode == "reenter")
        {
            try { EditorServices.GetRequiredService<IEditorCommandLineRunner>().Run(new[]{"--fixture"}); throw new Exception("nested invocation permitted"); }
            catch (InvalidOperationException) { Event("reentry-rejected"); }
        }
        Environment.SetEnvironmentVariable("TEMP","SC43-mutated");
        Environment.SetEnvironmentVariable("TMP","SC43-mutated");
        Environment.SetEnvironmentVariable("TMPDIR","SC43-mutated");
        if (Mode is "fail" or "both") throw new InvalidOperationException("fixture-body-failure");
        return 37;
    }
    public void Dispose()
    {
        Event("dispose-main");
        if (Mode is "cleanup" or "both") throw new InvalidOperationException("fixture-cleanup-failure");
    }
}

public sealed class OtherCommand : IEditorCommandLineTool, IDisposable
{
    static string Mode => Environment.GetEnvironmentVariable("SC43_MODE") ?? "";
    static void Event(string value) => File.AppendAllText(Environment.GetEnvironmentVariable("SC43_EVENTS")!,value+"\n");
    public OtherCommand() { Event("create-other"); }
    public string Option => Mode == "duplicate" ? "--fixture" :
        Mode == "optionfail" ? throw new InvalidOperationException("fixture-option-failure") : "--unused";
    public int Run(IReadOnlyList<string> arguments) => throw new Exception("wrong tool");
    public void Dispose() { Event("dispose-other"); }
}
