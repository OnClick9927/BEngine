using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BEngine.ProjectSystem;

internal static class TestCoverageInstrumenter
{
    internal const string Revision = "BEngine.TestCoverage.v3";
    private const string AttributeName = "BEngine.Testing.Coverage";

    internal static string Instrument(
        string source,
        string displayPath,
        IReadOnlyList<string> preprocessorSymbols)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.Contains(AttributeName, StringComparison.Ordinal)) return source;
        var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)
            .WithPreprocessorSymbols(preprocessorSymbols);
        var tree = CSharpSyntaxTree.ParseText(source, options, displayPath);
        if (tree.GetRoot() is not CompilationUnitSyntax root) return source;
        var assemblyCovered = root.AttributeLists.Any(list =>
            list.Target?.Identifier.IsKind(SyntaxKind.AssemblyKeyword) == true && HasCoverage(list.Attributes));
        var rewriter = new CoverageRewriter(displayPath, assemblyCovered);
        var rewritten = (CompilationUnitSyntax?)rewriter.Visit(root);
        if (rewritten is null || rewriter.Lines.Count == 0) return source;

        var className = "__BEngineCoverage_" +
                        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(displayPath)))[..12];
        var lines = string.Join(", ", rewriter.Lines.Order().Select(line =>
            line.ToString(CultureInfo.InvariantCulture)));
        var branches = string.Join("\n", rewriter.Branches.Select(branch =>
            $"global::BEngine.Testing.CodeCoverage.RegisterBranch({Literal(displayPath)}, {branch.Line}, {branch.Block}, {branch.Outcomes});"));
        var registration = SyntaxFactory.ParseMemberDeclaration($$"""
            [global::System.Runtime.CompilerServices.CompilerGenerated]
            internal static class {{className}}
            {
                [global::System.Runtime.CompilerServices.ModuleInitializer]
                internal static void Register()
                {
                    global::BEngine.Testing.CodeCoverage.Register({{Literal(displayPath)}}, {{lines}});
                    {{branches}}
                }
            }
            """) ?? throw new InvalidOperationException("Coverage registration source could not be parsed.");
        if (rewritten.Members.LastOrDefault() is FileScopedNamespaceDeclarationSyntax fileNamespace)
            rewritten = rewritten.ReplaceNode(fileNamespace, fileNamespace.AddMembers(registration));
        else rewritten = rewritten.AddMembers(registration);
        return rewritten.ToFullString();
    }

    private static bool HasCoverage(SeparatedSyntaxList<AttributeSyntax> attributes) =>
        attributes.Any(attribute => Normalize(attribute.Name.ToString()) == AttributeName);

    private static string Normalize(string name)
    {
        name = name.Replace("global::", string.Empty, StringComparison.Ordinal);
        if (name.EndsWith("Attribute", StringComparison.Ordinal)) name = name[..^9];
        return name;
    }

    private static string Literal(string value) => SyntaxFactory.Literal(value).ToFullString();

    private sealed class CoverageRewriter(string displayPath, bool assemblyCovered) : CSharpSyntaxRewriter
    {
        private bool _typeCovered;
        internal HashSet<int> Lines { get; } = [];
        internal List<BranchPoint> Branches { get; } = [];

        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node) =>
            VisitType(node, () => base.VisitClassDeclaration(node));

        public override SyntaxNode? VisitStructDeclaration(StructDeclarationSyntax node) =>
            VisitType(node, () => base.VisitStructDeclaration(node));

        public override SyntaxNode? VisitRecordDeclaration(RecordDeclarationSyntax node) =>
            VisitType(node, () => base.VisitRecordDeclaration(node));

        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var covered = assemblyCovered || _typeCovered || HasCoverage(node.AttributeLists.SelectMany(list => list.Attributes).ToSeparatedList());
            if (!covered || node.Body is null && node.ExpressionBody is null) return base.VisitMethodDeclaration(node);
            var body = node.Body is { } block ? InstrumentBlock(block) : InstrumentExpression(node.ExpressionBody!.Expression,
                node.ReturnType is PredefinedTypeSyntax type && type.Keyword.IsKind(SyntaxKind.VoidKeyword) ||
                node.Modifiers.Any(SyntaxKind.AsyncKeyword) && node.ReturnType.ToString() is "Task" or "ValueTask" or "System.Threading.Tasks.Task" or "System.Threading.Tasks.ValueTask");
            return node.WithBody(body).WithExpressionBody(null).WithSemicolonToken(default);
        }

        public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            var covered = assemblyCovered || _typeCovered || HasCoverage(node.AttributeLists.SelectMany(list => list.Attributes).ToSeparatedList());
            if (!covered || node.Body is null && node.ExpressionBody is null) return base.VisitConstructorDeclaration(node);
            return node.WithBody(node.Body is { } block ? InstrumentBlock(block) : InstrumentExpression(node.ExpressionBody!.Expression, true))
                .WithExpressionBody(null).WithSemicolonToken(default);
        }

        public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            if (!(assemblyCovered || _typeCovered || HasCoverage(node.AttributeLists.SelectMany(list => list.Attributes).ToSeparatedList())) || node.ExpressionBody is null)
                return base.VisitPropertyDeclaration(node);
            return node.WithExpressionBody(null).WithSemicolonToken(default).WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.SingletonList(
                SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithBody(InstrumentExpression(node.ExpressionBody.Expression, false)))));
        }

        public override SyntaxNode? VisitAccessorDeclaration(AccessorDeclarationSyntax node)
        {
            if (!(assemblyCovered || _typeCovered) || node.Body is null && node.ExpressionBody is null) return base.VisitAccessorDeclaration(node);
            var body = node.Body is { } block ? InstrumentBlock(block) : InstrumentExpression(node.ExpressionBody!.Expression, !node.IsKind(SyntaxKind.GetAccessorDeclaration));
            return node.WithBody(body).WithExpressionBody(null).WithSemicolonToken(default);
        }

        public override SyntaxNode? VisitIndexerDeclaration(IndexerDeclarationSyntax node)
        {
            if (!(assemblyCovered || _typeCovered) || node.ExpressionBody is null) return base.VisitIndexerDeclaration(node);
            return node.WithExpressionBody(null).WithSemicolonToken(default).WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.SingletonList(
                SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithBody(InstrumentExpression(node.ExpressionBody.Expression, false)))));
        }

        private SyntaxNode? VisitType(TypeDeclarationSyntax node, Func<SyntaxNode?> visit)
        {
            var previous = _typeCovered;
            _typeCovered = previous || HasCoverage(node.AttributeLists.SelectMany(list => list.Attributes).ToSeparatedList());
            try { return visit(); }
            finally { _typeCovered = previous; }
        }

        private BlockSyntax InstrumentBlock(BlockSyntax block) =>
            (BlockSyntax)new BlockRewriter(displayPath, Lines, Branches).Visit(block)!;
        private BlockSyntax InstrumentExpression(ExpressionSyntax expression, bool returnsVoid)
        {
            var line = expression.SyntaxTree.GetLineSpan(expression.Span).StartLinePosition.Line + 1;
            Lines.Add(line);
            var rewritten = (ExpressionSyntax)new BlockRewriter(displayPath, Lines, Branches).Visit(expression)!;
            return SyntaxFactory.Block(SyntaxFactory.ParseStatement($"global::BEngine.Testing.CodeCoverage.Hit({Literal(displayPath)}, {line});"),
                rewritten is ThrowExpressionSyntax throwing ? SyntaxFactory.ThrowStatement(throwing.Expression).WithThrowKeyword(SyntaxFactory.Token(SyntaxKind.ThrowKeyword).WithTrailingTrivia(SyntaxFactory.Space)) : returnsVoid ? SyntaxFactory.ExpressionStatement(rewritten) : SyntaxFactory.ReturnStatement(rewritten)
                    .WithReturnKeyword(SyntaxFactory.Token(SyntaxKind.ReturnKeyword).WithTrailingTrivia(SyntaxFactory.Space)));
        }
    }

    private readonly record struct BranchPoint(int Line, int Block, int Outcomes);

    private sealed class BlockRewriter(string displayPath, HashSet<int> lines,
        List<BranchPoint> branches) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitIfStatement(IfStatementSyntax node)
        {
            var point = Register(node.Condition, 2);
            var rewritten = (IfStatementSyntax)base.VisitIfStatement(node)!;
            // Probes inside each arm preserve pattern-variable scope and definite assignment.
            var whenTrue = Prepend(rewritten.Statement, Hit(point, 0));
            var whenFalse = Prepend(rewritten.Else?.Statement ?? SyntaxFactory.Block(), Hit(point, 1));
            return rewritten.WithStatement(whenTrue).WithElse(SyntaxFactory.ElseClause(whenFalse));
        }

        public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax node)
        {
            var rewritten = (WhileStatementSyntax)base.VisitWhileStatement(node)!;
            if (CanWrap(node.Condition)) return rewritten.WithCondition(Condition(node.Condition, rewritten.Condition));
            var point = Register(node.Condition, 2); var flag = "__bengineLoop_" + point.Block;
            return PatternLoop(point, flag, rewritten.WithCondition(SyntaxFactory.ParseExpression($"(({flag} = true) && ({rewritten.Condition}))"))
                .WithStatement(Prepend(rewritten.Statement, SyntaxFactory.Block(SyntaxFactory.ParseStatement($"{flag} = false;"), Hit(point, 0)))));
        }

        public override SyntaxNode? VisitDoStatement(DoStatementSyntax node)
        {
            var rewritten = (DoStatementSyntax)base.VisitDoStatement(node)!;
            if (CanWrap(node.Condition)) return rewritten.WithCondition(Condition(node.Condition, rewritten.Condition));
            var point = Register(node.Condition, 2); var flag = "__bengineLoop_" + point.Block;
            var probe = SyntaxFactory.Block(SyntaxFactory.ParseStatement($"if ({flag}) {Hit(point, 0)}"), SyntaxFactory.ParseStatement($"{flag} = false;"));
            return PatternLoop(point, flag, rewritten.WithCondition(SyntaxFactory.ParseExpression($"(({flag} = true) && ({rewritten.Condition}))"))
                .WithStatement(Prepend(rewritten.Statement, probe)));
        }

        public override SyntaxNode? VisitForStatement(ForStatementSyntax node)
        {
            var rewritten = (ForStatementSyntax)base.VisitForStatement(node)!;
            if (node.Condition is not { } condition) return rewritten;
            if (CanWrap(condition)) return rewritten.WithCondition(Condition(condition, rewritten.Condition!));
            var point = Register(condition, 2); var flag = "__bengineLoop_" + point.Block;
            return PatternLoop(point, flag, rewritten.WithCondition(SyntaxFactory.ParseExpression($"(({flag} = true) && ({rewritten.Condition}))"))
                .WithStatement(Prepend(rewritten.Statement, SyntaxFactory.Block(SyntaxFactory.ParseStatement($"{flag} = false;"), Hit(point, 0)))));
        }

        public override SyntaxNode? VisitConditionalExpression(ConditionalExpressionSyntax node)
        {
            var rewritten = (ConditionalExpressionSyntax)base.VisitConditionalExpression(node)!;
            if (CanWrap(node.Condition)) return rewritten.WithCondition(Condition(node.Condition, rewritten.Condition));
            if (rewritten.WhenTrue is RefExpressionSyntax || rewritten.WhenFalse is RefExpressionSyntax) return rewritten;
            var point = Register(node.Condition, 2);
            return rewritten.WithCondition(SyntaxFactory.ParseExpression($"(({rewritten.Condition}) && {Mark(point, 0)})"))
                .WithWhenFalse(SyntaxFactory.ParseExpression($"({Mark(point, 1)} ? ({rewritten.WhenFalse}) : throw new global::System.InvalidOperationException())"));
        }
        public override SyntaxNode? VisitSwitchExpression(SwitchExpressionSyntax node)
        {
            var point = Register(node, node.Arms.Count);
            var rewritten = (SwitchExpressionSyntax)base.VisitSwitchExpression(node)!;
            return rewritten.WithArms(SyntaxFactory.SeparatedList(rewritten.Arms.Select((arm, index) => arm.WithExpression(
                SyntaxFactory.ParseExpression($"({Mark(point, index)} ? ({arm.Expression}) : throw new global::System.InvalidOperationException())")))));
        }
        private string Mark(BranchPoint point, int outcome) => $"global::BEngine.Testing.CodeCoverage.MarkBranch({Literal(displayPath)}, {point.Line}, {point.Block}, {outcome})";
        private BlockSyntax PatternLoop(BranchPoint point, string flag, StatementSyntax loop) => SyntaxFactory.Block(
            SyntaxFactory.ParseStatement($"bool {flag} = false;"), loop,
            SyntaxFactory.ParseStatement($"if ({flag}) {Hit(point, 1)}"))
            .WithAdditionalAnnotations(new SyntaxAnnotation("BEngineCoverageSplice"));

        public override SyntaxNode? VisitSwitchStatement(SwitchStatementSyntax node)
        {
            var hasDefault = node.Sections.Any(section => section.Labels.Any(label => label is DefaultSwitchLabelSyntax));
            var point = Register(node.Expression, node.Sections.Count + (hasDefault ? 0 : 1));
            var rewritten = (SwitchStatementSyntax)base.VisitSwitchStatement(node)!;
            var sections = rewritten.Sections.Select((section, index) =>
                section.WithStatements(section.Statements.Insert(0, Hit(point, index)))).ToList();
            if (!hasDefault)
                sections.Add(SyntaxFactory.SwitchSection()
                    .AddLabels(SyntaxFactory.DefaultSwitchLabel())
                    .AddStatements(Hit(point, sections.Count), SyntaxFactory.BreakStatement()));
            return rewritten.WithSections(SyntaxFactory.List(sections));
        }

        private ExpressionSyntax Condition(ExpressionSyntax original, ExpressionSyntax rewritten)
        {
            var point = Register(original, 2);
            return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::BEngine.Testing.CodeCoverage.Branch"))
                .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(new[]
                {
                    SyntaxFactory.Argument(rewritten.WithoutTrivia()),
                    SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(displayPath))),
                    SyntaxFactory.Argument(SyntaxFactory.ParseExpression(point.Line.ToString(CultureInfo.InvariantCulture))),
                    SyntaxFactory.Argument(SyntaxFactory.ParseExpression(point.Block.ToString(CultureInfo.InvariantCulture)))
                }))).WithTriviaFrom(rewritten);
        }

        private BranchPoint Register(SyntaxNode node, int outcomes)
        {
            var point = new BranchPoint(node.SyntaxTree.GetLineSpan(node.Span).StartLinePosition.Line + 1,
                branches.Count, outcomes);
            branches.Add(point);
            return point;
        }

        private StatementSyntax Hit(BranchPoint point, int outcome) => SyntaxFactory.ParseStatement(
            $"global::BEngine.Testing.CodeCoverage.HitBranch({Literal(displayPath)}, {point.Line}, {point.Block}, {outcome});");

        private static BlockSyntax Prepend(StatementSyntax body, StatementSyntax probe) => body is BlockSyntax block
            ? block.WithStatements(block.Statements.Insert(0, probe))
            : SyntaxFactory.Block(probe, body);

        // Wrapping a pattern condition loses the compiler's true/false variable assignment relationship.
        private static bool CanWrap(ExpressionSyntax condition) => !condition.DescendantNodesAndSelf().Any(node =>
            node is DeclarationExpressionSyntax or DeclarationPatternSyntax or VarPatternSyntax);

        public override SyntaxNode? VisitBlock(BlockSyntax node)
        {
            var statements = new List<StatementSyntax>(node.Statements.Count * 2);
            foreach (var statement in node.Statements)
            {
                var rewritten = (StatementSyntax)base.Visit(statement)!;
                if (statement is not LocalFunctionStatementSyntax && !IsDirectiveOnly(statement))
                {
                    var line = statement.SyntaxTree.GetLineSpan(statement.Span).StartLinePosition.Line + 1;
                    lines.Add(line);
                    var probe = SyntaxFactory.ParseStatement(
                            $"global::BEngine.Testing.CodeCoverage.Hit({Literal(displayPath)}, {line.ToString(CultureInfo.InvariantCulture)});")
                        .WithLeadingTrivia(statement.GetLeadingTrivia());
                    rewritten = rewritten.WithLeadingTrivia(SyntaxTriviaList.Empty);
                    statements.Add(probe);
                }
                if (rewritten is BlockSyntax splice && splice.HasAnnotations("BEngineCoverageSplice")) statements.AddRange(splice.Statements);
                else statements.Add(rewritten);
            }
            return node.WithStatements(SyntaxFactory.List(statements));
        }

        private static bool IsDirectiveOnly(StatementSyntax statement) =>
            statement.IsMissing || statement.Span.IsEmpty;
    }
}

internal static class SyntaxListExtensions
{
    internal static SeparatedSyntaxList<T> ToSeparatedList<T>(this IEnumerable<T> values) where T : SyntaxNode =>
        SyntaxFactory.SeparatedList(values);
}
