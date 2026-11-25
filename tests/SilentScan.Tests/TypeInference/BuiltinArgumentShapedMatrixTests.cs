using System.Globalization;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Parsing;
using SilentScan.Core.Rules;
using SilentScan.Core.TypeInference;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.TypeInference;

public sealed class BuiltinArgumentShapedMatrixTests
{
    private const string CollateMarker = " COLLATE ";

    private static SqlType FromSyntax(string syntax)
    {
        var collateAt = syntax.IndexOf(CollateMarker, StringComparison.OrdinalIgnoreCase);
        var typeText = collateAt < 0 ? syntax : syntax[..collateAt];
        var collation = collateAt < 0 ? null : syntax[(collateAt + CollateMarker.Length)..].Trim();
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        var fragment = parser.Parse(new StringReader($"DECLARE @x {typeText};"), out var errors);
        Assert.Empty(errors);
        var declare = (DeclareVariableStatement)((TSqlScript)fragment).Batches[0].Statements[0];
        return SqlTypeReferenceResolver.Resolve(declare.Declarations[0].DataType, columnCollation: collation is null ? null : new Identifier { Value = collation })!;
    }

    private static string Text(BuiltinProbe probe)
    {
        var text = probe.Template;
        for (var i = 0; i < probe.Positions.Count; i++)
        {
            var index = i.ToString(CultureInfo.InvariantCulture);
            text = text.Replace("{" + index + "}", "@a" + index, StringComparison.Ordinal);
        }

        return text;
    }

    private static ScalarExpression ParseExpression(string text)
    {
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        var fragment = parser.Parse(new StringReader("SELECT " + text), out var errors);
        Assert.True(errors.Count == 0, text + ": " + string.Join("; ", errors.Select(e => e.Message)));
        var select = (SelectStatement)((TSqlScript)fragment).Batches[0].Statements[0];
        return ((SelectScalarExpression)((QuerySpecification)select.QueryExpression).SelectElements[0]).Expression;
    }

    internal static bool Agrees(SqlType ours, SqlType engine) =>
        ours.Category == engine.Category
        && (!ours.LengthKnown || ours.Length is null || ours.Length == engine.Length)
        && (ours.Scale is null || ours.Scale == engine.Scale)
        && (ours.Precision is null || ours.Precision == engine.Precision)
        && ours.IsMax == engine.IsMax;

    internal static (List<string> Wrong, List<string> Unresolved) Compare()
    {
        var wrong = new List<string>();
        var unresolved = new List<string>();
        foreach (var probe in BuiltinProbes.All)
        {
            var expression = ParseExpression(Text(probe));
            foreach (var shape in BuiltinMatrixGenerator.Shapes(probe))
            {
                var cell = BuiltinMatrix.Instance.TryGet(probe.Function, BuiltinMatrixGenerator.ShapeText(shape.Types));
                if (cell is null || cell.ResultType.StartsWith(BuiltinMatrix.ErrorPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var argumentTypes = shape.Types.Select(FromSyntax).ToList();
                var ours = ExpressionTypeInferencer.Resolve(
                    expression,
                    e => e is VariableReference v && v.Name.StartsWith("@a", StringComparison.Ordinal)
                        ? argumentTypes[int.Parse(v.Name[2..], CultureInfo.InvariantCulture)]
                        : null,
                    null);
                var label = $"{Text(probe)} [{cell.Shape}]";
                if (ours is null)
                {
                    unresolved.Add(label);
                }
                else if (!Agrees(ours, FromSyntax(cell.ResultType)))
                {
                    wrong.Add($"{label}: ours {ours} vs engine {cell.ResultType}");
                }
            }
        }

        return (wrong, unresolved);
    }

    [Fact]
    public void ResolvedBuiltinResultType_NeverDisagreesWithTheEngineForAProbedArgumentShape()
    {
        var (wrong, _) = Compare();

        Assert.True(wrong.Count == 0, $"{wrong.Count} builtin cells differ from the engine:\n" + string.Join("\n", wrong));
    }

    [Fact]
    public void EveryProbedBuiltinShape_ResolvesToAType()
    {
        var (_, unresolved) = Compare();

        Assert.True(unresolved.Count == 0, $"{unresolved.Count} builtin cells have no inferred type:\n" + string.Join("\n", unresolved));
    }
}
