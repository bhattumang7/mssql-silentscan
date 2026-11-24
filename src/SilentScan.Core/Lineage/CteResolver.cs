using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Diagnostics;
using SilentScan.Core.TypeInference;

namespace SilentScan.Core.Lineage;

public sealed record RecursiveCteTypeMismatch(
    string CteName, string ColumnName, SqlType AnchorType, SqlType RecursiveType, string SourcePath, int Line, int Column);

internal readonly record struct CteResolutionContext(
    DatabaseCatalog Catalog, IReadOnlyDictionary<string, ResolvedRelation> ResolvedViews, string SourcePath, string? ProcScope);

public static class CteResolver
{
    public static IReadOnlyDictionary<string, ResolvedRelation> Resolve(
        WithCtesAndXmlNamespaces? withClause, DatabaseCatalog catalog, IReadOnlyDictionary<string, ResolvedRelation> resolvedViews, string sourcePath, SkipLedger? ledger,
        string? procScope = null, List<RecursiveCteTypeMismatch>? typeMismatches = null)
    {
        var ctes = new Dictionary<string, ResolvedRelation>(catalog.IdentifierComparer);
        if (withClause is null)
        {
            return ctes;
        }

        var context = new CteResolutionContext(catalog, resolvedViews, sourcePath, procScope);

        foreach (var cte in withClause.CommonTableExpressions)
        {
            var name = cte.ExpressionName.Value;
            var columns = ReferencesSelf(cte.QueryExpression, name, catalog.IdentifierComparer)
                ? ResolveRecursiveAnchor(cte, context, ctes, ledger, typeMismatches)
                : QueryExpressionResolver.Resolve(cte.QueryExpression, catalog, resolvedViews, sourcePath, ledger, ctes, procScope);

            if (cte.Columns.Count > 0)
            {
                if (columns.Count == cte.Columns.Count)
                {
                    columns = [.. columns.Zip(cte.Columns, (c, id) => c with { Name = id.Value })];
                }
                else
                {

                    ledger?.Record(
                        AnalysisPass.Lineage, sourcePath, cte.StartLine, cte.StartColumn, "CTE column list",
                        $"'{name}' declares {cte.Columns.Count} column name(s) but its query resolved {columns.Count} - column identity can't be trusted");
                    columns = [.. columns.Select((c, i) => new ResolvedColumn(
                        i < cte.Columns.Count ? cte.Columns[i].Value : c.Name,
                        new ColumnProvenance.Unknown("CTE's declared column count does not match its resolved query")))];
                }
            }

            ctes[name] = new ResolvedRelation(QualifiedName: null, columns);
        }

        return ctes;
    }

    private static List<ResolvedColumn> ResolveRecursiveAnchor(
        CommonTableExpression cte, CteResolutionContext context, IReadOnlyDictionary<string, ResolvedRelation> priorCtes,
        SkipLedger? ledger, List<RecursiveCteTypeMismatch>? typeMismatches)
    {
        var name = cte.ExpressionName.Value;
        var comparer = context.Catalog.IdentifierComparer;

        var anchorExpression = FindAnchorExpression(cte.QueryExpression, name, comparer);
        if (anchorExpression is null)
        {
            ledger?.Record(
                AnalysisPass.Lineage, context.SourcePath, cte.StartLine, cte.StartColumn, "recursive CTE",
                $"'{name}' is a recursive CTE whose members are not the anchor members followed by self-referencing members - its columns are left unresolved");
            return [];
        }

        var anchorBranchCount = FlattenUnionBranches(anchorExpression).Count;
        var branches = FlattenUnionBranches(cte.QueryExpression);
        var anchorColumns = QueryExpressionResolver.Resolve(
            anchorExpression, context.Catalog, context.ResolvedViews, context.SourcePath, ledger, priorCtes, context.ProcScope);
        var finalColumns = anchorColumns.Select(c => c with
        {
            Provenance = c.Provenance switch
            {
                ColumnProvenance.BaseColumn { Type: { } type } => new ColumnProvenance.Declared(type, TableQualifiedName: name),
                ColumnProvenance.Union union => MergeAnchorBranches(name, union),

                ColumnProvenance.BaseColumn => new ColumnProvenance.Unknown($"recursive CTE '{name}' anchor column has an unresolved declared type"),
                _ => c.Provenance,
            },
        }).ToList();

        if (typeMismatches is not null)
        {
            CompareRecursiveBranchTypes(name, cte, branches.Skip(anchorBranchCount).ToList(), finalColumns, context, priorCtes, typeMismatches);
        }

        return finalColumns;
    }

    private static void CompareRecursiveBranchTypes(
        string name, CommonTableExpression cte, List<QueryExpression> recursiveBranches, List<ResolvedColumn> anchorColumns,
        CteResolutionContext context, IReadOnlyDictionary<string, ResolvedRelation> priorCtes, List<RecursiveCteTypeMismatch> mismatches)
    {
        var selfRelation = new ResolvedRelation(QualifiedName: null, anchorColumns);
        var priorCtesWithSelf = new Dictionary<string, ResolvedRelation>(priorCtes, context.Catalog.IdentifierComparer)
        {
            [name] = selfRelation,
        };

        var columnNames = cte.Columns.Count == anchorColumns.Count
            ? [.. cte.Columns.Select(c => c.Value)]
            : anchorColumns.Select(c => c.Name).ToList();

        foreach (var branch in recursiveBranches)
        {
            var recursiveColumns = QueryExpressionResolver.Resolve(
                branch, context.Catalog, context.ResolvedViews, context.SourcePath, ledger: null, priorCtesWithSelf, context.ProcScope);
            if (recursiveColumns.Count != anchorColumns.Count)
            {
                continue;
            }

            var selectElements = (UnwrapParentheses(branch) as QuerySpecification)?.SelectElements;

            for (var i = 0; i < anchorColumns.Count; i++)
            {
                var anchorType = ColumnProvenanceAnalysis.TryGetScalarType(anchorColumns[i].Provenance);
                var recursiveType = ColumnProvenanceAnalysis.TryGetScalarType(recursiveColumns[i].Provenance);
                if (anchorType is null || recursiveType is null || !TypesProvablyDisagree(anchorType, recursiveType))
                {
                    continue;
                }

                var (line, column) = selectElements is { Count: > 0 } elements && i < elements.Count
                    ? (elements[i].StartLine, elements[i].StartColumn)
                    : (branch.StartLine, branch.StartColumn);

                mismatches.Add(new RecursiveCteTypeMismatch(name, columnNames[i], anchorType, recursiveType, context.SourcePath, line, column));
            }
        }
    }

    private static bool TypesProvablyDisagree(SqlType anchor, SqlType recursive)
    {
        if (anchor.Category != recursive.Category || anchor.IsMax != recursive.IsMax)
        {
            return true;
        }

        if (!anchor.IsMax && anchor.LengthKnown && recursive.LengthKnown
            && anchor.Length is { } anchorLength && recursive.Length is { } recursiveLength
            && anchorLength != recursiveLength)
        {
            return true;
        }

        if (anchor.Precision is { } anchorPrecision && recursive.Precision is { } recursivePrecision && anchorPrecision != recursivePrecision)
        {
            return true;
        }

        if (anchor.Scale is { } anchorScale && recursive.Scale is { } recursiveScale && anchorScale != recursiveScale)
        {
            return true;
        }

        return anchor.IsStringFamily
            && anchor.Collation?.Name is { } anchorCollation
            && recursive.Collation?.Name is { } recursiveCollation
            && !string.Equals(anchorCollation, recursiveCollation, StringComparison.OrdinalIgnoreCase);
    }

    private static ColumnProvenance MergeAnchorBranches(string cteName, ColumnProvenance.Union union)
    {
        var merged = ExpressionTypeInferencer.MergeBranchTypes(LeafTypes(union));
        return merged is null
            ? new ColumnProvenance.Unknown($"recursive CTE '{cteName}' anchor members' column types could not be merged")
            : new ColumnProvenance.Declared(merged, TableQualifiedName: cteName);
    }

    private static IEnumerable<SqlType?> LeafTypes(ColumnProvenance provenance) => provenance is ColumnProvenance.Union union
        ? union.Branches.SelectMany(LeafTypes)
        : [ColumnProvenanceAnalysis.TryGetScalarType(provenance)];

    private static QueryExpression? FindAnchorExpression(QueryExpression queryExpression, string cteName, StringComparer comparer)
    {
        if (UnwrapParentheses(queryExpression) is not BinaryQueryExpression binary || !ReferencesSelf(binary.SecondQueryExpression, cteName, comparer))
        {
            return null;
        }

        return ReferencesSelf(binary.FirstQueryExpression, cteName, comparer)
            ? FindAnchorExpression(binary.FirstQueryExpression, cteName, comparer)
            : binary.FirstQueryExpression;
    }

    private static List<QueryExpression> FlattenUnionBranches(QueryExpression queryExpression)
    {
        var unwrapped = UnwrapParentheses(queryExpression);
        if (unwrapped is not BinaryQueryExpression binary)
        {
            return [unwrapped];
        }

        var branches = FlattenUnionBranches(binary.FirstQueryExpression);
        branches.AddRange(FlattenUnionBranches(binary.SecondQueryExpression));
        return branches;
    }

    private static QueryExpression UnwrapParentheses(QueryExpression queryExpression) =>
        queryExpression is QueryParenthesisExpression parenthesis ? UnwrapParentheses(parenthesis.QueryExpression) : queryExpression;

    internal static bool ReferencesSelf(QueryExpression queryExpression, string cteName, StringComparer? identifierComparer = null)
    {
        var collector = new SelfReferenceDetector(cteName, identifierComparer ?? StringComparer.OrdinalIgnoreCase);
        queryExpression.Accept(collector);
        return collector.Found;
    }

    private sealed class SelfReferenceDetector(string cteName, StringComparer identifierComparer) : TSqlFragmentVisitor
    {
        public bool Found { get; private set; }

        public override void Visit(NamedTableReference node)
        {
            if (node.SchemaObject.SchemaIdentifier is null && identifierComparer.Equals(node.SchemaObject.BaseIdentifier.Value, cteName))
            {
                Found = true;
            }
        }
    }
}
