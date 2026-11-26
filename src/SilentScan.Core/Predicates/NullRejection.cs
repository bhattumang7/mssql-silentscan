using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Core.Predicates;

internal static class NullRejection
{
    public static IEnumerable<ColumnReferenceExpression> Columns(BooleanExpression conjunct) =>
        Operands(conjunct).Select(Unwrap).OfType<ColumnReferenceExpression>();

    public static IEnumerable<BooleanExpression?> PreservedInnerJoinConditions(FromClause? fromClause) =>
        fromClause is null ? [] : fromClause.TableReferences.SelectMany(PreservedInnerJoinConditions);

    private static IEnumerable<BooleanExpression?> PreservedInnerJoinConditions(TableReference reference) =>
        reference switch
        {
            QualifiedJoin { QualifiedJoinType: QualifiedJoinType.Inner } join =>
                [join.SearchCondition, .. PreservedInnerJoinConditions(join.FirstTableReference), .. PreservedInnerJoinConditions(join.SecondTableReference)],
            QualifiedJoin { QualifiedJoinType: QualifiedJoinType.LeftOuter } join => PreservedInnerJoinConditions(join.FirstTableReference),
            QualifiedJoin { QualifiedJoinType: QualifiedJoinType.RightOuter } join => PreservedInnerJoinConditions(join.SecondTableReference),
            _ => [],
        };

    private static IEnumerable<ScalarExpression> Operands(BooleanExpression conjunct) =>
        conjunct switch
        {
            BooleanIsNullExpression { IsNot: true } isNotNull => [isNotNull.Expression],
            BooleanComparisonExpression comparison when comparison.ComparisonType is not (BooleanComparisonType.IsDistinctFrom or BooleanComparisonType.IsNotDistinctFrom) =>
                [comparison.FirstExpression, comparison.SecondExpression],
            BooleanTernaryExpression { TernaryExpressionType: BooleanTernaryExpressionType.Between } between =>
                [between.FirstExpression, between.SecondExpression, between.ThirdExpression],
            BooleanTernaryExpression ternary => [ternary.FirstExpression],
            InPredicate { Subquery: null } list => [list.Expression],
            InPredicate { NotDefined: false } membership => [membership.Expression],
            LikePredicate like => [like.FirstExpression, like.SecondExpression],
            _ => [],
        };

    private static ScalarExpression Unwrap(ScalarExpression expression) =>
        expression is ParenthesisExpression parenthesis ? Unwrap(parenthesis.Expression) : expression;
}
