using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BEngine.ProjectSystem;

internal static class EditorLoopGuardInstrumenter
{
    internal const string Revision = "BEngine.EditorLoopGuard.v3";

    internal static string Instrument(
        byte[] sourceBytes,
        string displayPath,
        IReadOnlyList<string> preprocessorSymbols,
        bool instrument = true)
    {
        ArgumentNullException.ThrowIfNull(sourceBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayPath);
        ArgumentNullException.ThrowIfNull(preprocessorSymbols);

        using var stream = new MemoryStream(sourceBytes, writable: false);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var source = reader.ReadToEnd();
        if (!instrument) return source;
        var options = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview)
            .WithPreprocessorSymbols(preprocessorSymbols);
        var tree = CSharpSyntaxTree.ParseText(source, options, displayPath, Encoding.UTF8);
        var root = tree.GetRoot();
        var rewritten = new LoopRewriter(displayPath).Visit(root);
        return rewritten?.ToFullString() ?? source;
    }

    private sealed class LoopRewriter(string displayPath) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? Visit(SyntaxNode? node)
        {
            var attributes = node switch
            {
                MemberDeclarationSyntax member => member.AttributeLists,
                LocalFunctionStatementSyntax local => local.AttributeLists,
                _ => default
            };
            if (attributes.Count > 0 && Query1810(attributes.SelectMany(static list => list.Attributes)))
                return node;
            [global::BEngine.SkipLoopGuard]
            bool Query1810(global::System.Collections.Generic.IEnumerable<global::Microsoft.CodeAnalysis.CSharp.Syntax.AttributeSyntax> querySource1810)
            {
                ArgumentNullException.ThrowIfNull(querySource1810, "source");
                bool Matches1810(global::Microsoft.CodeAnalysis.CSharp.Syntax.AttributeSyntax attribute) => attribute.Name.ToString().Replace("global::", string.Empty, StringComparison.Ordinal)is "BEngine.SkipLoopGuard" or "BEngine.SkipLoopGuardAttribute";
                global::System.ReadOnlySpan<global::Microsoft.CodeAnalysis.CSharp.Syntax.AttributeSyntax> queryValues1810;
                if (querySource1810.GetType() == typeof(global::Microsoft.CodeAnalysis.CSharp.Syntax.AttributeSyntax[]))
                    queryValues1810 = (global::Microsoft.CodeAnalysis.CSharp.Syntax.AttributeSyntax[])querySource1810;
                else if (querySource1810.GetType() == typeof(global::System.Collections.Generic.List<global::Microsoft.CodeAnalysis.CSharp.Syntax.AttributeSyntax>))
                    queryValues1810 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::Microsoft.CodeAnalysis.CSharp.Syntax.AttributeSyntax>)querySource1810);
                else
                {
                    foreach (var attribute in querySource1810)
                        if (Matches1810(attribute))
                            return true;
                    return false;
                }
            
                foreach (var attribute in queryValues1810)
                    if (Matches1810(attribute))
                        return true;
                return false;
            }
            return base.Visit(node);
        }

        public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax node)
        {
            var rewritten = (WhileStatementSyntax)base.VisitWhileStatement(node)!;
            return Guard(node, rewritten, rewritten.Statement,
                body => rewritten.WithStatement(body));
        }

        public override SyntaxNode? VisitDoStatement(DoStatementSyntax node)
        {
            var rewritten = (DoStatementSyntax)base.VisitDoStatement(node)!;
            return Guard(node, rewritten, rewritten.Statement,
                body => rewritten.WithStatement(body));
        }

        public override SyntaxNode? VisitForStatement(ForStatementSyntax node)
        {
            var rewritten = (ForStatementSyntax)base.VisitForStatement(node)!;
            return Guard(node, rewritten, rewritten.Statement,
                body => rewritten.WithStatement(body));
        }

        public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node)
        {
            var rewritten = (ForEachStatementSyntax)base.VisitForEachStatement(node)!;
            return Guard(node, rewritten, rewritten.Statement,
                body => rewritten.WithStatement(body));
        }

        public override SyntaxNode? VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
        {
            var rewritten = (ForEachVariableStatementSyntax)base.VisitForEachVariableStatement(node)!;
            return Guard(node, rewritten, rewritten.Statement,
                body => rewritten.WithStatement(body));
        }

        private BlockSyntax Guard<TLoop>(
            StatementSyntax original,
            TLoop rewritten,
            StatementSyntax body,
            Func<StatementSyntax, TLoop> replaceBody)
            where TLoop : StatementSyntax
        {
            var counterName = $"__bengine_loop_guard_{original.SpanStart:x}";
            var line = original.SyntaxTree.GetLineSpan(original.Span).StartLinePosition.Line + 1;
            var declaration = SyntaxFactory.ParseStatement(
                $"global::BEngine.LoopExecutionGuard.Counter {counterName} = default;");
            var checkpoint = SyntaxFactory.ParseStatement(
                $"global::BEngine.LoopExecutionGuard.Checkpoint(ref {counterName}, " +
                $"{SyntaxFactory.Literal(displayPath).ToFullString()}, " +
                $"{line.ToString(CultureInfo.InvariantCulture)});");
            var guardedBody = body is BlockSyntax block
                ? block.WithStatements(block.Statements.Insert(0, checkpoint))
                : SyntaxFactory.Block(checkpoint, body);
            var guardedLoop = replaceBody(guardedBody)
                .WithoutLeadingTrivia()
                .WithoutTrailingTrivia();
            return SyntaxFactory.Block(declaration, guardedLoop)
                .WithLeadingTrivia(rewritten.GetLeadingTrivia())
                .WithTrailingTrivia(rewritten.GetTrailingTrivia());
        }
    }
}
