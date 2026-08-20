using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AngleSharp.Css.Dom;
using AngleSharp.Css.Dom.Events;
using AngleSharp.Css.Parser;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Dom.Events;
using AngleSharp.Html.Parser;
using BEngine.Serialization;
using BEngine.UIElements;

namespace BEngine.UIElements.Editor;

public sealed class HtmlConversionDiagnostic
{
    public HtmlConversionSeverity Severity { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public int Line { get; set; }
    public int Column { get; set; }
}
