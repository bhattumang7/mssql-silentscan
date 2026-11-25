using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Parsing;
using SilentScan.Core.Rules;
using SilentScan.Core.TypeInference;

namespace SilentScan.Tests.TypeInference;

public sealed class ExpressionOperatorMatrixTests
{
    private static SqlType FromSyntax(string syntax)
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        var fragment = parser.Parse(new StringReader($"DECLARE @x {syntax};"), out var errors);
        Assert.Empty(errors);
        var declare = (DeclareVariableStatement)((TSqlScript)fragment).Batches[0].Statements[0];
        return SqlTypeReferenceResolver.Resolve(declare.Declarations[0].DataType, columnCollation: null)!;
    }

    private static string Sql(string op) => op switch
    {
        "COALESCE" => "SELECT COALESCE(@l, @r)",
        "CASE" => "SELECT CASE WHEN @l IS NULL THEN @l ELSE @r END",
        "IIF" => "SELECT IIF(@l IS NULL, @l, @r)",
        _ => $"SELECT @l {op} @r",
    };

    public static TheoryData<string> Operators() => ["+", "-", "*", "/", "%", "COALESCE", "CASE", "IIF"];

    [Theory]
    [MemberData(nameof(Operators))]
    public void ResolvedResultType_MatchesTheEngineForEveryOperandPair(string op)
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        var mismatches = new List<string>();
        foreach (var cell in OperatorMatrix.Instance.Entries)
        {
            var engineText = OperatorMatrix.Instance.TryGetResult(op, cell.Left, cell.Right)!;
            if (engineText.StartsWith(OperatorMatrix.ErrorPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var left = FromSyntax(cell.Left);
            var right = FromSyntax(cell.Right);
            var fragment = parser.Parse(new StringReader(Sql(op)), out var errors);
            Assert.Empty(errors);
            var select = (SelectStatement)((TSqlScript)fragment).Batches[0].Statements[0];
            var expression = ((SelectScalarExpression)((QuerySpecification)select.QueryExpression).SelectElements[0]).Expression;
            var ours = ExpressionTypeInferencer.Resolve(expression, e => e is VariableReference v ? (v.Name == "@l" ? left : right) : null, null);
            var engine = FromSyntax(engineText);
            var same = ours is not null
                && ours.Category == engine.Category
                && (!ours.LengthKnown || ours.Length is null || ours.Length == engine.Length)
                && (ours.Scale is null || ours.Scale == engine.Scale)
                && (ours.Precision is null || ours.Precision == engine.Precision)
                && ours.IsMax == engine.IsMax;
            if (!same)
            {
                mismatches.Add($"{cell.Left} {op} {cell.Right}: ours {ours?.ToString() ?? "unresolved"} vs engine {engineText}");
            }
        }

        Assert.True(mismatches.Count == 0, $"{mismatches.Count} operand pairs differ from the engine:\n" + string.Join("\n", mismatches.Take(25)));
    }
}
