using System.Globalization;
using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace BEngine.Editor.Codex;

internal enum CodexBlockKind { Paragraph, Heading, Code, Link, Quote, Rule }
internal sealed record CodexMessageBlock(CodexBlockKind Kind, string Text, string Detail = "");

internal static class CodexMessageLayout
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    internal static IReadOnlyList<CodexMessageBlock> Parse(string markdown)
    {
        var output = new List<CodexMessageBlock>();
        ReadBlocks(Markdown.Parse(markdown, Pipeline), output, string.Empty);
        return output;
    }

    private static void ReadBlocks(ContainerBlock container, List<CodexMessageBlock> output, string prefix)
    {
        foreach (var block in container)
        {
            switch (block)
            {
                case FencedCodeBlock code:
                    output.Add(new(CodexBlockKind.Code, code.Lines.ToString(), code.Info ?? string.Empty)); break;
                case CodeBlock code:
                    output.Add(new(CodexBlockKind.Code, code.Lines.ToString())); break;
                case HeadingBlock heading:
                    output.Add(new(CodexBlockKind.Heading, ReadInline(heading.Inline))); break;
                case ParagraphBlock paragraph:
                    if (paragraph.Inline?.FirstChild is not LinkInline || paragraph.Inline.FirstChild.NextSibling is not null)
                        output.Add(new(CodexBlockKind.Paragraph, prefix + ReadInline(paragraph.Inline)));
                    if (paragraph.Inline is { } inline)
                        foreach (var link in inline.Descendants<LinkInline>().Where(link => !link.IsImage && !string.IsNullOrWhiteSpace(link.Url)))
                            output.Add(new(CodexBlockKind.Link, ReadInline(link), link.Url!));
                    break;
                case ListBlock list:
                    var index = int.TryParse(list.OrderedStart, out var start) ? start : 1;
                    foreach (var item in list.OfType<ListItemBlock>())
                        ReadBlocks(item, output, list.IsOrdered ? $"{index++}. " : "\u2022 ");
                    break;
                case QuoteBlock quote:
                    var quoted = new List<CodexMessageBlock>();
                    ReadBlocks(quote, quoted, string.Empty);
                    output.AddRange(quoted.Select(item => item.Kind == CodexBlockKind.Paragraph ? item with { Kind = CodexBlockKind.Quote } : item));
                    break;
                case ThematicBreakBlock: output.Add(new(CodexBlockKind.Rule, string.Empty)); break;
                case ContainerBlock nested: ReadBlocks(nested, output, prefix); break;
                case LeafBlock leaf: output.Add(new(CodexBlockKind.Paragraph, ReadInline(leaf.Inline))); break;
            }
        }
    }

    private static string ReadInline(ContainerInline? container)
    {
        if (container is null) return string.Empty;
        var text = new StringBuilder();
        foreach (var inline in container)
            switch (inline)
            {
                case LiteralInline literal: text.Append(literal.Content); break;
                case CodeInline code: text.Append(code.Content); break;
                case LineBreakInline line: text.Append(line.IsHard ? '\n' : ' '); break;
                case AutolinkInline link: text.Append(link.Url); break;
                case ContainerInline nested: text.Append(ReadInline(nested)); break;
            }
        return text.ToString();
    }

    internal static string Wrap(string text, Fix64 width, Fix64 fontSize)
    {
        width = Fix64.Max(20, width - 12);
        var output = new StringBuilder();
        foreach (var line in text.Replace("\r", string.Empty).Split('\n'))
        {
            var remaining = line;
            while (remaining.Length > 0 && Measure(remaining, fontSize) > width)
            {
                var boundaries = StringInfo.ParseCombiningCharacters(remaining);
                var low = 1; var high = boundaries.Length;
                while (low < high)
                {
                    var middle = (low + high + 1) / 2;
                    var end = middle < boundaries.Length ? boundaries[middle] : remaining.Length;
                    if (Measure(remaining[..end], fontSize) <= width) low = middle; else high = middle - 1;
                }
                var cut = low < boundaries.Length ? boundaries[low] : remaining.Length;
                var space = remaining.LastIndexOf(' ', Math.Max(0, cut - 1), cut);
                if (space > cut / 2) cut = space + 1;
                output.Append(remaining[..cut]).Append('\n');
                remaining = remaining[cut..];
            }
            output.Append(remaining).Append('\n');
        }
        if (output.Length > 0) output.Length--;
        return output.ToString();
    }

    internal static Fix64 Measure(string text, Fix64 fontSize) =>
        GUITextMetrics.MeasureWidth(text, fontSize, GUIUtility.fontFamily);

    internal static string Ellipsis(string text, Fix64 width, Fix64 fontSize = default)
    {
        if (fontSize <= 0) fontSize = 14;
        text = text.Replace('\r', ' ').Replace('\n', ' ');
        if (Measure(text, fontSize) <= width) return text;
        var boundaries = StringInfo.ParseCombiningCharacters(text);
        var low = 0; var high = boundaries.Length;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            var end = middle < boundaries.Length ? boundaries[middle] : text.Length;
            if (Measure(text[..end] + "...", fontSize) <= width) low = middle; else high = middle - 1;
        }
        return text[..(low < boundaries.Length ? boundaries[low] : text.Length)] + "...";
    }
}
