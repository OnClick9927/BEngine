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

public sealed class HtmlConversionResult
{
    public required string UxmlPath { get; init; }
    public required string UssPath { get; init; }
    public string BindingScriptPath { get; init; } = string.Empty;
    public required string ReportPath { get; init; }
    public required HtmlConversionReport Report { get; init; }
}
