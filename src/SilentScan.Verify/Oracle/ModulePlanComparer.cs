using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public static class ModulePlanComparer
{
    public static IReadOnlyList<ModuleComparisonRow> Compare(
        IReadOnlyList<TypedPredicateFinding> moduleFindings, IReadOnlyList<PlannedStatement> statements)
    {
        var claims = moduleFindings.Where(f => f.Verdict is Verdict.ScanForced or Verdict.RangeSeek).ToList();
        var rows = new List<ModuleComparisonRow>();
        var seen = new HashSet<(int Statement, string Table, string Column, ModuleComparisonOutcome Outcome)>();
        var claimsByStatement = new Dictionary<int, List<(string? Schema, string Table, string Column)>>();

        foreach (var claim in claims)
        {
            var (schema, table) = SplitQualifiedName(claim.Column.TableQualifiedName);
            var column = claim.Column.ColumnName;

            if (claim.DynamicSqlCallSite is not null)
            {
                AddNotDecided(ModuleNotDecidedReason.DynamicSql);
                continue;
            }

            var index = FindStatement(statements, claim.Line);
            if (index < 0)
            {
                AddNotDecided(ModuleNotDecidedReason.StatementNotLocated);
                continue;
            }

            if (!claimsByStatement.TryGetValue(index, out var statementClaims))
            {
                statementClaims = [];
                claimsByStatement[index] = statementClaims;
            }

            statementClaims.Add((schema, table, column));

            var statement = statements[index];
            var converted = statement.Conversions.Any(c => Matches(c, schema, table, column));
            var outcome = converted ? ModuleComparisonOutcome.Agree : ModuleComparisonOutcome.ToolOnly;
            if (seen.Add((index, table.ToUpperInvariant(), column.ToUpperInvariant(), outcome)))
            {
                rows.Add(new ModuleComparisonRow(outcome, ModuleNotDecidedReason.None, table, column, statement.StartLine, claim));
            }

            void AddNotDecided(ModuleNotDecidedReason reason)
            {
                var key = (reason == ModuleNotDecidedReason.DynamicSql ? -1 : -2, table.ToUpperInvariant(), column.ToUpperInvariant(), ModuleComparisonOutcome.NotDecided);
                if (seen.Add(key))
                {
                    rows.Add(new ModuleComparisonRow(ModuleComparisonOutcome.NotDecided, reason, table, column, null, claim));
                }
            }
        }

        for (var i = 0; i < statements.Count; i++)
        {
            if (statements[i].StartLine is null || statements[i].EndLine is null)
            {
                continue;
            }

            claimsByStatement.TryGetValue(i, out var statementClaims);
            foreach (var conversion in statements[i].Conversions)
            {
                if (conversion.Table is null || conversion.Column is null)
                {
                    continue;
                }

                var claimed = statementClaims?.Any(c => Matches(conversion, c.Schema, c.Table, c.Column)) ?? false;
                if (!claimed && seen.Add((i, (conversion.Schema + "." + conversion.Table).ToUpperInvariant(), conversion.Column.ToUpperInvariant(), ModuleComparisonOutcome.EngineOnly)))
                {
                    rows.Add(new ModuleComparisonRow(
                        ModuleComparisonOutcome.EngineOnly, ModuleNotDecidedReason.None, conversion.Table, conversion.Column, statements[i].StartLine, Finding: null));
                }
            }
        }

        return rows;
    }

    private static int FindStatement(IReadOnlyList<PlannedStatement> statements, int line)
    {
        var best = -1;
        var bestSpan = int.MaxValue;
        for (var i = 0; i < statements.Count; i++)
        {
            var s = statements[i];
            if (s.StartLine is { } start && s.EndLine is { } end && line >= start && line <= end && end - start < bestSpan)
            {
                best = i;
                bestSpan = end - start;
            }
        }

        return best;
    }

    private static bool Matches(ConvertImplicitFinding conversion, string? schema, string table, string column) =>
        string.Equals(conversion.Table, table, StringComparison.OrdinalIgnoreCase)
        && (schema is null || string.Equals(conversion.Schema, schema, StringComparison.OrdinalIgnoreCase))
        && string.Equals(conversion.Column, column, StringComparison.OrdinalIgnoreCase);

    private static (string? Schema, string Table) SplitQualifiedName(string qualifiedName)
    {
        var parts = qualifiedName.Split('.', 2);
        return parts.Length == 2 ? (parts[0], parts[1]) : (null, parts[0]);
    }
}
