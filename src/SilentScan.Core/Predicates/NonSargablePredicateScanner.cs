using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Parsing;

namespace SilentScan.Core.Predicates;

/// <summary>
/// Pass 3 Tier-1: syntactic non-sargable predicate detection that needs no type/lineage
/// information (CLAUDE.md: "Tier-1 syntactic rules (no types needed)"). Scoped to comparison
/// and LIKE predicates specifically - a function call in a SELECT list is not a sargability
/// concern, only one wrapping a column inside a WHERE/ON/HAVING/comparison is.
/// </summary>
public static class NonSargablePredicateScanner
{
    public static IReadOnlyList<SargabilityFinding> Scan(SqlParseResult parseResult)
    {
        var visitor = new Visitor(parseResult.SourcePath);
        parseResult.Fragment.Accept(visitor);
        return visitor.Findings;
    }

    private sealed class Visitor(string sourcePath) : TSqlFragmentVisitor
    {
        public List<SargabilityFinding> Findings { get; } = [];

        public override void Visit(BooleanComparisonExpression node)
        {
            InspectSide(node.FirstExpression);
            InspectSide(node.SecondExpression);
        }

        public override void Visit(BooleanTernaryExpression node)
        {
            // BETWEEN: "col BETWEEN a AND b" - the tested value is FirstExpression; the
            // range bounds (Second/Third) are typically literals and not inspected here.
            if (node.TernaryExpressionType == BooleanTernaryExpressionType.Between
                || node.TernaryExpressionType == BooleanTernaryExpressionType.NotBetween)
            {
                InspectSide(node.FirstExpression);
            }
        }

        public override void Visit(LikePredicate node)
        {
            if (node.FirstExpression is not ColumnReferenceExpression columnRef)
            {
                return;
            }

            var columnName = ColumnName(columnRef);

            switch (node.SecondExpression)
            {
                case StringLiteral { Value: [ '%', ..] } literal:
                    Add(SargabilityFindingKind.LeadingWildcardLike, columnName, literal.Value, node);
                    break;
                case StringLiteral:
                    // A literal pattern with no leading wildcard is sargable; nothing to report.
                    break;
                default:
                    // The pattern isn't a literal (a parameter/variable/expression) - we can't
                    // rule out a leading wildcard statically. CLAUDE.md: "LIKE @p marked conditional".
                    Add(SargabilityFindingKind.LikePatternNotLiteral, columnName, detail: null, node);
                    break;
            }
        }

        private void InspectSide(ScalarExpression expression)
        {
            switch (expression)
            {
                case FunctionCall { Parameters.Count: > 0 } functionCall
                    when functionCall.Parameters.OfType<ColumnReferenceExpression>().FirstOrDefault() is { } columnRef:
                    Add(SargabilityFindingKind.FunctionWrappedColumn, ColumnName(columnRef), functionCall.FunctionName.Value, functionCall);
                    break;

                case CastCall { Parameter: ColumnReferenceExpression columnRef } castCall:
                    Add(SargabilityFindingKind.CastOrConvertOnColumn, ColumnName(columnRef), "CAST", castCall);
                    break;

                case ConvertCall { Parameter: ColumnReferenceExpression columnRef } convertCall:
                    Add(SargabilityFindingKind.CastOrConvertOnColumn, ColumnName(columnRef), "CONVERT", convertCall);
                    break;

                case BinaryExpression binary:
                    InspectArithmetic(binary);
                    break;
            }
        }

        private void InspectArithmetic(BinaryExpression binary)
        {
            if (binary.FirstExpression is ColumnReferenceExpression leftColumn)
            {
                Add(SargabilityFindingKind.ColumnArithmetic, ColumnName(leftColumn), binary.BinaryExpressionType.ToString(), binary);
            }
            else if (binary.SecondExpression is ColumnReferenceExpression rightColumn)
            {
                Add(SargabilityFindingKind.ColumnArithmetic, ColumnName(rightColumn), binary.BinaryExpressionType.ToString(), binary);
            }
        }

        private static string ColumnName(ColumnReferenceExpression columnRef) =>
            columnRef.MultiPartIdentifier.Identifiers[^1].Value;

        private void Add(SargabilityFindingKind kind, string columnName, string? detail, TSqlFragment node) =>
            Findings.Add(new SargabilityFinding(kind, columnName, detail, sourcePath, node.StartLine, node.StartColumn));
    }
}
