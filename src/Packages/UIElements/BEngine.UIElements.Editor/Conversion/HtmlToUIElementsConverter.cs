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

public static class HtmlToUIElementsConverter
{
    private static readonly HashSet<string> HiddenTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "head", "base", "link", "meta", "style", "script", "title", "noscript"
    };

    private static readonly HashSet<string> InlineTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "abbr", "b", "bdi", "bdo", "cite", "code", "data", "del", "dfn", "em", "i",
        "ins", "kbd", "mark", "q", "ruby", "s", "samp", "small", "span", "strong", "sub",
        "sup", "time", "u", "var", "wbr"
    };

    private static readonly HashSet<string> NativeTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "html", "body", "main", "header", "footer", "nav", "section", "article", "aside", "div",
        "form", "fieldset", "legend", "details", "summary", "p", "h1", "h2", "h3", "h4", "h5",
        "h6", "span", "strong", "b", "em", "i", "small", "mark", "pre", "code", "blockquote",
        "ul", "ol", "li", "dl", "dt", "dd", "figure", "figcaption", "table", "caption", "thead",
        "tbody", "tfoot", "tr", "th", "td", "button", "a", "input", "textarea", "select", "option",
        "img", "picture", "progress", "meter", "hr", "br", "label"
    };

    private static readonly HashSet<string> UnsupportedRuntimeTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "canvas", "iframe", "frame", "frameset", "object", "embed", "portal", "video", "audio",
        "track", "map", "area", "svg", "math"
    };

    private static readonly HashSet<string> DirectUssProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "flex-direction", "display", "align-items", "justify-content", "overflow", "width", "height",
        "min-width", "min-height", "max-width", "max-height", "flex-grow", "flex-shrink", "font-size",
        "border-width", "color", "background-color", "border-color", "margin", "padding", "margin-left",
        "margin-top", "margin-right", "margin-bottom", "padding-left", "padding-top", "padding-right",
        "padding-bottom"
    };

    public static HtmlConversionResult ConvertFile(
        string htmlPath,
        string uxmlPath,
        HtmlConversionOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(htmlPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(uxmlPath);
        var sourcePath = Path.GetFullPath(htmlPath);
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("HTML source was not found.", sourcePath);
        return Convert(File.ReadAllText(sourcePath), sourcePath, uxmlPath, options);
    }

    public static HtmlConversionResult Convert(
        string html,
        string sourcePath,
        string uxmlPath,
        HtmlConversionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(uxmlPath);
        options ??= new HtmlConversionOptions();
        var sourceFullPath = Path.GetFullPath(sourcePath);
        var outputFullPath = Path.GetFullPath(uxmlPath);
        if (!Path.GetExtension(outputFullPath).Equals(".uxml", StringComparison.OrdinalIgnoreCase))
            outputFullPath = Path.ChangeExtension(outputFullPath, ".uxml");
        var outputDirectory = Path.GetDirectoryName(outputFullPath) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outputDirectory);
        var baseName = Path.GetFileNameWithoutExtension(outputFullPath);
        var ussPath = Path.Combine(outputDirectory, $"{baseName}.uss");
        var bindingPath = Path.Combine(outputDirectory, $"{baseName}.generated.cs");
        var reportPath = Path.Combine(outputDirectory, $"{baseName}.conversion.yaml");
        var report = new HtmlConversionReport
        {
            SourcePath = sourceFullPath,
            SourceSha256 = System.Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(html)))
                .ToLowerInvariant(),
            UxmlPath = outputFullPath,
            UssPath = ussPath,
            BindingScriptPath = options.GenerateBindingScript ? bindingPath : string.Empty
        };

        var parser = new HtmlParser(new HtmlParserOptions
        {
            IsScripting = true,
            IsPreservingAttributeNames = true,
            IsAcceptingCustomElementsEverywhere = true
        });
        parser.Error += (_, error) =>
        {
            if (error is HtmlErrorEvent htmlError)
                AddDiagnostic(report, HtmlConversionSeverity.Warning,
                    "HTML_PARSE_RECOVERY", htmlError.Message, sourceFullPath,
                    htmlError.Position.Line, htmlError.Position.Column);
        };
        var document = parser.ParseDocument(html);
        report.HtmlElementCount = document.All.Length;
        report.DocumentType = FormatDocumentType(document.Doctype);
        var context = new ConversionContext(sourceFullPath, outputFullPath, baseName, options, report);
        var rootNode = document.DocumentElement ?? throw new InvalidDataException("HTML has no document element.");
        var root = ConvertElement(rootNode, context);
        if (report.DocumentType.Length > 0)
            root.AddFirst(new XElement("SourceAttribute", new XAttribute("name", "!doctype"),
                new XAttribute("value", report.DocumentType)));
        root.SetAttributeValue("name", SanitizeIdentifier(root.Attribute("name")?.Value, "HtmlRoot"));
        root.SetAttributeValue("class", MergeClasses(root.Attribute("class")?.Value, "html-document"));

        var cssSources = CollectCss(document, sourceFullPath, report);
        report.CssSources = cssSources.Select(source => new HtmlConversionCssSource
        {
            Name = source.Name,
            Content = source.Content
        }).ToList();
        var convertedCss = ConvertCss(cssSources, context);
        var uxml = new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement("UXML", new XElement("Style", new XAttribute("src", Path.GetFileName(ussPath))), root));

        WriteAtomic(outputFullPath, uxml.ToString());
        WriteAtomic(ussPath, BuildDefaultUss(options) + Environment.NewLine + convertedCss);
        if (options.GenerateBindingScript)
            WriteAtomic(bindingPath, GenerateBindingScript(document, baseName, options, report));
        else if (File.Exists(bindingPath)) File.Delete(bindingPath);
        report.ScriptCount = document.Scripts.Length;
        if (options.PreserveScripts)
            report.Scripts = document.Scripts.Select(script => script.TextContent ?? string.Empty).ToList();
        YamlUtility.Save(report, reportPath);
        var persistedReport = YamlUtility.Load<HtmlConversionReport>(reportPath);
        if (persistedReport.HtmlElementCount != report.HtmlElementCount ||
            persistedReport.PreservedElementCount != report.PreservedElementCount)
            throw new InvalidDataException("HTML conversion report could not be read back without data loss.");

        // Validate the exact files consumed by the runtime loader before reporting success.
        var instantiated = VisualTreeAsset.Load(outputFullPath).Instantiate();
        if (instantiated.sourceTag.Length == 0)
            throw new InvalidDataException("Converted UXML lost its HTML source metadata.");
        if (UIRenderListBuilder.Build(instantiated, options.ViewportWidth, options.ViewportHeight).Commands.Count == 0)
            throw new InvalidDataException("Converted UXML produced no GPU UI render commands.");
        return new HtmlConversionResult
        {
            UxmlPath = outputFullPath,
            UssPath = ussPath,
            BindingScriptPath = options.GenerateBindingScript ? bindingPath : string.Empty,
            ReportPath = reportPath,
            Report = report
        };
    }

    private static XElement ConvertElement(IElement element, ConversionContext context)
    {
        var tag = element.LocalName.ToLowerInvariant();
        context.Report.PreservedElementCount++;
        if (NativeTags.Contains(tag)) context.Report.NativeElementCount++;
        else context.Report.FallbackElementCount++;

        var type = MapElementType(element);
        var node = new XElement(type,
            new XAttribute("source-tag", tag),
            new XAttribute("source-text", element.TextContent ?? string.Empty));
        var id = element.Id;
        if (!string.IsNullOrWhiteSpace(id)) node.SetAttributeValue("name", SanitizeIdentifier(id, tag));
        var classes = element.ClassList.Select(SanitizeClass).Where(value => value.Length > 0)
            .Concat(["html-element", $"html-tag-{SanitizeClass(tag)}"])
            .Concat(InlineTags.Contains(tag) ? ["html-inline"] : [])
            .Concat(HiddenTags.Contains(tag) ? ["html-hidden"] : [])
            .Distinct(StringComparer.Ordinal).ToArray();
        node.SetAttributeValue("class", string.Join(' ', classes));
        if (HiddenTags.Contains(tag)) node.SetAttributeValue("visible", false);
        foreach (var attribute in element.Attributes)
        {
            node.Add(new XElement("SourceAttribute", new XAttribute("name", attribute.Name),
                new XAttribute("value", attribute.Value)));
            var attributeClass = $"html-attr-{SanitizeClass(attribute.Name)}";
            classes = classes.Append(attributeClass).Append(
                $"{attributeClass}-{SanitizeClass(attribute.Value)}").Distinct(StringComparer.Ordinal).ToArray();
        }
        node.SetAttributeValue("class", string.Join(' ', classes));
        ApplyElementValues(element, node, context);

        if (UnsupportedRuntimeTags.Contains(tag))
        {
            AddDiagnostic(context.Report, HtmlConversionSeverity.Warning, "HTML_RUNTIME_FALLBACK",
                $"<{tag}> is preserved in the UXML DOM but has no native runtime renderer.", context.SourcePath);
            node.Add(new XElement("Label", new XAttribute("text", $"<{tag}>"),
                new XAttribute("class", "html-fallback-label")));
        }

        var skipTextChildren = type is nameof(Button) or nameof(Toggle) or nameof(TextField) or
            nameof(SearchField) or nameof(FloatField) or nameof(IntegerField) or nameof(Slider) or
            nameof(DropdownField) or nameof(ProgressBar) or nameof(Image);
        foreach (var child in element.ChildNodes)
        {
            if (child is IElement childElement)
            {
                if (tag == "select" && childElement.LocalName.Equals("option", StringComparison.OrdinalIgnoreCase))
                {
                    var sourceOption = ConvertElement(childElement, context);
                    sourceOption.SetAttributeValue("visible", false);
                    sourceOption.SetAttributeValue("class",
                        MergeClasses(sourceOption.Attribute("class")?.Value, "html-source-only"));
                    node.Add(sourceOption);
                }
                else node.Add(ConvertElement(childElement, context));
            }
            else if (child.NodeType == NodeType.Text && !skipTextChildren)
            {
                context.Report.HtmlTextNodeCount++;
                var raw = child.TextContent ?? string.Empty;
                var display = NormalizeText(raw);
                var textNode = new XElement("Label",
                    new XAttribute("source-tag", "#text"), new XAttribute("source-text", raw),
                    new XAttribute("class", "html-text-node"), new XAttribute("text", display));
                if (display.Length == 0) textNode.SetAttributeValue("visible", false);
                node.Add(textNode);
            }
            else if (child.NodeType == NodeType.Text)
            {
                context.Report.HtmlTextNodeCount++;
                node.Add(new XElement("Label",
                    new XAttribute("source-tag", "#text"),
                    new XAttribute("source-text", child.TextContent ?? string.Empty),
                    new XAttribute("class", "html-text-node html-source-only"),
                    new XAttribute("visible", false)));
            }
            else if (child.NodeType == NodeType.Comment && context.Options.PreserveComments)
            {
                node.Add(new XElement("VisualElement", new XAttribute("source-tag", "#comment"),
                    new XAttribute("source-text", child.TextContent ?? string.Empty),
                    new XAttribute("visible", false), new XAttribute("class", "html-comment")));
            }
        }
        return node;
    }

    private static string MapElementType(IElement element)
    {
        var tag = element.LocalName.ToLowerInvariant();
        if (tag is "button" or "summary" or "a") return nameof(Button);
        if (tag == "textarea") return nameof(TextField);
        if (tag == "select") return nameof(DropdownField);
        if (tag == "img") return nameof(Image);
        if (tag is "progress" or "meter") return nameof(ProgressBar);
        if (tag == "input")
        {
            var inputType = (element.GetAttribute("type") ?? "text").ToLowerInvariant();
            return inputType switch
            {
                "checkbox" or "radio" => nameof(Toggle),
                "range" => nameof(Slider),
                "number" => IsIntegerInput(element) ? nameof(IntegerField) : nameof(FloatField),
                "search" => nameof(SearchField),
                "color" => nameof(ColorField),
                "button" or "submit" or "reset" or "image" => nameof(Button),
                _ => nameof(TextField)
            };
        }
        if (tag == "br") return nameof(Label);
        if (tag == "hr") return nameof(VisualElement);
        if (UnsupportedRuntimeTags.Contains(tag)) return nameof(Box);
        return nameof(VisualElement);
    }

    private static void ApplyElementValues(IElement element, XElement node, ConversionContext context)
    {
        var tag = element.LocalName.ToLowerInvariant();
        var text = NormalizeText(element.TextContent ?? string.Empty);
        switch (node.Name.LocalName)
        {
            case nameof(Button):
                node.SetAttributeValue("text", text.Length > 0 ? text : element.GetAttribute("value") ?? tag);
                break;
            case nameof(TextField):
                node.SetAttributeValue("value", element.GetAttribute("value") ?? element.TextContent ?? string.Empty);
                if (tag == "textarea") node.SetAttributeValue("multiline", true);
                if (element.HasAttribute("readonly")) node.SetAttributeValue("read-only", true);
                break;
            case nameof(SearchField):
                node.SetAttributeValue("value", element.GetAttribute("value") ?? string.Empty);
                node.SetAttributeValue("placeholder-text", element.GetAttribute("placeholder") ?? "Search");
                break;
            case nameof(Toggle):
                node.SetAttributeValue("label", element.GetAttribute("aria-label") ??
                    element.GetAttribute("name") ?? element.GetAttribute("value") ?? string.Empty);
                node.SetAttributeValue("value", element.HasAttribute("checked"));
                break;
            case nameof(IntegerField):
            case nameof(FloatField):
                node.SetAttributeValue("value", element.GetAttribute("value") ?? "0");
                break;
            case nameof(Slider):
                node.SetAttributeValue("low-value", element.GetAttribute("min") ?? "0");
                node.SetAttributeValue("high-value", element.GetAttribute("max") ?? "100");
                node.SetAttributeValue("value", element.GetAttribute("value") ?? "0");
                break;
            case nameof(ColorField):
                node.SetAttributeValue("value", HtmlColorToEngine(element.GetAttribute("value") ?? "#FFFFFF"));
                break;
            case nameof(DropdownField):
                var options = element.Children.Where(child =>
                        child.LocalName.Equals("option", StringComparison.OrdinalIgnoreCase)).ToArray();
                node.SetAttributeValue("choices", string.Join(',', options.Select(option =>
                    EscapeChoice(NormalizeText(option.TextContent ?? string.Empty)))));
                node.SetAttributeValue("value", NormalizeText(options.FirstOrDefault(option =>
                    option.HasAttribute("selected"))?.TextContent ?? options.FirstOrDefault()?.TextContent ?? string.Empty));
                break;
            case nameof(Image):
                var source = element.GetAttribute("src") ?? string.Empty;
                node.SetAttributeValue("src", ResolveResource(source, context));
                node.SetAttributeValue("tooltip", element.GetAttribute("alt") ?? string.Empty);
                break;
            case nameof(ProgressBar):
                node.SetAttributeValue("low-value", element.GetAttribute("min") ?? "0");
                node.SetAttributeValue("high-value", element.GetAttribute("max") ?? "100");
                node.SetAttributeValue("value", element.GetAttribute("value") ?? "0");
                node.SetAttributeValue("title", text);
                break;
            case nameof(Label) when tag == "br":
                node.SetAttributeValue("text", "\n");
                break;
        }
        if (tag == "hr") node.SetAttributeValue("style", "height: 1px; background-color: #606060");
        var inlineStyle = element.GetAttribute("style");
        if (!string.IsNullOrWhiteSpace(inlineStyle))
        {
            var converted = ConvertDeclarations(inlineStyle, context, $"<{tag}> style");
            if (converted.Length > 0)
                node.SetAttributeValue("style", MergeStyle(node.Attribute("style")?.Value, converted));
        }
        if (element.HasAttribute("disabled")) node.SetAttributeValue("enabled", false);
        if (element.HasAttribute("title")) node.SetAttributeValue("tooltip", element.GetAttribute("title"));
    }

    private static IReadOnlyList<CssSource> CollectCss(
        IHtmlDocument document,
        string sourcePath,
        HtmlConversionReport report)
    {
        var sources = new List<CssSource>();
        var directory = Path.GetDirectoryName(sourcePath) ?? Directory.GetCurrentDirectory();
        foreach (var style in document.QuerySelectorAll("style"))
            sources.Add(new CssSource($"{sourcePath}#style", style.TextContent ?? string.Empty));
        foreach (var link in document.QuerySelectorAll("link[rel~=stylesheet]"))
        {
            var href = link.GetAttribute("href") ?? string.Empty;
            if (Uri.TryCreate(href, UriKind.Absolute, out var uri) && !uri.IsFile)
            {
                AddDiagnostic(report, HtmlConversionSeverity.Warning, "CSS_REMOTE_PRESERVED",
                    $"Remote style sheet '{href}' is recorded but is not downloaded automatically.", sourcePath);
                continue;
            }
            var path = uri?.IsFile == true ? uri.LocalPath : Path.GetFullPath(Path.Combine(directory,
                href.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(path)) sources.Add(new CssSource(path, File.ReadAllText(path)));
            else AddDiagnostic(report, HtmlConversionSeverity.Warning, "CSS_FILE_MISSING",
                $"Style sheet '{href}' was not found.", sourcePath);
        }
        return sources;
    }

    private static string ConvertCss(IReadOnlyList<CssSource> sources, ConversionContext context)
    {
        var output = new StringBuilder();
        foreach (var source in sources)
        {
            output.AppendLine($"/* Imported from {EscapeComment(source.Name)} */");
            var parser = new CssParser(new CssParserOptions
            {
                IsIncludingUnknownDeclarations = true,
                IsIncludingUnknownRules = true,
                IsToleratingInvalidSelectors = true
            });
            parser.Error += (_, error) =>
            {
                if (error is CssErrorEvent cssError)
                    AddDiagnostic(context.Report, HtmlConversionSeverity.Warning,
                        "CSS_PARSE_RECOVERY", cssError.Message, source.Name,
                        cssError.Position.Line, cssError.Position.Column);
            };
            var sheet = parser.ParseStyleSheet(source.Content);
            WriteRules(sheet.Rules, output, context, source.Name);
        }
        return output.ToString();
    }

    private static void WriteRules(
        ICssRuleList rules,
        StringBuilder output,
        ConversionContext context,
        string source)
    {
        for (var index = 0; index < rules.Length; index++)
        {
            var rule = rules[index];
            context.Report.CssRuleCount++;
            if (rule is ICssStyleRule styleRule)
            {
                var selector = ConvertSelector(styleRule.SelectorText, context, source);
                var declarations = ConvertDeclaration(styleRule.Style, context, source);
                if (selector.Length == 0 || declarations.Length == 0) continue;
                output.AppendLine($"{selector} {{");
                foreach (var declaration in declarations.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    output.AppendLine($"    {declaration.Trim()};");
                output.AppendLine("}");
            }
            else if (rule is ICssGroupingRule group)
            {
                AddDiagnostic(context.Report, HtmlConversionSeverity.Warning, "CSS_CONDITION_FLATTENED",
                    $"Conditional CSS rule '{RuleHeader(rule.CssText)}' was flattened for UIElements.", source);
                WriteRules(group.Rules, output, context, source);
            }
            else
            {
                AddDiagnostic(context.Report, HtmlConversionSeverity.Warning, "CSS_RULE_PRESERVED_IN_REPORT",
                    $"CSS rule '{RuleHeader(rule.CssText)}' has no USS runtime equivalent.", source);
                output.AppendLine($"/* Unsupported CSS: {EscapeComment(rule.CssText)} */");
            }
        }
    }

    private static string ConvertDeclaration(
        ICssStyleDeclaration declaration,
        ConversionContext context,
        string source)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < declaration.Length; index++)
        {
            var name = declaration[index];
            var value = declaration.GetPropertyValue(name);
            if (value.Equals("initial", StringComparison.OrdinalIgnoreCase)) continue;
            context.Report.CssDeclarationCount++;
            if (TryConvertProperty(name, value, context, out var converted))
            {
                context.Report.ConvertedCssDeclarationCount++;
                builder.Append(converted).Append(';');
            }
            else AddDiagnostic(context.Report, HtmlConversionSeverity.Warning, "CSS_PROPERTY_PRESERVED",
                $"CSS property '{name}: {value}' is preserved in the report but has no native USS renderer.", source);
        }
        return builder.ToString();
    }

    private static string ConvertDeclarations(string css, ConversionContext context, string source)
    {
        var parser = new CssParser(new CssParserOptions { IsIncludingUnknownDeclarations = true });
        var declaration = parser.ParseDeclaration(css);
        return declaration is null ? string.Empty : ConvertDeclaration(declaration, context, source);
    }

    private static bool TryConvertProperty(
        string name,
        string value,
        ConversionContext context,
        out string converted)
    {
        name = name.Trim().ToLowerInvariant();
        value = value.Trim();
        if (DirectUssProperties.Contains(name))
        {
            if (name is "width" or "height" or "min-width" or "min-height" or "max-width" or "max-height" or
                "font-size" or "border-width" or "margin" or "padding" or "margin-left" or "margin-top" or
                "margin-right" or "margin-bottom" or "padding-left" or "padding-top" or "padding-right" or
                "padding-bottom")
                value = ConvertLengthExpression(value, name, context);
            if (name == "display" && value is not ("none" or "flex")) value = "flex";
            if (name == "overflow" && value is "auto") value = "scroll";
            converted = $"{name}: {value}";
            return true;
        }
        switch (name)
        {
            case "background" when LooksLikeColor(value):
                converted = $"background-color: {value}";
                return true;
            case "border":
                var width = Regex.Match(value, @"(?<width>\d+(?:\.\d+)?(?:px|pt)?)", RegexOptions.IgnoreCase);
                var color = Regex.Match(value, @"(?<color>#[0-9a-f]{3,8}|rgba?\([^)]*\)|[a-z]+)$",
                    RegexOptions.IgnoreCase);
                var parts = new List<string>();
                if (width.Success) parts.Add($"border-width: {ConvertLengthExpression(width.Groups["width"].Value, name, context)}");
                if (color.Success) parts.Add($"border-color: {color.Groups["color"].Value}");
                converted = string.Join("; ", parts);
                return converted.Length > 0;
            case "overflow-x":
            case "overflow-y":
                converted = $"overflow: {(value == "auto" ? "scroll" : value)}";
                return true;
            case "flex":
                var grow = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "0";
                converted = $"flex-grow: {grow}";
                return float.TryParse(grow, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
            case "font" when Regex.Match(value, @"\d+(?:\.\d+)?(?:px|pt|rem|em)") is { Success: true } size:
                converted = $"font-size: {ConvertLengthExpression(size.Value, name, context)}";
                return true;
            case "visibility":
                converted = $"display: {(value == "hidden" ? "none" : "flex")}";
                return true;
            default:
                converted = string.Empty;
                return false;
        }
    }

    private static string ConvertSelector(string selector, ConversionContext context, string source)
    {
        var result = selector.Trim();
        if (result.Length == 0) return string.Empty;
        result = Regex.Replace(result, "\\[(?<name>[\\w-]+)(?:\\s*[~|^$*]?=\\s*['\\\"]?(?<value>[^'\\\"\\]]+)['\\\"]?)?\\]", match =>
        {
            var name = SanitizeClass(match.Groups["name"].Value);
            var value = SanitizeClass(match.Groups["value"].Value);
            return value.Length == 0 ? $".html-attr-{name}" : $".html-attr-{name}-{value}";
        });
        if (Regex.IsMatch(result, @"::?[\w-]+"))
        {
            AddDiagnostic(context.Report, HtmlConversionSeverity.Warning, "CSS_PSEUDO_APPROXIMATED",
                $"Pseudo selector in '{selector}' was removed because USS state selectors are not implemented.", source);
            result = Regex.Replace(result, @"::?[\w-]+(?:\([^)]*\))?", string.Empty);
        }
        if (result.Contains('>') || result.Contains('+') || result.Contains('~'))
        {
            AddDiagnostic(context.Report, HtmlConversionSeverity.Warning, "CSS_COMBINATOR_APPROXIMATED",
                $"Selector combinators in '{selector}' were approximated as descendant selectors.", source);
            result = Regex.Replace(result, @"\s*[>+~]\s*", " ");
        }
        result = Regex.Replace(result,
            @"(?<![.#\w-])(?<tag>[a-zA-Z][\w-]*)(?=(?:[.#\s,:]|$))",
            match => $".html-tag-{SanitizeClass(match.Groups["tag"].Value)}");
        result = Regex.Replace(result, @"#([\w-]+)", match => $"#{SanitizeIdentifier(match.Groups[1].Value, "html")}");
        result = Regex.Replace(result, @"\.([\w-]+)", match => $".{SanitizeClass(match.Groups[1].Value)}");
        return result.Trim();
    }

    private static string ConvertLengthExpression(string value, string property, ConversionContext context)
    {
        var token = value.Trim().ToLowerInvariant();
        if (token is "auto" or "none" or "0") return token;
        if (token.Contains(' ')) return string.Join(' ', token.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => ConvertLengthExpression(part, property, context)));
        var match = Regex.Match(token, @"^(?<number>-?\d+(?:\.\d+)?)(?<unit>px|pt|pc|in|cm|mm|rem|em|vw|vh|%)?$");
        if (!match.Success)
        {
            AddDiagnostic(context.Report, HtmlConversionSeverity.Warning, "CSS_LENGTH_APPROXIMATED",
                $"Length expression '{value}' for {property} could not be evaluated and was replaced with 0.",
                context.SourcePath);
            return "0";
        }
        var number = double.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture);
        var unit = match.Groups["unit"].Value;
        var pixels = unit switch
        {
            "" or "px" => number,
            "pt" => number * 96 / 72,
            "pc" => number * 16,
            "in" => number * 96,
            "cm" => number * 96 / 2.54,
            "mm" => number * 96 / 25.4,
            "rem" or "em" => number * 16,
            "vw" => number * context.Options.ViewportWidth / 100,
            "vh" => number * context.Options.ViewportHeight / 100,
            "%" => number * (property.Contains("height", StringComparison.OrdinalIgnoreCase)
                ? context.Options.ViewportHeight : context.Options.ViewportWidth) / 100,
            _ => number
        };
        if (unit is "%" or "vw" or "vh" or "rem" or "em")
            AddDiagnostic(context.Report, HtmlConversionSeverity.Info, "CSS_LENGTH_RESOLVED",
                $"'{value}' was resolved to pixels using a {context.Options.ViewportWidth}x{context.Options.ViewportHeight} viewport.",
                context.SourcePath);
        return $"{pixels:0.###}px";
    }

    private static string ResolveResource(string source, ConversionContext context)
    {
        if (string.IsNullOrWhiteSpace(source)) return string.Empty;
        if (source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            AddDiagnostic(context.Report, HtmlConversionSeverity.Warning, "RESOURCE_DATA_URI_PRESERVED",
                "A data URI image was preserved as source metadata but cannot be loaded by Resources.Load.",
                context.SourcePath);
            return source;
        }
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            AddDiagnostic(context.Report, HtmlConversionSeverity.Warning, "RESOURCE_REMOTE_PRESERVED",
                $"Remote resource '{source}' is not downloaded automatically.", context.SourcePath);
            return source;
        }
        var inputDirectory = Path.GetDirectoryName(context.SourcePath) ?? Directory.GetCurrentDirectory();
        var sourcePath = uri?.IsFile == true ? uri.LocalPath : Path.GetFullPath(Path.Combine(inputDirectory,
            source.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(sourcePath))
        {
            AddDiagnostic(context.Report, HtmlConversionSeverity.Warning, "RESOURCE_MISSING",
                $"Local resource '{source}' was not found.", context.SourcePath);
            return source;
        }
        if (!context.Options.CopyLocalResources) return sourcePath;
        var assetsPath = ResolveAssetsDirectory(context);
        var resourcesRoot = Path.Combine(assetsPath, "Resources", "HtmlImported",
            SanitizeClass(context.BaseName));
        Directory.CreateDirectory(resourcesRoot);
        var fileName = UniqueResourceName(sourcePath);
        var destination = Path.Combine(resourcesRoot, fileName);
        File.Copy(sourcePath, destination, true);
        return $"HtmlImported/{SanitizeClass(context.BaseName)}/{fileName}".Replace('\\', '/');
    }

    private static string ResolveAssetsDirectory(ConversionContext context)
    {
        if (!string.IsNullOrWhiteSpace(Application.dataPath))
        {
            var configured = Path.GetFullPath(Application.dataPath);
            if (Directory.Exists(configured) &&
                Path.GetFileName(configured).Equals("Assets", StringComparison.OrdinalIgnoreCase))
                return configured;
        }

        foreach (var candidate in new[] { context.SourcePath, context.OutputPath })
        {
            for (var directory = new DirectoryInfo(Path.GetDirectoryName(candidate)!);
                 directory is not null;
                 directory = directory.Parent)
                if (directory.Name.Equals("Assets", StringComparison.OrdinalIgnoreCase)) return directory.FullName;
        }

        return Path.GetDirectoryName(context.OutputPath)!;
    }

    private static string GenerateBindingScript(
        IHtmlDocument document,
        string baseName,
        HtmlConversionOptions options,
        HtmlConversionReport report)
    {
        var className = SanitizeIdentifier(baseName, "HtmlView") + "View";
        var elements = document.All.Where(element => !string.IsNullOrWhiteSpace(element.Id))
            .GroupBy(element => SanitizeIdentifier(element.Id, "Element"), StringComparer.Ordinal)
            .Select(group => group.First()).ToArray();
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("using BEngine.UIElements;");
        builder.AppendLine();
        builder.AppendLine($"namespace {SanitizeNamespace(options.BindingNamespace)};");
        builder.AppendLine();
        builder.AppendLine($"public sealed partial class {className}");
        builder.AppendLine("{");
        builder.AppendLine("    public VisualElement Root { get; }");
        builder.AppendLine();
        builder.AppendLine($"    public {className}(VisualElement root)");
        builder.AppendLine("    {");
        builder.AppendLine("        Root = root ?? throw new System.ArgumentNullException(nameof(root));");
        builder.AppendLine("        BindHtmlEvents();");
        builder.AppendLine("    }");
        foreach (var element in elements)
        {
            var property = SanitizeIdentifier(element.Id, "Element");
            var type = MapElementType(element);
            builder.AppendLine();
            builder.AppendLine($"    public {type}? {property} => Root.Q<{type}>(\"{EscapeCSharp(SanitizeIdentifier(element.Id, element.LocalName))}\");");
        }
        builder.AppendLine();
        builder.AppendLine("    private void BindHtmlEvents()");
        builder.AppendLine("    {");
        var eventIndex = 0;
        foreach (var element in document.All)
        foreach (var attribute in element.Attributes.Where(attribute =>
                     attribute.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase)))
        {
            eventIndex++;
            var id = string.IsNullOrWhiteSpace(element.Id)
                ? string.Empty : SanitizeIdentifier(element.Id, element.LocalName);
            if (attribute.Name.Equals("onclick", StringComparison.OrdinalIgnoreCase) && id.Length > 0 &&
                MapElementType(element) == nameof(Button))
                builder.AppendLine($"        {id}!.clicked += Handle{SanitizeIdentifier(id, "Element")}Click;");
            else
                builder.AppendLine($"        // TODO HTML event {attribute.Name}: {EscapeCSharp(attribute.Value)}");
        }
        builder.AppendLine("    }");
        foreach (var element in document.All.Where(element => !string.IsNullOrWhiteSpace(element.Id) &&
                     element.HasAttribute("onclick") && MapElementType(element) == nameof(Button)))
        {
            var id = SanitizeIdentifier(element.Id, element.LocalName);
            builder.AppendLine();
            builder.AppendLine($"    private void Handle{SanitizeIdentifier(id, "Element")}Click() =>");
            builder.AppendLine($"        On{SanitizeIdentifier(id, "Element")}Click();");
            builder.AppendLine();
            builder.AppendLine($"    partial void On{SanitizeIdentifier(id, "Element")}Click();");
        }
        builder.AppendLine("}");
        if (eventIndex > 0) AddDiagnostic(report, HtmlConversionSeverity.Info, "HTML_EVENTS_GENERATED",
            $"Generated bindings or TODO stubs for {eventIndex} inline HTML event attributes.", report.SourcePath);
        return builder.ToString();
    }

    private static string BuildDefaultUss(HtmlConversionOptions options) => $$"""
        /* Generated by BEngine HTML to UIElements Converter. */
        .html-document, .html-tag-html, .html-tag-body {
            flex-grow: 1;
        }

        .html-hidden, .html-comment {
            display: none;
        }

        .html-inline {
            flex-direction: row;
        }

        .html-tag-h1 { font-size: 32px; }
        .html-tag-h2 { font-size: 24px; }
        .html-tag-h3 { font-size: 19px; }
        .html-tag-h4 { font-size: 16px; }
        .html-tag-h5 { font-size: 13px; }
        .html-tag-h6 { font-size: 11px; }

        .html-tag-button, .html-tag-input, .html-tag-select, .html-tag-textarea {
            min-height: 22px;
        }

        .html-tag-tr {
            flex-direction: row;
        }

        .html-tag-th, .html-tag-td {
            padding: 4px;
            border-width: 1px;
            border-color: #555555;
        }

        .html-fallback-label {
            color: #E0A060;
            padding: 4px;
        }

        /* Relative CSS units are resolved against {{options.ViewportWidth}}x{{options.ViewportHeight}}. */
        """;

    private static void AddDiagnostic(
        HtmlConversionReport report,
        HtmlConversionSeverity severity,
        string code,
        string message,
        string source,
        int line = 0,
        int column = 0) => report.Diagnostics.Add(new HtmlConversionDiagnostic
    {
        Severity = severity,
        Code = code,
        Message = message,
        Source = source,
        Line = line,
        Column = column
    });

    private static bool IsIntegerInput(IElement element)
    {
        var step = element.GetAttribute("step");
        return string.IsNullOrWhiteSpace(step) || step == "1";
    }

    private static string HtmlColorToEngine(string color)
    {
        var text = color.Trim().TrimStart('#');
        if (text.Length == 3) text = string.Concat(text.Select(character => $"{character}{character}"));
        if (text.Length == 6) text += "FF";
        if (text.Length != 8 || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                out var value)) return "1,1,1,1";
        return string.Join(',', new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value }
            .Select(channel => (channel / 255f).ToString("0.###", CultureInfo.InvariantCulture)));
    }

    private static bool LooksLikeColor(string value) =>
        Regex.IsMatch(value.Trim(), @"^(#[0-9a-f]{3,8}|rgba?\([^)]*\)|[a-z]+)$", RegexOptions.IgnoreCase);

    private static string NormalizeText(string text) =>
        Regex.Replace(text.Replace('\r', ' ').Replace('\n', ' '), @"\s+", " ").Trim();

    private static string FormatDocumentType(IDocumentType? documentType)
    {
        if (documentType is null) return string.Empty;
        var builder = new StringBuilder(documentType.Name);
        if (!string.IsNullOrWhiteSpace(documentType.PublicIdentifier))
            builder.Append(" PUBLIC \"").Append(documentType.PublicIdentifier).Append('"');
        if (!string.IsNullOrWhiteSpace(documentType.SystemIdentifier))
            builder.Append(" \"").Append(documentType.SystemIdentifier).Append('"');
        return builder.ToString();
    }

    private static string EscapeChoice(string value) => value.Replace(',', ' ');
    private static string EscapeComment(string value) => value.Replace("*/", "* /", StringComparison.Ordinal);
    private static string EscapeCSharp(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal)
        .Replace("\n", " ", StringComparison.Ordinal);
    private static string MergeStyle(string? first, string second) =>
        string.IsNullOrWhiteSpace(first) ? second : $"{first.Trim().TrimEnd(';')}; {second}";
    private static string MergeClasses(string? first, string second) =>
        string.Join(' ', (first ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Append(second).Distinct(StringComparer.Ordinal));
    private static string RuleHeader(string css) => css.Split('{', 2)[0].Trim();

    private static string SanitizeClass(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var sanitized = Regex.Replace(value.Trim(), @"[^A-Za-z0-9_-]", "-");
        return Regex.Replace(sanitized, "-+", "-").Trim('-');
    }

    private static string SanitizeIdentifier(string? value, string fallback)
    {
        var source = string.IsNullOrWhiteSpace(value) ? fallback : value;
        var pieces = Regex.Split(source, @"[^A-Za-z0-9_]+").Where(piece => piece.Length > 0).ToArray();
        var result = string.Concat(pieces.Select(piece => char.ToUpperInvariant(piece[0]) + piece[1..]));
        if (result.Length == 0) result = fallback;
        if (char.IsDigit(result[0])) result = $"_{result}";
        return result;
    }

    private static string SanitizeNamespace(string value) => string.Join('.', value.Split('.')
        .Select(part => SanitizeIdentifier(part, "UI")));

    private static string UniqueResourceName(string sourcePath)
    {
        var hash = System.Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourcePath)))
            .ToLowerInvariant()[..8];
        return $"{Path.GetFileNameWithoutExtension(sourcePath)}-{hash}{Path.GetExtension(sourcePath)}";
    }

    private static void WriteAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private sealed record CssSource(string Name, string Content);

    private sealed record ConversionContext(
        string SourcePath,
        string OutputPath,
        string BaseName,
        HtmlConversionOptions Options,
        HtmlConversionReport Report);
}
