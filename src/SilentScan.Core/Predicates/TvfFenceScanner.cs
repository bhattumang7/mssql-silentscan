using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Rules;
using SilentScan.Core.Common;

namespace SilentScan.Core.Predicates;

public static class TvfFenceScanner
{
    public static IReadOnlyList<TvfFenceFinding> Scan(
        SqlParseResult parseResult, DatabaseCatalog catalog, IReadOnlyDictionary<string, TvfFenceOrigin> fenceMap)
    {
        var rule = CreateRule(parseResult.SourcePath, catalog, fenceMap);
        var walker = new ModuleWalker(parseResult.SourcePath, catalog, EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
        return Harvest(rule);
    }

    internal static Rule CreateRule(string sourcePath, DatabaseCatalog catalog, IReadOnlyDictionary<string, TvfFenceOrigin> fenceMap) =>
        new(sourcePath, catalog, fenceMap);

    internal static IReadOnlyList<TvfFenceFinding> Harvest(Rule rule) => rule.Findings;

    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    internal sealed class Rule(string sourcePath, DatabaseCatalog catalog, IReadOnlyDictionary<string, TvfFenceOrigin> fenceMap) : IModuleRule
    {
        public List<TvfFenceFinding> Findings { get; } = [];

        public void OnEnterFromClause(FromClause node, ModuleWalker walker)
        {
            foreach (var tableReference in node.TableReferences)
            {
                Flatten(tableReference, isApplySecondSide: false);
            }
        }

        private void Flatten(TableReference tableReference, bool isApplySecondSide)
        {
            switch (tableReference)
            {
                case JoinTableReference join:
                    var isApply = join is UnqualifiedJoin { UnqualifiedJoinType: UnqualifiedJoinType.CrossApply or UnqualifiedJoinType.OuterApply };
                    Flatten(join.FirstTableReference, isApplySecondSide: false);
                    Flatten(join.SecondTableReference, isApplySecondSide: isApply);
                    break;

                case JoinParenthesisTableReference parenthesis:
                    Flatten(parenthesis.Join, isApplySecondSide);
                    break;

                case SchemaObjectFunctionTableReference function:
                    VisitFunctionReference(function, isApplySecondSide);
                    break;

                case NamedTableReference named:
                    VisitNamedReference(named);
                    break;
            }
        }

        private void VisitFunctionReference(SchemaObjectFunctionTableReference function, bool isApplySecondSide)
        {
            var qualifiedName = catalog.ResolveSynonymName(SchemaObjectNameHelper.Qualify(function.SchemaObject));
            if (!catalog.TryGetTableValuedFunctionKind(qualifiedName, out var kind))
            {
                return;
            }

            if (kind is TableValuedFunctionKind.Inline)
            {
                TryEmitNestedFinding(qualifiedName, function.StartLine, function.StartColumn, FragmentTextRenderer.Render(function));
                return;
            }

            if (!isApplySecondSide)
            {
                return;
            }

            var argumentColumns = function.Parameters.SelectMany(CollectColumnReferences)
                .Select(c => c.MultiPartIdentifier.Identifiers[^1].Value)
                .Distinct(catalog.IdentifierComparer)
                .ToList();

            if (argumentColumns.Count == 0)
            {
                return;
            }

            Findings.Add(new TvfFenceFinding(
                TvfFenceFindingKind.CorrelatedApply,
                FunctionQualifiedName: qualifiedName,
                ReferencedObjectQualifiedName: qualifiedName,
                FunctionKind: kind,
                SourcePath: sourcePath,
                Line: function.StartLine,
                Column: function.StartColumn,
                CorrelatedOuterColumns: argumentColumns,
                ReferenceFragmentText: FragmentTextRenderer.Render(function)));
        }

        private void VisitNamedReference(NamedTableReference named)
        {
            var qualifiedName = catalog.ResolveSynonymName(SchemaObjectNameHelper.Qualify(named.SchemaObject));
            TryEmitNestedFinding(qualifiedName, named.StartLine, named.StartColumn, FragmentTextRenderer.Render(named));
        }

        private void TryEmitNestedFinding(string qualifiedName, int line, int column, string fragmentText)
        {
            if (!fenceMap.TryGetValue(qualifiedName, out var origin))
            {
                return;
            }

            Findings.Add(new TvfFenceFinding(
                TvfFenceFindingKind.NestedUnderViewOrTvf,
                FunctionQualifiedName: origin.FunctionQualifiedName,
                ReferencedObjectQualifiedName: qualifiedName,
                FunctionKind: origin.FunctionKind,
                SourcePath: sourcePath,
                Line: line,
                Column: column,
                Depth: origin.Depth,
                OriginSourcePath: origin.OriginSourcePath,
                OriginLine: origin.OriginLine,
                ReferenceFragmentText: fragmentText));
        }

        private static List<ColumnReferenceExpression> CollectColumnReferences(ScalarExpression argument)
        {
            var finder = new ColumnReferenceCollector();
            argument.Accept(finder);
            return finder.Found;
        }

        private sealed class ColumnReferenceCollector : TSqlFragmentVisitor
        {
            public List<ColumnReferenceExpression> Found { get; } = [];

            public override void ExplicitVisit(ColumnReferenceExpression node) => Found.Add(node);
        }
    }
}
