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

public sealed class HtmlConversionReport
{
    public string Format { get; set; } = "BEngine.HtmlConversion";
    public int Version { get; set; } = 1;
    public string SourcePath { get; set; } = string.Empty;
    public string SourceSha256 { get; set; } = string.Empty;
    public string UxmlPath { get; set; } = string.Empty;
    public string UssPath { get; set; } = string.Empty;
    public string BindingScriptPath { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public int HtmlElementCount { get; set; }
    public int HtmlTextNodeCount { get; set; }
    public int PreservedElementCount { get; set; }
    public int NativeElementCount { get; set; }
    public int FallbackElementCount { get; set; }
    public int CssRuleCount { get; set; }
    public int CssDeclarationCount { get; set; }
    public int ConvertedCssDeclarationCount { get; set; }
    public int ScriptCount { get; set; }
    public List<string> Scripts { get; set; } = [];
    public List<HtmlConversionCssSource> CssSources { get; set; } = [];
    public List<HtmlConversionDiagnostic> Diagnostics { get; set; } = [];
    public double DomPreservationPercent => HtmlElementCount == 0
        ? 100 : Math.Round(PreservedElementCount * 100d / HtmlElementCount, 2);
    public double NativeRenderingPercent => HtmlElementCount == 0
        ? 100 : Math.Round(NativeElementCount * 100d / HtmlElementCount, 2);
}
