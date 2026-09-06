using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;

namespace SilentScan.Core.Predicates;

public static class RowLimitOutOfRangeScanner
{
    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    public static IReadOnlyList<RowLimitOutOfRangeFinding> Scan(SqlParseResult parseResult)
    {
        var rule = CreateRule(parseResult.SourcePath);
        var walker = new ModuleWalker(parseResult.SourcePath, new DatabaseCatalog(), EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
        return Harvest(rule);
    }

    internal static Rule CreateRule(string sourcePath) => new(sourcePath);

    internal static IReadOnlyList<RowLimitOutOfRangeFinding> Harvest(Rule rule) =>
        [
            .. rule.Findings
                .OrderBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column),
        ];

    internal sealed class Rule(string sourcePath) : IModuleRule
    {
        public List<RowLimitOutOfRangeFinding> Findings { get; } = [];

        public void OnEnterTopRowFilter(TopRowFilter node, ModuleWalker walker)
        {
            if (!node.Percent
                && LiteralComparisonFolder.TryFoldToNumeric(node.Expression) is { } value
                && value < 0)
            {
                Add(RowLimitOutOfRangeKind.TopRowCountNegative, value, node);
            }
        }

        public void OnEnterOffsetClause(OffsetClause node, ModuleWalker walker)
        {
            if (LiteralComparisonFolder.TryFoldToNumeric(node.OffsetExpression) is { } offset && offset < 0)
            {
                Add(RowLimitOutOfRangeKind.OffsetNegative, offset, node.OffsetExpression);
            }

            if (node.FetchExpression is { } fetchExpression
                && LiteralComparisonFolder.TryFoldToNumeric(fetchExpression) is { } fetch
                && fetch <= 0)
            {
                Add(RowLimitOutOfRangeKind.FetchNotPositive, fetch, fetchExpression);
            }
        }

        public void OnEnterTableSampleClause(TableSampleClause node, ModuleWalker walker)
        {
            if (LiteralComparisonFolder.TryFoldToNumeric(node.SampleNumber) is not { } value)
            {
                return;
            }

            switch (node.TableSampleClauseOption)
            {
                case TableSampleClauseOption.Percent when value < 0 || value > 100:
                    Add(RowLimitOutOfRangeKind.TableSamplePercentOutOfRange, value, node.SampleNumber);
                    break;

                case TableSampleClauseOption.Rows when value <= 0:
                    Add(RowLimitOutOfRangeKind.TableSampleRowsNotPositive, value, node.SampleNumber);
                    break;
            }
        }

        private void Add(RowLimitOutOfRangeKind kind, decimal value, TSqlFragment location) =>
            Findings.Add(new RowLimitOutOfRangeFinding(kind, value.ToString(System.Globalization.CultureInfo.InvariantCulture), sourcePath, location.StartLine, location.StartColumn));
    }
}
