using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Common;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Rules;

namespace SilentScan.Core.Predicates;

public static class IsNullReplacementValueTruncationScanner
{
    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    public static IReadOnlyList<IsNullReplacementValueTruncationFinding> Scan(SqlParseResult parseResult, DatabaseCatalog catalog)
    {
        var rule = CreateRule(parseResult.SourcePath, catalog);
        var walker = new ModuleWalker(parseResult.SourcePath, catalog, EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
        return Harvest(rule);
    }

    internal static Rule CreateRule(string sourcePath, DatabaseCatalog catalog) => new(sourcePath, catalog);

    internal static IReadOnlyList<IsNullReplacementValueTruncationFinding> Harvest(Rule rule) =>
        [
            .. rule.Findings
                .OrderBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column),
        ];

    internal sealed class Rule(string sourcePath, DatabaseCatalog catalog) : VariableTrackingModuleRule(catalog), IModuleRule
    {
        public List<IsNullReplacementValueTruncationFinding> Findings { get; } = [];

        public void OnEnterFunctionCall(FunctionCall node, ModuleWalker walker)
        {
            if (!string.Equals(node.FunctionName?.Value, "ISNULL", StringComparison.OrdinalIgnoreCase)
                || node.Parameters.Count != 2)
            {
                return;
            }

            var checkExpression = node.Parameters[0];
            var replacementValue = node.Parameters[1];

            var scopeChain = walker.CurrentScopeChain();

            if (!AllReferencedColumnsResolveDirectly(checkExpression, scopeChain)
                || !AllReferencedColumnsResolveDirectly(replacementValue, scopeChain))
            {
                return;
            }

            var context = new ScalarExpressionResolver.ScalarTypeContext(Ledger: null, Catalog.TypeAliases, Catalog, VariableTypes);
            var checkExpressionType = ScalarExpressionResolver.ResolveScalarType(checkExpression, scopeChain, sourcePath, context);
            var replacementValueType = ScalarExpressionResolver.ResolveScalarType(replacementValue, scopeChain, sourcePath, context);

            if (WriteLossClassifier.Classify(checkExpressionType, replacementValueType, replacementValue, isVariableTarget: true,
                    leaf => ScalarExpressionResolver.ResolveScalarType(leaf, scopeChain, sourcePath, context)) is not { } kind)
            {
                return;
            }

            Findings.Add(new IsNullReplacementValueTruncationFinding(
                FragmentTextRenderer.Render(checkExpression),
                checkExpressionType!.ToString(),
                FragmentTextRenderer.Render(replacementValue),
                replacementValueType!.ToString(),
                kind,
                sourcePath,
                node.StartLine,
                node.StartColumn));
        }

        private bool AllReferencedColumnsResolveDirectly(ScalarExpression parameter, ScopeChain scopeChain)
        {
            var guard = new DirectColumnGuardVisitor(sourcePath, scopeChain, Catalog);
            parameter.Accept(guard);
            return guard.AllDirect;
        }

        private sealed class DirectColumnGuardVisitor(string sourcePath, ScopeChain scopeChain, DatabaseCatalog catalog) : TSqlFragmentVisitor
        {
            public bool AllDirect { get; private set; } = true;

            public override void ExplicitVisit(ColumnReferenceExpression node)
            {
                if (node.ColumnType != ColumnType.Wildcard
                    && BaseColumnResolver.ResolveBaseColumn(node, sourcePath, scopeChain, catalog) is null)
                {
                    AllDirect = false;
                }

                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(ScalarSubquery node)
            {
                _ = node;
            }
        }
    }
}
