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

public sealed class HtmlConversionOptions
{
    public bool CopyLocalResources { get; set; } = true;
    public bool GenerateBindingScript { get; set; } = true;
    public bool PreserveComments { get; set; } = true;
    public bool PreserveScripts { get; set; } = true;
    public int ViewportWidth { get; set; } = 1280;
    public int ViewportHeight { get; set; } = 720;
    public string BindingNamespace { get; set; } = "Game.UI";
}
