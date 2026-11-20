using System.Xml;
using BEngine;
using BEngine.Serialization;
using BEngine.ProjectSystem;

var exact = Fix64.FromRaw(123456789);
var original = new Sample
{
    Title = "<hello> & \"world\"",
    Fixed = exact,
    Values = [1, 2, 3],
    Named = new Dictionary<string, string> { ["a/b & c"] = "value\nline" },
    Adapted = new Adapted("detached")
};

foreach (var format in new (string Name, Func<Sample, string> Write, Func<string, Sample?> Read)[]
{
    ("JSON", JsonSerialization.Serialize, JsonSerialization.Deserialize<Sample>),
    ("XML", XmlSerialization.Serialize, XmlSerialization.Deserialize<Sample>)
})
{
    var text = format.Write(original);
    var copy = format.Read(text) ?? throw new Exception($"{format.Name}: null result");
    Check(copy.Title == original.Title, $"{format.Name}: escaped string");
    Check(copy.Fixed.RawValue == exact.RawValue, $"{format.Name}: exact fixed-point value");
    Check(copy.Values.SequenceEqual(original.Values), $"{format.Name}: array");
    Check(copy.Named["a/b & c"] == "value\nline", $"{format.Name}: dictionary");
    Check(copy.Adapted?.Name == "detached", $"{format.Name}: nested data adapter");
    Check(!text.Contains("ignored", StringComparison.OrdinalIgnoreCase), $"{format.Name}: ignored member");
    Check(text.Contains("displayName", StringComparison.Ordinal), $"{format.Name}: member alias");
}

Check(JsonSerialization.Deserialize<int>("42") == 42, "JSON scalar");
Check(XmlSerialization.Deserialize<int>(XmlSerialization.Serialize(42)) == 42, "XML scalar");
Check(JsonSerialization.Deserialize<string?>("null") is null, "JSON null");
Check(XmlSerialization.Deserialize<string?>(XmlSerialization.Serialize<string?>(null)) is null, "XML null");
var project = new RuntimeProjectData { Name = "XML & JSON", Window = new WindowData { Width = 1920, VSync = false } };
Check(JsonSerialization.Deserialize<RuntimeProjectData>(JsonSerialization.Serialize(project))?.Window.Width == 1920,
    "JSON project DTO");
var xmlProject = XmlSerialization.Deserialize<RuntimeProjectData>(XmlSerialization.Serialize(project));
Check(xmlProject?.Name == project.Name && xmlProject.Window.Width == 1920 && !xmlProject.Window.VSync,
    "XML project DTO");
CheckThrows<XmlException>(() => XmlSerialization.Deserialize<Sample>(
    "<!DOCTYPE serialization [<!ENTITY x SYSTEM 'file:///nope'>]><serialization version='1'><string>&x;</string></serialization>"),
    "XML DTD rejected");
CheckThrows<InvalidDataException>(() => XmlSerialization.Deserialize<Sample>(
    "<serialization version='1'><object><string name='title'>one</string><string name='title'>two</string></object></serialization>"),
    "XML duplicate member rejected");
CheckThrows<InvalidDataException>(() => XmlSerialization.Deserialize<Sample>(
    "<serialization version='1'><object><string name='title'><string>nested</string></string></object></serialization>"),
    "XML nested scalar rejected");

Check(typeof(YamlSerialization).Assembly == typeof(JsonSerialization).Assembly,
    "YAML, JSON and XML share the runtime Core assembly");
var coreReferences = typeof(JsonSerialization).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToHashSet();
Check(!coreReferences.Contains("BEngine.Core.Editor"), "Runtime serialization has no Editor dependency");
YamlSerialization.Initialize();
Check(TextDataSerialization.Codec is not null, "YAML codec registered without Foundation");
Check(TextDataSerialization.Codec!.Deserialize(TextDataSerialization.Codec.Serialize(original), typeof(Sample), false) is Sample,
    "YAML codec round trip");

Console.WriteLine("SERIALIZATION_FORMATS_OK|json|xml|yaml|independent");

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void CheckThrows<TException>(Action action, string message) where TException : Exception
{
    try { action(); }
    catch (TException) { return; }
    throw new Exception(message);
}

public sealed class Sample
{
    [SerializationMember(Alias = "displayName", Order = -1)]
    public string Title { get; set; } = string.Empty;
    public Fix64 Fixed { get; set; }
    public List<int> Values { get; set; } = [];
    public Dictionary<string, string> Named { get; set; } = [];
    public Adapted? Adapted { get; set; }
    [SerializationIgnore] public string Ignored { get; set; } = "secret";
}

[SerializationDataAdapter(typeof(Adapted.Adapter))]
public sealed class Adapted(string name)
{
    public string Name { get; } = name;

    public sealed class Adapter : SerializationDataAdapter
    {
        public override Type ObjectType => typeof(Adapted);
        public override Type DataType => typeof(AdaptedData);
        public override object CaptureData(object value) => new AdaptedData { Name = ((Adapted)value).Name };
        public override object RestoreData(object data) => new Adapted(((AdaptedData)data).Name);
    }
}

public sealed class AdaptedData
{
    public string Name { get; set; } = string.Empty;
}
