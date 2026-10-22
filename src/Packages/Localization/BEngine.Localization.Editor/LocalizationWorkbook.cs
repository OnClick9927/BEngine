using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace BEngine.Localization.Editor;

/// <summary>A text-only XLSX exchange format. Formulas and legacy XLS files are not evaluated.</summary>
public static class LocalizationWorkbook
{
    private static readonly XNamespace Sheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Relations = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelations = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const long MaximumXmlBytes = 32 * 1024 * 1024;

    public static byte[] Export(IEnumerable<LocalizationTable> tables)
    {
        var rows = LocalizationCsv.Parse(LocalizationCsv.Export(tables));
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            XNamespace content = "http://schemas.openxmlformats.org/package/2006/content-types";
            Write(zip, "[Content_Types].xml", new XElement(content + "Types",
                new XElement(content + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(content + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                new XElement(content + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                new XElement(content + "Override", new XAttribute("PartName", "/xl/worksheets/sheet1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"))));
            Write(zip, "_rels/.rels", Relationships("officeDocument", "xl/workbook.xml"));
            Write(zip, "xl/_rels/workbook.xml.rels", Relationships("worksheet", "worksheets/sheet1.xml"));
            Write(zip, "xl/workbook.xml", new XElement(Sheet + "workbook", new XAttribute(XNamespace.Xmlns + "r", Relations),
                new XElement(Sheet + "sheets", new XElement(Sheet + "sheet", new XAttribute("name", "Localization"),
                    new XAttribute("sheetId", 1), new XAttribute(Relations + "id", "rId1")))));
            var data = new XElement(Sheet + "sheetData");
            foreach (var (row, index) in rows.Select((row, index) => (row, index)))
                data.Add(new XElement(Sheet + "row", new XAttribute("r", index + 1), row.Select((value, column) =>
                    new XElement(Sheet + "c", new XAttribute("r", $"{(char)('A' + column)}{index + 1}"), new XAttribute("t", "inlineStr"),
                        new XElement(Sheet + "is", new XElement(Sheet + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value))))));
            Write(zip, "xl/worksheets/sheet1.xml", new XElement(Sheet + "worksheet", data));
        }
        return output.ToArray();
    }

    public static IReadOnlyList<LocalizationTable> Import(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        using var input = new MemoryStream(bytes, writable: false);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        if (zip.Entries.Count > 4096 || zip.Entries.Sum(entry => entry.Length) > MaximumXmlBytes)
            throw new InvalidDataException("Localization workbook exceeds the 32 MiB expanded limit.");
        var workbook = Read(zip, "xl/workbook.xml");
        var sheet = workbook.Descendants(Sheet + "sheet").FirstOrDefault()
            ?? throw new InvalidDataException("Workbook contains no worksheets.");
        var id = (string?)sheet.Attribute(Relations + "id");
        var relation = Read(zip, "xl/_rels/workbook.xml.rels").Descendants(PackageRelations + "Relationship")
            .SingleOrDefault(element => (string?)element.Attribute("Id") == id)
            ?? throw new InvalidDataException("Worksheet relationship is missing.");
        if ((string?)relation.Attribute("TargetMode") == "External") throw new InvalidDataException("External worksheets are unsupported.");
        var target = (string?)relation.Attribute("Target") ?? throw new InvalidDataException("Worksheet target is missing.");
        var uri = new Uri(new Uri("https://workbook.invalid/xl/workbook.xml"), target);
        if (uri.Host != "workbook.invalid") throw new InvalidDataException("Invalid worksheet target.");
        var document = Read(zip, Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/'));
        var strings = zip.GetEntry("xl/sharedStrings.xml") is null ? [] : Read(zip, "xl/sharedStrings.xml")
            .Descendants(Sheet + "si").Select(element => string.Concat(element.Descendants(Sheet + "t").Select(text => text.Value))).ToArray();
        var csv = new StringBuilder();
        foreach (var row in document.Descendants(Sheet + "row"))
        {
            var values = new string[4]; Array.Fill(values, "");
            var occupied = new HashSet<int>();
            foreach (var cell in row.Elements(Sheet + "c"))
            {
                var reference = (string?)cell.Attribute("r") ?? "";
                if (reference.Length < 2 || reference[0] is < 'A' or > 'D' || !char.IsAsciiDigit(reference[1]))
                    throw new InvalidDataException("Localization worksheets must use columns A through D.");
                var column = reference[0] - 'A';
                if (!occupied.Add(column)) throw new InvalidDataException("Duplicate worksheet cell.");
                if (cell.Element(Sheet + "f") is not null) throw new InvalidDataException("Translate formulas to literal text before importing.");
                var value = cell.Element(Sheet + "v")?.Value ?? "";
                var kind = (string?)cell.Attribute("t");
                if (kind == "s")
                {
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index < 0 || index >= strings.Length)
                        throw new InvalidDataException("Invalid shared string reference.");
                    value = strings[index];
                }
                else if (kind == "inlineStr") value = string.Concat(cell.Descendants(Sheet + "t").Select(text => text.Value));
                values[column] = value;
            }
            if (values.All(string.IsNullOrEmpty)) continue;
            csv.AppendJoin(',', values.Select(value => '"' + value.Replace("\"", "\"\"") + '"')).Append("\r\n");
        }
        return LocalizationCsv.Import(csv.ToString());
    }

    private static XElement Relationships(string type, string target) => new(PackageRelations + "Relationships",
        new XElement(PackageRelations + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", Relations.NamespaceName + "/" + type), new XAttribute("Target", target)));
    private static void Write(ZipArchive zip, string path, XElement element)
    {
        using var stream = zip.CreateEntry(path, CompressionLevel.Optimal).Open();
        new XDocument(new XDeclaration("1.0", "utf-8", "yes"), element).Save(stream);
    }
    private static XDocument Read(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path) ?? throw new InvalidDataException($"Missing workbook part '{path}'.");
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumXmlBytes });
        return XDocument.Load(reader);
    }
}
