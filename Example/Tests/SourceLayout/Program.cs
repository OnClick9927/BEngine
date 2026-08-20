using BEngine.ExampleTests.SourceLayout;

try
{
    var result = SourceLayoutAudit.Run(AppContext.BaseDirectory);
    Console.WriteLine($"SOURCE_LAYOUT_OK|files={result.SourceFiles},types={result.TopLevelTypes},projects={result.Projects}");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"SOURCE_LAYOUT_FAILED|{exception.Message}");
    return 1;
}
