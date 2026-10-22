using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace BEngine.Testing;

public static class TestReportWriter
{
    public static void WriteJson(string path, TestRunResult result) => Write(path, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    public static void WriteJUnit(string path, TestRunResult result)
    {
        var suite = new XElement("testsuite", new XAttribute("name", "BEngine"), new XAttribute("tests", result.Tests.Count),
            new XAttribute("failures", result.Failed), new XAttribute("skipped", result.Tests.Count(test => test.Status is TestStatus.Skipped or TestStatus.Canceled)),
            new XAttribute("time", ((result.FinishedAt - result.StartedAt).TotalSeconds).ToString("R", CultureInfo.InvariantCulture)),
            new XAttribute("timestamp", result.StartedAt.UtcDateTime.ToString("O")));
        foreach (var test in result.Tests)
        {
            var split = test.DisplayName.LastIndexOf('.');
            var item = new XElement("testcase", new XAttribute("name", split < 0 ? test.DisplayName : test.DisplayName[(split + 1)..]),
                new XAttribute("classname", split < 0 ? "BEngine" : test.DisplayName[..split]),
                new XAttribute("time", (test.DurationMilliseconds / 1000).ToString("R", CultureInfo.InvariantCulture)),
                new XAttribute("file", test.SourceFile), new XAttribute("line", test.SourceLine));
            if (test.Status is TestStatus.Failed or TestStatus.TimedOut)
                item.Add(new XElement("failure", new XAttribute("message", test.Message), new XAttribute("type", test.Status), test.StackTrace));
            else if (test.Status is TestStatus.Skipped or TestStatus.Canceled)
                item.Add(new XElement("skipped", new XAttribute("message", test.Message)));
            suite.Add(item);
        }
        Write(path, new XDocument(new XElement("testsuites", suite)).ToString());
    }
    private static void Write(string path, string text)
    {
        var fullPath = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, text); File.Move(temporary, fullPath, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
