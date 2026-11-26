using System.Globalization;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public static class ModulePlanComparer
{
    public static IReadOnlyList<ModuleComparisonRow> Compare(
        IReadOnlyList<TypedPredicateFinding> moduleFindings, IReadOnlyList<PlannedStatement> statements)
    {
        var claims = moduleFindings.Where(f => f.Verdict is Verdict.ScanForced or Verdict.RangeSeek).ToList();
        var otherVerdictFindings = moduleFindings.Where(f => f.Verdict is not (Verdict.ScanForced or Verdict.RangeSeek) && f.DynamicSqlCallSite is null).ToList();
        var rows = new List<ModuleComparisonRow>();
        var locatedCount = statements.Count(s => s.StartLine is not null && s.EndLine is not null);
        var seen = new HashSet<(int Statement, string Table, string Column, ModuleComparisonOutcome Outcome)>();
        var claimsByStatement = new Dictionary<int, List<(string? Schema, string Table, string Column)>>();

        foreach (var claim in claims)
        {
            CompareClaim(claim, statements, locatedCount, claimsByStatement, seen, rows);
        }

        AddEngineOnlyRows(statements, claimsByStatement, otherVerdictFindings, seen, rows);

        return rows;
    }

    private static void CompareClaim(
        TypedPredicateFinding claim,
        IReadOnlyList<PlannedStatement> statements,
        int locatedCount,
        Dictionary<int, List<(string? Schema, string Table, string Column)>> claimsByStatement,
        HashSet<(int Statement, string Table, string Column, ModuleComparisonOutcome Outcome)> seen,
        List<ModuleComparisonRow> rows)
    {
        void AddNotDecided(ModuleNotDecidedReason reason, string? detail) =>
            AddNotDecidedRow(rows, seen, reason, SplitQualifiedName(claim.Column.TableQualifiedName).Table, claim.Column.ColumnName, claim, detail);

        var (schema, table) = SplitQualifiedName(claim.Column.TableQualifiedName);
        var column = claim.Column.ColumnName;

        if (claim.DynamicSqlCallSite is not null)
        {
            AddNotDecided(ModuleNotDecidedReason.DynamicSql, null);
            return;
        }

        var index = FindStatement(statements, claim.Line);
        if (index < 0)
        {
            AddNotDecided(ModuleNotDecidedReason.StatementNotLocated, string.Create(CultureInfo.InvariantCulture, $"finding line {claim.Line}; {locatedCount} of {statements.Count} plan statements located"));
            return;
        }

        if (!claimsByStatement.TryAdd(index, [(schema, table, column)]))
        {
            claimsByStatement[index].Add((schema, table, column));
        }

        var statement = statements[index];
        var converted = statement.Conversions.Any(c => Matches(c, schema, table, column));
        if (!converted && statement.Conversions.Any(c => c.Table is null && string.Equals(c.Column, column, StringComparison.OrdinalIgnoreCase)))
        {
            AddNotDecided(ModuleNotDecidedReason.ConversionNotAttributable, "the plan converts a column of this name but does not say which table it belongs to");
            return;
        }

        var outcome = converted ? ModuleComparisonOutcome.Agree : ModuleComparisonOutcome.ToolOnly;
        if (seen.Add((index, table.ToUpperInvariant(), column.ToUpperInvariant(), outcome)))
        {
            rows.Add(new ModuleComparisonRow(outcome, ModuleNotDecidedReason.None, table, column, statement.StartLine, claim));
        }
    }

    private static void AddEngineOnlyRows(
        IReadOnlyList<PlannedStatement> statements,
        Dictionary<int, List<(string? Schema, string Table, string Column)>> claimsByStatement,
        List<TypedPredicateFinding> otherVerdictFindings,
        HashSet<(int Statement, string Table, string Column, ModuleComparisonOutcome Outcome)> seen,
        List<ModuleComparisonRow> rows)
    {
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
                        ModuleComparisonOutcome.EngineOnly, ModuleNotDecidedReason.None, conversion.Table, conversion.Column, statements[i].StartLine, OtherVerdictFindingFor(otherVerdictFindings, statements[i], conversion), conversion.ConvertedToDataType, conversion.Context));
                }
            }
        }
    }

    private static void AddNotDecidedRow(
        List<ModuleComparisonRow> rows,
        HashSet<(int Statement, string Table, string Column, ModuleComparisonOutcome Outcome)> seen,
        ModuleNotDecidedReason reason, string table, string column, TypedPredicateFinding claim, string? detail)
    {
        var key = (NotDecidedOrder(reason), table.ToUpperInvariant(), column.ToUpperInvariant(), ModuleComparisonOutcome.NotDecided);
        if (seen.Add(key))
        {
            rows.Add(new ModuleComparisonRow(ModuleComparisonOutcome.NotDecided, reason, table, column, null, claim, detail));
        }
    }

    private static TypedPredicateFinding? OtherVerdictFindingFor(
        List<TypedPredicateFinding> findings, PlannedStatement statement, ConvertImplicitFinding conversion) =>
        findings.FirstOrDefault(f =>
        {
            var (schema, table) = SplitQualifiedName(f.Column.TableQualifiedName);
            return f.Line >= statement.StartLine && f.Line <= statement.EndLine && Matches(conversion, schema, table, f.Column.ColumnName);
        });

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
        && (schema is null || conversion.Schema is null || string.Equals(conversion.Schema, schema, StringComparison.OrdinalIgnoreCase))
        && string.Equals(conversion.Column, column, StringComparison.OrdinalIgnoreCase);

    private static (string? Schema, string Table) SplitQualifiedName(string qualifiedName)
    {
        var parts = qualifiedName.Split('.', 2);
        return parts.Length == 2 ? (parts[0], parts[1]) : (null, parts[0]);
    }

    private static int NotDecidedOrder(ModuleNotDecidedReason reason) => reason switch
    {
        ModuleNotDecidedReason.DynamicSql => -1,
        ModuleNotDecidedReason.ConversionNotAttributable => -3,
        _ => -2,
    };
}
